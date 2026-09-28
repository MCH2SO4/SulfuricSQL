using MySqlConnector;
using SulfuricSQL.Models;

namespace SulfuricSQL.Services;

// 只负责连接 MySQL。以后查数据库、查表的代码可以放在其他 Service 中。
public sealed class MySqlConnectionService
{
    public async Task<MySqlConnection> ConnectAsync(
        ConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();

        // 用构建器处理特殊字符，避免密码中的分号破坏连接字符串。
        var builder = new MySqlConnectionStringBuilder
        {
            Server = settings.Host.Trim(),
            Port = settings.Port,
            UserID = settings.Username.Trim(),
            Password = settings.Password,
            ConnectionTimeout = 8,
            DefaultCommandTimeout = 8,
            Pooling = false,
            UseAffectedRows = true,
            PersistSecurityInfo = false,
            SslMode = MySqlSslMode.Preferred
        };

        var connection = new MySqlConnection(builder.ConnectionString);
        try
        {
            // await 等待网络结果时，窗口仍可响应操作。
            await connection.OpenAsync(cancellationToken);
            return connection; // 谁接收连接，谁负责在不用时释放。
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<string> TestConnectionAsync(
        ConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        // 测试连接用完立即释放，不会留下一个后台连接。
        await using var connection = await ConnectAsync(settings, cancellationToken);
        return connection.ServerVersion;
    }
}
