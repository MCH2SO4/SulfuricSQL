namespace SulfuricSQL.Models;

// 一份连接配置：只放数据，不负责画界面或连接服务器。
public sealed class ConnectionSettings
{
    public string Host { get; set; } = "";
    public uint Port { get; set; } = 3306;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
            throw new ArgumentException("请填写服务器地址（Host / IP）。");
        if (Port is < 1 or > 65535)
            throw new ArgumentException("端口必须是 1～65535 之间的整数。");
        if (string.IsNullOrWhiteSpace(Username))
            throw new ArgumentException("请填写 MySQL 用户名。");
        // 允许空密码，但不自动猜测、保存或修改密码。
    }
}
