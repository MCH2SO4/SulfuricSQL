using System.Data;
using MySqlConnector;
using SulfuricSQL.Models;
using SulfuricSQL.Services;

// 只指向自己创建的临时测试实例。绝不能传入日常数据库的端口！
// 运行：dotnet run --project tests/SulfuricSQL.SmokeTests -- <临时实例端口>
if (args.Length < 1 || !uint.TryParse(args[0], out uint port) || port == 3306)
    throw new ArgumentException("请提供独立临时实例的端口（不能是 3306）。");

int passed = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

var settings = new ConnectionSettings
{
    Host = "127.0.0.1", Port = port, Username = "root",
    Password = Environment.GetEnvironmentVariable("SULFURICSQL_FIXTURE_PASSWORD") ?? ""
};
var service = new MySqlConnectionService();
foreach (var invalid in new[]
{
    new ConnectionSettings { Host = "", Username = "root" },
    new ConnectionSettings { Host = "localhost", Username = " " },
    new ConnectionSettings { Host = "localhost", Username = "root", Port = 0 },
    new ConnectionSettings { Host = "localhost", Username = "root", Port = 65536 }
})
{
    try { invalid.Validate(); throw new Exception("Invalid settings accepted"); }
    catch (ArgumentException) { Check(true, "invalid input rejected before network access"); }
}

string version = await service.TestConnectionAsync(settings);
Check(version.StartsWith("8."), "test connection returns real MySQL version");
await using var observer = await service.ConnectAsync(settings);
using var countCommand = new MySqlCommand("SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE USER = 'root'", observer);
Check(Convert.ToInt32(await countCommand.ExecuteScalarAsync()) == 1, "test connection was actually released");

var connection = await service.ConnectAsync(settings);
Check(connection.State == ConnectionState.Open, "connect keeps session open");
using (var query = new MySqlCommand("SELECT 1", connection))
    Check(Convert.ToInt32(await query.ExecuteScalarAsync()) == 1, "retained session can execute query");
Check(await connection.PingAsync(), "connection health check succeeds");
await connection.DisposeAsync();
Check(Convert.ToInt32(await countCommand.ExecuteScalarAsync()) == 1, "disconnect releases server session");

// 给临时实例设置一个含特殊字符的密码，验证连接字符串构建器。
const string fixturePassword = "Fixture;Quoted='Only!2026";
using (var change = new MySqlCommand("ALTER USER 'root'@'localhost' IDENTIFIED BY @password", observer))
{
    change.Parameters.AddWithValue("@password", fixturePassword);
    await change.ExecuteNonQueryAsync();
}
settings.Password = fixturePassword;
Check((await service.TestConnectionAsync(settings)).Length > 0, "password with semicolon and quotes connects");
settings.Password = "deliberately-wrong-fixture-password";
try { await service.TestConnectionAsync(settings); throw new Exception("Wrong password accepted"); }
catch (MySqlException error)
{
    Check(error.Number == 1045, "wrong password rejected by server");
    Check(ConnectionErrorMessages.For(error).Contains("账号验证失败"), "authentication error has useful Chinese message");
    Check(!ConnectionErrorMessages.For(error).Contains(settings.Password), "error message does not expose password");
}
settings.Password = fixturePassword;
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { await service.TestConnectionAsync(settings, cancelled.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Check(true, "cancelled operation does not connect"); }
}

// 开一个不说 MySQL 协议的本机端口，验证网络等待可取消且不会永久卡住。
var silentServer = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
silentServer.Start();
try
{
    settings.Port = (uint)((System.Net.IPEndPoint)silentServer.LocalEndpoint).Port;
    using var timeout = new CancellationTokenSource(350);
    var elapsed = System.Diagnostics.Stopwatch.StartNew();
    try { await service.TestConnectionAsync(settings, timeout.Token); throw new Exception("Silent server accepted"); }
    catch (OperationCanceledException) { Check(elapsed.Elapsed.TotalSeconds < 12, "silent handshake cancellation is bounded by connection timeout"); }
}
finally { silentServer.Stop(); }

