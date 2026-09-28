using System.Data;
using System.Diagnostics;
using MySqlConnector;

namespace SulfuricSQL.Services;

public sealed class SqlExecutionResult
{
    public string Sql { get; init; } = "";
    public string Kind { get; init; } = "";
    public DataTable? Rows { get; set; }
    public long AffectedRows { get; set; }
    public long Milliseconds { get; set; }
    public bool Truncated { get; set; }
    public string? Error { get; set; }
    public bool Success => Error == null;
}

public sealed class SqlBatchResult : IDisposable
{
    public List<SqlExecutionResult> Results { get; } = [];
    public IReadOnlyList<CompatibilityIssue> CompatibilityIssues { get; set; } = [];
    public string? CurrentDatabase { get; set; }
    public bool Cancelled { get; set; }
    public bool ChangesSchema { get; set; }
    public bool ChangesData { get; set; }
    public bool Success => !Cancelled && Results.Count > 0 && Results.All(r => r.Success);
    public void Dispose() { foreach (var result in Results) result.Rows?.Dispose(); }
}

public sealed class SqlExecutionService(MySqlConnection connection)
{
    // 先检查整个脚本并确认，再逐句执行；出错或停止后不继续后面的语句。
    public async Task<SqlBatchResult> ExecuteAsync(string? database, string sql, Func<string, bool> confirm, CancellationToken token, Func<IReadOnlyList<CompatibilityIssue>, bool>? confirmCompatibility = null)
    {
        var batch = new SqlBatchResult { CurrentDatabase = database };
        try
        {
            using var modeCommand = new MySqlCommand("SELECT @@SESSION.sql_mode", connection);
            var modes = (Convert.ToString(await modeCommand.ExecuteScalarAsync(token)) ?? "").Split(',');
            var statements = SqlSafetyChecker.Analyze(sql, modes.Contains("NO_BACKSLASH_ESCAPES"), modes.Contains("ANSI_QUOTES"));
            batch.CompatibilityIssues = SqlCompatibilityChecker.Check(ServerCompatibility.Parse(connection.ServerVersion), statements);
            if (batch.CompatibilityIssues.Count > 0)
            {
                bool proceed = confirmCompatibility?.Invoke(batch.CompatibilityIssues) ?? false;
                if (batch.CompatibilityIssues.Any(i => i.Blocking))
                    throw new ArgumentException("版本兼容检查未通过：" + string.Join("；", batch.CompatibilityIssues.Where(i => i.Blocking).Select(i => i.Message)));
                if (!proceed) { batch.Cancelled = true; return batch; }
            }
            bool hasDatabase = !string.IsNullOrEmpty(database);
            foreach (var statement in statements)
            {
                if (statement.Kind == "USE") hasDatabase = true;
                if (!hasDatabase && statement.RequiresDatabase) throw new ArgumentException("请先选择当前数据库，或在脚本前加入 USE 数据库名。");
            }
            var dangerous = statements.Where(s => s.Risk != null).ToList();
            if (dangerous.Count > 0 && !confirm($"开始时的数据库：{database ?? "未选择"}\n脚本按顺序执行；USE 会切换目标数据库。\n\n" +
                string.Join("\n", dangerous.Select(s => s.Risk)) + "\n\n完整待执行 SQL：\n" + sql))
            { batch.Cancelled = true; return batch; }
            token.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(database)) await connection.ChangeDatabaseAsync(database, token);
            foreach (var statement in statements)
            {
                token.ThrowIfCancellationRequested();
                var result = new SqlExecutionResult { Sql = statement.Text, Kind = statement.Kind };
                batch.Results.Add(result);
                batch.ChangesSchema |= statement.ChangesSchema;
                batch.ChangesData |= statement.Kind is "INSERT" or "UPDATE" or "DELETE" or "REPLACE";
                var timer = Stopwatch.StartNew();
                try
                {
                    using var command = new MySqlCommand(statement.Text, connection) { CommandTimeout = 30 };
                    await using (var reader = await command.ExecuteReaderAsync(token))
                    {
                        if (reader.FieldCount > 0)
                        {
                            result.Rows = new DataTable();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                string name = reader.GetName(i), unique = name.Length == 0 ? "列" + (i + 1) : name;
                                for (int suffix = 2; result.Rows.Columns.Contains(unique); suffix++) unique = name + " (" + suffix + ")";
                                result.Rows.Columns.Add(unique, reader.GetFieldType(i));
                            }
                            while (await reader.ReadAsync(token))
                            {
                                if (result.Rows.Rows.Count == 500) { result.Truncated = true; continue; }
                                var values = new object[reader.FieldCount];
                                reader.GetValues(values);
                                result.Rows.Rows.Add(values);
                            }
                        }
                        while (await reader.NextResultAsync(token)) { while (await reader.ReadAsync(token)) { } }
                        result.AffectedRows = Math.Max(0, reader.RecordsAffected);
                    }
                    token.ThrowIfCancellationRequested();
                }
                catch (Exception error)
                {
                    result.Error = DescribeError(error);
                    batch.Cancelled = error is OperationCanceledException || token.IsCancellationRequested;
                    break;
                }
                finally { result.Milliseconds = timer.ElapsedMilliseconds; }
            }
        }
        catch (Exception error)
        {
            batch.Cancelled = error is OperationCanceledException || token.IsCancellationRequested;
            batch.Results.Add(new SqlExecutionResult { Sql = sql, Error = DescribeError(error) });
        }
        // USE / DROP DATABASE 可能改变服务器实际的当前库，不能只依赖连接对象的缓存。
        try
        {
            using var current = new MySqlCommand("SELECT DATABASE()", connection);
            batch.CurrentDatabase = (await current.ExecuteScalarAsync(CancellationToken.None)) as string;
        }
        catch { /* 保留已经记录的执行结果；连接状态由工作区显示。 */ }
        return batch;
    }

    public static string DescribeError(Exception error) => error switch
    {
        OperationCanceledException => "操作已停止。已完成的语句不会自动撤销，请核对数据后再执行。",
        MySqlException sql => $"MySQL {sql.Number} / SQLSTATE {sql.SqlState}: {sql.Message}",
        _ => error.Message
    };
}

