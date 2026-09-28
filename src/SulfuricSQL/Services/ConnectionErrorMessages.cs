using MySqlConnector;

namespace SulfuricSQL.Services;

public static class ConnectionErrorMessages
{
    public static string For(Exception error) => error switch
    {
        ArgumentException => error.Message,
        OperationCanceledException => "操作已取消，可以修改配置后重试。",
        MySqlException { Number: 1045 or 1698 } =>
            "账号验证失败：请检查用户名、密码，以及该账号是否允许从当前电脑连接。",
        MySqlException { Number: 1042 or 2002 or 2003 } =>
            "无法到达服务器：请检查地址、端口、MySQL 服务状态和防火墙。",
        MySqlException { Number: 1040 } => "服务器连接数已满，请稍后重试。",
        MySqlException ex => $"MySQL 连接失败（错误码 {ex.Number}）。请检查网络、账号权限和 TLS 配置。",
        _ => "连接失败，请检查服务器状态和连接配置后重试。"
    };
}