// 同一个端口现在没有服务，测试无法到达时的提示和重试恢复。
try { await service.TestConnectionAsync(settings); throw new Exception("Closed port accepted"); }
catch (MySqlException error) { Check(ConnectionErrorMessages.For(error).Contains("服务器"), "unreachable endpoint has helpful error"); }
settings.Port = port;
Check((await service.TestConnectionAsync(settings)).Length > 0, "retry after failures succeeds");
// 只在独立临时实例中创建随机命名的数据，结束时删除自己的测试库。
string testDatabase = "sulfuricsql_临时验证_" + Guid.NewGuid().ToString("N")[..8];
async Task Execute(string sql)
{
    using var command = new MySqlCommand(sql, observer);
    await command.ExecuteNonQueryAsync();
}
await Execute($"CREATE DATABASE `{testDatabase}` CHARACTER SET utf8mb4");
try
{
    await Execute($"CREATE TABLE `{testDatabase}`.`学生` (id INT PRIMARY KEY, 姓名 VARCHAR(50), 备注 TEXT NULL, 图片 BLOB)");
    using (var insert = new MySqlCommand($"INSERT INTO `{testDatabase}`.`学生` VALUES (@id,@name,NULL,@bytes)", observer))
    {
        insert.Parameters.Add("@id", MySqlDbType.Int32);
        insert.Parameters.Add("@name", MySqlDbType.VarChar);
        insert.Parameters.AddWithValue("@bytes", new byte[] { 1, 2, 255 });
        for (int i = 205; i >= 1; i--)
        {
            insert.Parameters["@id"].Value = i;
            insert.Parameters["@name"].Value = "同学" + i;
            await insert.ExecuteNonQueryAsync();
        }
    }
    await Execute($"CREATE TABLE `{testDatabase}`.`空表` (id INT)");
    await Execute($"CREATE TABLE `{testDatabase}`.`特殊``表` (id INT)");
    await Execute($"CREATE VIEW `{testDatabase}`.`学生视图` AS SELECT id, 姓名 FROM `{testDatabase}`.`学生`");
    var browser = new DatabaseBrowserService(observer);
    Check((await browser.GetDatabasesAsync(default)).Contains(testDatabase), "database discovery");
    var tables = await browser.GetTablesAsync(testDatabase, default);
    Check(tables.Contains("学生") && tables.Contains("学生视图"), "tables and views discovered");
    var first = await browser.ReadPageAsync(testDatabase, "学生", 0, 100, default);
    Check(first.Rows.Rows.Count == 100 && first.HasMore && first.Ordered, "first page boundary");
    Check((int)first.Rows.Rows[0][0] == 1 && (int)first.Rows.Rows[99][0] == 100, "stable primary key ordering");
    Check(first.Rows.Rows[0][2] == DBNull.Value && first.Rows.Rows[0][3] is byte[], "NULL and binary values preserved");
    var second = await browser.ReadPageAsync(testDatabase, "学生", 1, 100, default);
    Check(second.Rows.Rows.Count == 100 && second.HasMore && (int)second.Rows.Rows[0][0] == 101, "second page without duplicate boundary");
    var last = await browser.ReadPageAsync(testDatabase, "学生", 2, 100, default);
    Check(last.Rows.Rows.Count == 5 && !last.HasMore, "last page disables next");
    var empty = await browser.ReadPageAsync(testDatabase, "空表", 0, 100, default);
    Check(empty.Rows.Rows.Count == 0 && empty.Rows.Columns.Count == 1 && !empty.HasMore, "empty table retains column headers");
    var special = await browser.ReadPageAsync(testDatabase, "特殊`表", 0, 100, default);
    Check(special.Rows.Columns.Count == 1, "identifier escaping supports backtick");
    var view = await browser.ReadPageAsync(testDatabase, "学生视图", 0, 100, default);
    Check(view.Rows.Rows.Count == 100 && !view.Ordered, "view browsing without primary key");
    try { await browser.ReadPageAsync(testDatabase, "不存在", 0, 100, default); throw new Exception("Missing table accepted"); }
    catch (MySqlException error) { Check(error.Number == 1146, "missing table reports server error"); }
    Check((await browser.GetTablesAsync(testDatabase, default)).Count == 4, "connection usable after query failure");
    var columns = await browser.GetColumnsAsync(testDatabase, "学生", default);
    Check(columns.Rows.Count == 4 && columns.Rows[0][0].ToString() == "id", "table structure has friendly headers");
    columns.Dispose();
    var filtered = await browser.ReadPageAsync(testDatabase, "学生", 0, 100, default,
        new BrowseOptions { SearchColumn = "姓名", SearchText = "同学20", SortColumn = "id", Descending = true });
    Check(filtered.Rows.Rows.Count == 7 && (int)filtered.Rows.Rows[0][0] == 205, "server-side filtering and descending sort");
    filtered.Rows.Dispose();
    var hostile = await browser.ReadPageAsync(testDatabase, "学生", 0, 100, default,
        new BrowseOptions { SearchColumn = "姓名", SearchText = "%' OR 1=1 --" });
    Check(hostile.Rows.Rows.Count == 0, "filter input is parameterized and wildcard characters are literal");
    hostile.Rows.Dispose();
    await Execute($"INSERT INTO `{testDatabase}`.`学生` VALUES (206,'100%_完成',NULL,NULL)");
    var literal = await browser.ReadPageAsync(testDatabase, "学生", 0, 100, default,
        new BrowseOptions { SearchColumn = "姓名", SearchText = "%_" });
    Check(literal.Rows.Rows.Count == 1 && (int)literal.Rows.Rows[0][0] == 206, "percent and underscore search literally");
    literal.Rows.Dispose();
    var queryService = new SqlQueryService(observer);
    var queryResult = await queryService.ExecuteAsync(testDatabase, "SELECT id, 姓名 FROM 学生 ORDER BY id DESC LIMIT 3;", default);
    Check(queryResult.Rows.Rows.Count == 3 && (int)queryResult.Rows.Rows[0][0] == 206, "SQL editor runs query in selected database");
    queryResult.Rows.Dispose();
    var duplicates = await queryService.ExecuteAsync(testDatabase, "SELECT 1 AS x, 2 AS x, 'a;b' AS text", default);
    Check(duplicates.Rows.Columns.Count == 3 && duplicates.Rows.Columns[1].ColumnName == "x (2)", "duplicate result column names and quoted semicolons");
    duplicates.Rows.Dispose();
    var capped = await queryService.ExecuteAsync(testDatabase, "SELECT a.id FROM 学生 a CROSS JOIN 学生 b LIMIT 510", default);
    Check(capped.Rows.Rows.Count == 500 && capped.Truncated, "SQL result cap reported accurately");
    capped.Rows.Dispose();
    foreach (string invalid in new[] { "DELETE FROM 学生", "SELECT 1; DROP TABLE 学生", "/*! DELETE FROM 学生 */ SELECT 1", "SELECT 1 INTO OUTFILE 'x'", "WITH x AS (SELECT 1) DELETE FROM 学生" })
    {
        try { await queryService.ExecuteAsync(testDatabase, invalid, default); throw new Exception("Unsafe query accepted"); }
        catch (ArgumentException) { Check(true, "non-query or multi-statement rejected"); }
    }
    ReadQueryPolicy.Validate("-- comment\nSELECT 'a;b' AS x; # trailing comment");
    Check(true, "normal SQL comments supported");
    try { await queryService.ExecuteAsync(testDatabase, "SELECT 不存在 FROM 学生", default); throw new Exception("Invalid query accepted"); }
    catch (MySqlException error) { Check(error.Number == 1054, "SQL error propagated for friendly UI message"); }
    using (var cancelledQuery = new CancellationTokenSource(300))
    {
        try { await queryService.ExecuteAsync(testDatabase, "SELECT SLEEP(5)", cancelledQuery.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Check(true, "running SQL can be cancelled"); }
    }
    var recovery = await queryService.ExecuteAsync(testDatabase, "SELECT COUNT(*) FROM 学生", default);
    Check(Convert.ToInt32(recovery.Rows.Rows[0][0]) == 206, "query failure and cancellation preserve data and connection");
    recovery.Rows.Dispose();
    foreach (string sql in new[] { "SHOW TABLES", "DESCRIBE 学生", "WITH x AS (SELECT 1 AS n) SELECT n FROM x" })
    {
        var supported = await queryService.ExecuteAsync(testDatabase, sql, default);
        Check(supported.Rows.Rows.Count > 0, "metadata and WITH queries supported");
        supported.Rows.Dispose();
    }
    await ManagementChecks.RunAsync(observer);
    await CompatibilityChecks.RunAsync(observer, testDatabase);
    if (args.Contains("--ui-check")) await WorkspaceUiChecks.RunAsync(settings, testDatabase);
    foreach (var result in new[] { first, second, last, empty, special, view }) result.Rows.Dispose();
    Console.WriteLine($"All {passed} service checks passed.");
    if (args.Contains("--preview"))
    {
        // 独立 STA 线程运行真实 WinForms 窗口，供界面检查；不使用用户的数据库。
        var thread = new Thread(() =>
        {
            ApplicationConfiguration.Initialize();
            using var previewConnection = service.ConnectAsync(settings).GetAwaiter().GetResult();
            using var form = new SulfuricSQL.Forms.WorkspaceForm(previewConnection, "临时测试实例 · " + settings.Port);
            Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}
finally { await Execute($"DROP DATABASE `{testDatabase}`"); }

