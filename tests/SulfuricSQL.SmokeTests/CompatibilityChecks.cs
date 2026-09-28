using MySqlConnector;
using SulfuricSQL.Services;

internal static class CompatibilityChecks
{
    public static async Task RunAsync(MySqlConnection connection, string database)
    {
        int checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("Compatibility FAIL: " + name); checks++; Console.WriteLine("Compatibility PASS: " + name); }
        IReadOnlyList<CompatibilityIssue> Rules(string version, string sql) => SqlCompatibilityChecker.Check(ServerCompatibility.Parse(version), SqlSafetyChecker.Analyze(sql));
        foreach (string version in new[] { "5.6.51", "5.7.44", "8.0.15", "8.0.16", "8.0.17", "8.0.20", "8.4.0" })
            Check(Rules(version, "SELECT id FROM t WHERE id=1; INSERT INTO t(id) VALUES(2);").Count == 0, version + " basic SQL remains compatible");
        Check(Rules("5.7.44", "WITH x AS (SELECT 1 n) SELECT n FROM x;").Any(i => i.Blocking), "5.7 blocks CTE");
        Check(Rules("8.0.11", "WITH x AS (SELECT 1 n) SELECT n FROM x;").Count == 0, "8.0 permits CTE");
        Check(Rules("5.6.51", "SELECT ROW_NUMBER() OVER (ORDER BY id) FROM t;").Any(i => i.Blocking), "5.6 blocks windows");
        Check(Rules("8.4.0", "SELECT ROW_NUMBER() OVER (ORDER BY id) FROM t;").Count == 0, "8.4 permits windows");
        Check(Rules("5.7.44", "CREATE DATABASE x COLLATE `utf8mb4_0900_ai_ci`;").Any(i => i.Blocking), "quoted 8.0 collation blocked on 5.7");
        Check(Rules("5.7.44", "CREATE DATABASE x COLLATE utf8mb4_unicode_ci;").Count == 0, "older collation allowed");
        Check(Rules("8.0.15", "CREATE TABLE t(n INT CHECK(n>0));").Any(i => !i.Blocking), "CHECK enforcement threshold warning");
        Check(Rules("8.0.16", "CREATE TABLE t(n INT CHECK(n>0));").Count == 0, "CHECK enforced from 8.0.16");
        Check(Rules("5.7.44", "SELECT SQL_CACHE 1;").Count == 0, "old query cache modifier allowed on 5.7");
        Check(Rules("8.0.46", "SELECT SQL_CACHE 1;").Any(i => i.Blocking), "removed query cache blocked on 8.0");
        Check(Rules("8.0.16", "SELECT SQL_CALC_FOUND_ROWS 1;").Count == 0, "found rows before deprecation");
        Check(Rules("8.0.17", "SELECT SQL_CALC_FOUND_ROWS 1;").Any(i => !i.Blocking), "found rows deprecation boundary");
        Check(Rules("8.4.0", "INSERT INTO t(id) VALUES(1) ON DUPLICATE KEY UPDATE id=VALUES(id);").Any(i => !i.Blocking), "legacy VALUES function warns");
        Check(Rules("8.4.0", "SELECT 'SQL_CACHE', `SQL_CALC_FOUND_ROWS` FROM t; -- SQL_CACHE\n").Count == 0, "strings comments and identifiers ignored");
        Check(Rules("5.5.5-10.11.6-MariaDB", "SELECT SQL_CACHE 1;").Count == 0, "MariaDB is not mistaken for MySQL 5.5");
        Check(Rules("unknown", "SELECT SQL_CACHE 1;").Count == 0, "unknown version does not invent restrictions");
        var service = new SqlExecutionService(connection);
        int confirmations = 0;
        using (var declined = await service.ExecuteAsync(database, "SELECT SQL_NO_CACHE 1;", _ => true, default, _ => { confirmations++; return false; }))
            Check(declined.Cancelled && declined.Results.Count == 0, "declining compatibility dialog executes nothing");
        using (var allowed = await service.ExecuteAsync(database, "SELECT SQL_NO_CACHE 1;", _ => true, default, _ => { confirmations++; return true; }))
            Check(allowed.Success && allowed.CompatibilityIssues.Count == 1, "accepted deprecated SQL executes and retains warning");
        string table = "compat_" + Guid.NewGuid().ToString("N")[..8];
        using (var blocked = await service.ExecuteAsync(database, $"CREATE TABLE `{table}`(id INT); SELECT SQL_CACHE 1;", _ => true, default, _ => true))
            Check(!blocked.Success && !blocked.ChangesSchema, "unsupported later statement blocks entire batch");
        using var exists = new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=@db AND TABLE_NAME=@table", connection);
        exists.Parameters.AddWithValue("@db", database); exists.Parameters.AddWithValue("@table", table);
        Check(Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0, "blocked batch created no table");
        Check(confirmations == 2, "compatibility callbacks run once per script");
        Console.WriteLine($"All {checks} compatibility checks passed (version rules simulated; live execution on {connection.ServerVersion}).");
    }
}
