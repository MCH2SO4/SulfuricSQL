using SulfuricSQL.Models;
using SulfuricSQL.Services;

namespace SulfuricSQL.Forms;

public sealed partial class ConnectionForm : Form
{
    private readonly MySqlConnectionService connectionService = new();
    private CancellationTokenSource? pendingOperation;

    public ConnectionForm()
    {
        InitializeComponent();
        AppTheme.Apply(this);
        UiMotion.Attach(this);
        testButton.Click += async (_, _) => await RunConnectionAsync(testOnly: true);
        connectButton.Click += async (_, _) => await RunConnectionAsync(testOnly: false);
        cancelButton.Click += (_, _) =>
        {
            pendingOperation?.Cancel();
            cancelButton.Enabled = false;
            SetStatus("正在取消…网络握手无响应时，最多等待约 8 秒。", false);
        };
        showPassword.CheckedChanged += (_, _) => passwordBox.UseSystemPasswordChar = !showPassword.Checked;
        foreach (TextBox box in new[] { hostBox, portBox, usernameBox, passwordBox })
            box.TextChanged += (_, _) => SetStatus("配置已修改，请测试或连接。", false);
        FormClosing += (_, _) => pendingOperation?.Cancel();
    }

    private ConnectionSettings ReadSettings()
    {
        if (!uint.TryParse(portBox.Text.Trim(), out uint port))
            throw new ArgumentException("端口必须是 1～65535 之间的整数。");
        var settings = new ConnectionSettings
        {
            Host = hostBox.Text,
            Port = port,
            Username = usernameBox.Text,
            Password = passwordBox.Text
        };
        settings.Validate();
        return settings;
    }

    private async Task RunConnectionAsync(bool testOnly)
    {
        if (pendingOperation != null) return;
        ConnectionSettings settings;
        try { settings = ReadSettings(); }
        catch (ArgumentException error) { SetStatus(error.Message, true); return; }

        using var operation = new CancellationTokenSource();
        pendingOperation = operation;
        SetBusy(true);
        SetStatus(testOnly ? "正在测试连接…" : "正在连接服务器…", false);
        try
        {
            if (testOnly)
            {
                string version = await connectionService.TestConnectionAsync(settings, operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                SetStatus($"测试成功 · MySQL {version}\n测试连接已关闭，点击“连接”进入工作区。", false);
            }
            else
            {
                // 工作区打开期间保留连接；退出工作区后自动释放。
                await using var connection = await connectionService.ConnectAsync(settings, operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                if (IsDisposed) return;
                passwordBox.Clear();
                showPassword.Checked = false;
                using var workspace = new WorkspaceForm(connection, $"{settings.Username.Trim()} @ {settings.Host.Trim()}:{settings.Port}");
                Hide();
                try { workspace.ShowDialog(this); }
                catch (Exception)
                {
                    SetStatus("工作区发生界面错误，已返回连接页。此提示不代表账号或密码错误，请重新连接。", true);
                    return;
                }
                finally { if (!IsDisposed) Show(); }
                SetStatus("已断开连接。密码已清空，再次连接时请重新填写。", false);
            }
        }
        catch (Exception error)
        {
            // 不展示原始连接字符串或异常堆栈，避免泄露密码。
            if (!IsDisposed) SetStatus(ConnectionErrorMessages.For(error), error is not OperationCanceledException);
        }
        finally
        {
            settings.Password = "";
            pendingOperation = null;
            if (!IsDisposed) SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        fieldsPanel.Enabled = !busy;
        testButton.Enabled = connectButton.Enabled = !busy;
        cancelButton.Visible = busy;
        cancelButton.Enabled = busy;
        progress.Visible = busy;
        UseWaitCursor = busy;
    }

    private void SetStatus(string message, bool isError)
    {
        statusLabel.Text = message;
        statusLabel.ForeColor = isError ? Color.FromArgb(174, 46, 46) : Color.FromArgb(39, 94, 100);
        statusLabel.BackColor = isError ? Color.FromArgb(255, 240, 239) : Color.FromArgb(232, 245, 243);
    }
}


