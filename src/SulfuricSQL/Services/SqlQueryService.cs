using System.Data;
using System.Diagnostics;
using MySqlConnector;

namespace SulfuricSQL.Services;

public sealed class SqlQueryService(MySqlConnection connection)
{
    public async Task<(DataTable Rows, bool Truncated, long Milliseconds)> ExecuteAsync(
        string database, string sql, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(database)) throw new ArgumentException("请先选择要查询的数据库。");
        using var modeCommand = new MySqlCommand("SELECT @@SESSION.sql_mode", connection);
        string modes = Convert.ToString(await modeCommand.ExecuteScalarAsync(token)) ?? "";
        ReadQueryPolicy.Validate(sql, modes.Split(',').Contains("NO_BACKSLASH_ESCAPES"), modes.Split(',').Contains("ANSI_QUOTES"));
        await connection.ChangeDatabaseAsync(database, token);
        var timer = Stopwatch.StartNew();
        // 服务器也以只读事务执行，避免普通数据写入。
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, true, token);
        using var command = new MySqlCommand(sql, connection, transaction) { CommandTimeout = 15 };
        var rows = new DataTable();
        bool truncated = false;
        try
        {
            await using (var reader = await command.ExecuteReaderAsync(token))
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    string label = reader.GetName(i);
                    if (label.Length == 0) label = "列" + (i + 1);
                    string unique = label;
                    for (int n = 2; rows.Columns.Contains(unique); n++) unique = label + " (" + n + ")";
                    rows.Columns.Add(unique, reader.GetFieldType(i));
                }
                while (await reader.ReadAsync(token))
                {
                    if (rows.Rows.Count == 500) { truncated = true; break; }
                    var values = new object[reader.FieldCount];
                    reader.GetValues(values);
                    rows.Rows.Add(values);
                }
            }
            await transaction.RollbackAsync(CancellationToken.None);
            // MySQL 的 SLEEP 被取消时可能返回一行而不是抛错，仍应向界面报告“已停止”。
            token.ThrowIfCancellationRequested();
            return (rows, truncated, timer.ElapsedMilliseconds);
        }
        catch { rows.Dispose(); throw; }
    }
}
