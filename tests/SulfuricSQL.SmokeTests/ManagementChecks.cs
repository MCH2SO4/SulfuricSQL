using MySqlConnector;
using SulfuricSQL.Services;

internal static class ManagementChecks
{
    public static async Task RunAsync(MySqlConnection connection)
    {
        var service = new SqlExecutionService(connection);
        var manager = new DatabaseService(connection);
        string db = "sulfuricsql_management_" + Guid.NewGuid().ToString("N")[..8];
        int checks = 0, confirmations = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new Exception("Management FAIL: " + name);
            Console.WriteLine("Management PASS: " + name); checks++;
        }
        async Task<SqlBatchResult> Run(string sql, string? database = null, bool approve = true)
        {
            var result = await service.ExecuteAsync(database, sql, _ => { confirmations++; return approve; }, default);
            return result;
        }
        void Success(SqlBatchResult result, string name) => Check(result.Success, name + " " + string.Join(" ", result.Results.Select(r => r.Error)));
        try
        {
            using (var create = await Run(DatabaseService.CreateDatabase(db, "utf8mb4", "utf8mb4_general_ci"))) Success(create, "CREATE DATABASE");
            using (var use = await Run($"USE `{db}`;")) { Success(use, "USE"); Check(use.CurrentDatabase == db, "current database follows USE"); }
            using (var createTable = await Run(DatabaseService.CreateTable(db, "学生", [new("id", "INT", "", true, false, true), new("name", "VARCHAR", "80", false, true, false)]), db)) Success(createTable, "CREATE TABLE from field definitions");
            using (var insert = await Run("INSERT INTO 学生(name) VALUES ('小明'),('小红');", db)) { Success(insert, "INSERT"); Check(insert.Results[0].AffectedRows == 2, "INSERT affected rows"); }
            using (var select = await Run("SELECT * FROM 学生 ORDER BY id;", db)) { Success(select, "SELECT"); Check(select.Results[0].Rows?.Rows.Count == 2, "SELECT grid data"); }
            using (var update = await Run("UPDATE 学生 SET name='小明同学' WHERE id=1;", db)) { Success(update, "UPDATE with WHERE"); Check(update.Results[0].AffectedRows == 1, "UPDATE affected rows"); }
            using (var unchanged = await Run("UPDATE 学生 SET name='小明同学' WHERE id=1;", db)) Check(unchanged.Success && unchanged.Results[0].AffectedRows == 0, "unchanged UPDATE reports zero modified rows");
            using (var delete = await Run("DELETE FROM 学生 WHERE id=2;", db)) { Success(delete, "DELETE with WHERE"); Check(delete.Results[0].AffectedRows == 1, "DELETE affected rows"); }
            using (var verify = await Run("SELECT name FROM 学生 WHERE id=1;", db)) Check(Convert.ToString(verify.Results[0].Rows!.Rows[0][0]) == "小明同学", "writes really persisted");
            Check(confirmations == 0, "WHERE-qualified writes do not need destructive confirmation");

            using (var props = await manager.GetPropertiesAsync(db, default)) Check(props.Rows.Count == 1 && props.Rows[0][1].ToString() == "utf8mb4", "database properties");
            using (var collations = await manager.GetCollationsAsync(default)) Check(collations.Rows.Count > 0, "charset and collation choices from server");
            using (var alter = await Run(DatabaseService.AlterDatabase(db, "utf8mb4", "utf8mb4_unicode_ci"), db)) Success(alter, "ALTER database collation");
            using (var props = await manager.GetPropertiesAsync(db, default)) Check(props.Rows[0][2].ToString() == "utf8mb4_unicode_ci", "new default collation verified");
            using (var rename = await Run(DatabaseService.RenameTable(db, "学生", "renamed"), db)) Success(rename, "RENAME TABLE");

            foreach (string sql in new[]
            {
                "UPDATE renamed SET name='WHERE';", "DELETE FROM renamed /* WHERE id=1 */;",
                "UPDATE renamed SET name=(SELECT 'x' WHERE 1=1);",
                "WITH x AS (SELECT 1 WHERE 1=1) UPDATE renamed SET name='x';",
                "UPDATE renamed SET name=@where;", "DELETE FROM example.where;", "UPDATE renamed SET renamed.where=1;",
                "TRUNCATE TABLE renamed;", "DROP TABLE renamed;", $"DROP DATABASE `{db}`;"
            })
            {
                int before = confirmations;
                using var blocked = await Run(sql, db, false);
                Check(blocked.Cancelled && blocked.Results.Count == 0 && confirmations == before + 1, "dangerous SQL requires confirmation and Cancel prevents execution");
            }
            using (var stillPresent = await Run("SELECT COUNT(*) FROM renamed", db)) Check(Convert.ToInt32(stillPresent.Results[0].Rows!.Rows[0][0]) == 1, "cancelled operations left data intact");
            using (var wholeBatch = await Run("INSERT INTO renamed(name) VALUES ('must_not_run'); DELETE FROM renamed;", db, false)) Check(wholeBatch.Cancelled && wholeBatch.Results.Count == 0, "whole batch confirmed before first write");
            foreach (string system in new[] { "information_schema", "mysql", "performance_schema", "sys", "MySQL", "#mysql50#mysql" })
            {
                int before = confirmations;
                using var blocked = await Run($"DROP SCHEMA IF EXISTS `{system}`", db);
                Check(!blocked.Success && blocked.Results.Single().Error!.Contains("禁止删除系统数据库") && confirmations == before, "system database drop blocked before confirmation: " + system);
            }
            using (var mixed = await Run("INSERT INTO renamed(name) VALUES ('must_not_run'); DROP DATABASE mysql;", db)) Check(!mixed.Success && mixed.Results.Count == 1, "system protection preflights complete script");
            using (var truncate = await Run(DatabaseService.TruncateTable(db, "renamed"), db)) Success(truncate, "confirmed TRUNCATE");
            using (var empty = await Run("SELECT COUNT(*) FROM renamed", db)) Check(Convert.ToInt32(empty.Results[0].Rows!.Rows[0][0]) == 0, "TRUNCATE actually emptied table");

            using (var partial = await Run("INSERT INTO renamed(name) VALUES ('before_error'); INSERT INTO missing_table VALUES(1); INSERT INTO renamed(name) VALUES ('after_error');", db))
                Check(!partial.Success && partial.Results.Count == 2 && partial.Results[1].Error!.Contains("1146"), "batch stops on MySQL error and preserves details");
            using (var verify = await Run("SELECT name FROM renamed", db)) Check(verify.Results[0].Rows!.Rows.Count == 1 && verify.Results[0].Rows!.Rows[0][0].ToString() == "before_error", "completed statements remain; later statements skipped");
            using (var results = await Run("SELECT 'a;b' AS text; SHOW TABLES; DESCRIBE renamed; EXPLAIN SELECT * FROM renamed;", db)) Check(results.Success && results.Results.Count == 4 && results.Results.All(r => r.Rows != null), "multiple result sets and quoted semicolon");
            using (var error = await Run("SELECT FROM", db)) Check(!error.Success && error.Results[0].Error!.Contains("1064") && error.Results[0].Error!.Contains("SQLSTATE"), "syntax error returned without throwing");
            using (var cts = new CancellationTokenSource(250))
            {
                using var cancelled = await service.ExecuteAsync(db, "SELECT SLEEP(5); INSERT INTO renamed(name) VALUES ('after_stop')", _ => true, cts.Token);
                Check(cancelled.Cancelled && cancelled.Results.Count == 1, "Stop aborts running statement and skips subsequent statements");
            }
            using (var deniedContext = await Run("DELETE FROM renamed", null, true)) Check(!deniedContext.Success && deniedContext.Results[0].Error!.Contains("选择当前数据库"), "no invisible current database when none selected");
            Check(SqlTemplates.Create(db, "renamed").Count >= 15, "common SQL templates available");
            using (var dropTable = await Run(DatabaseService.DropTable(db, "renamed"), db)) Success(dropTable, "confirmed DROP TABLE");
            using (var dropDb = await Run(DatabaseService.DropDatabase(db), db)) { Success(dropDb, "confirmed DROP DATABASE"); Check(dropDb.CurrentDatabase == null, "dropping current database clears current context"); }
            Console.WriteLine($"All {checks} management checks passed. Full CREATE→INSERT→SELECT→UPDATE→DELETE→DROP flow completed.");
        }
        finally
        {
            using var cleanup = new MySqlCommand($"DROP DATABASE IF EXISTS `{db}`", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
