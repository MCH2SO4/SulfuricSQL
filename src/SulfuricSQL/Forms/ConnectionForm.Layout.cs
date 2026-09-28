namespace SulfuricSQL.Forms;

// 界面布局单独放在这里。初学时先看 ConnectionForm.cs 的按钮事件即可。
public sealed partial class ConnectionForm
{
    private readonly TextBox hostBox = new() { Text = "localhost", AccessibleName = "Host / IP" };
    private readonly TextBox portBox = new() { Text = "3306", AccessibleName = "Port" };
    private readonly TextBox usernameBox = new() { PlaceholderText = "填写你的 MySQL 用户名", AccessibleName = "Username" };
    private readonly TextBox passwordBox = new() { UseSystemPasswordChar = true, AccessibleName = "Password" };
    private readonly CheckBox showPassword = new() { Text = "显示密码", AutoSize = true };
    private readonly Button testButton = new MotionButton() { Text = "测试连接", AutoSize = true, Padding = new Padding(14, 7, 14, 7) };
    private readonly Button connectButton = new MotionButton() { Text = "连接  →", AutoSize = true, Padding = new Padding(22, 7, 22, 7) };
    private readonly Button cancelButton = new MotionButton() { Text = "取消", AutoSize = true, Visible = false, Padding = new Padding(10, 7, 10, 7) };
    private readonly Label statusLabel = new() { Dock = DockStyle.Fill, Padding = new Padding(16), AutoSize = false, AccessibleName = "连接状态" };
    private readonly TableLayoutPanel fieldsPanel = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Visible = false };

    private void InitializeComponent()
    {
        SuspendLayout();
        Text = "SulfuricSQL · 连接 MySQL 服务器";
        Font = new Font("Microsoft YaHei UI", 10F);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(920, 630);
        MinimumSize = new Size(880, 650);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 247, 250);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 248));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(24, 36, 20, 24),
            BackColor = Color.FromArgb(23, 43, 61), ForeColor = Color.White
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        sidebar.Controls.Add(new Label { Text = "SulfuricSQL", Font = new Font(Font.FontFamily, 21F, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
        sidebar.Controls.Add(new Label { Text = "从连接开始，\n认识你的数据库。", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(171, 194, 208) }, 0, 1);
        sidebar.Controls.Add(new Label { Text = "01   服务器连接", Dock = DockStyle.Fill, Padding = new Padding(10, 16, 0, 0), BackColor = Color.FromArgb(38, 69, 88) }, 0, 2);
        sidebar.Controls.Add(new Label { Text = "连接后可以\n简单模式：选表、查数据\n专业模式：结构、SQL", Dock = DockStyle.Fill, Padding = new Padding(0, 20, 0, 0), ForeColor = Color.FromArgb(171, 194, 208) }, 0, 3);
        sidebar.Controls.Add(new Label { Text = "学习版  ·  双模式工作区\nC# + MySQL", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(171, 194, 208) }, 0, 5);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(32, 32, 32, 24) };
        foreach (int height in new[] { 48, 58, 255, 64, 6 }) content.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        content.Controls.Add(new Label { Text = "连接 MySQL 服务器", Font = new Font(Font.FontFamily, 20F, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
        content.Controls.Add(new Label { Text = "填写服务器信息，先测试，再进入工作区。\n本机地址和端口可修改；用户名和密码由你填写。", Dock = DockStyle.Fill, ForeColor = Color.DimGray }, 0, 1);
        fieldsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
        fieldsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var labels = new[] { "服务器地址", "端口号", "MySQL 用户名", "密码" };
        var boxes = new[] { hostBox, portBox, usernameBox, passwordBox };
        for (int i = 0; i < boxes.Length; i++)
        {
            fieldsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
            fieldsPanel.Controls.Add(new Label { Text = labels[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, i);
            boxes[i].Anchor = AnchorStyles.Left | AnchorStyles.Right;
            boxes[i].TabIndex = i;
            fieldsPanel.Controls.Add(boxes[i], 1, i);
        }
        fieldsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        showPassword.TabIndex = 4;
        fieldsPanel.Controls.Add(showPassword, 1, 4);
        content.Controls.Add(fieldsPanel, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        connectButton.BackColor = Color.FromArgb(25, 113, 116);
        connectButton.ForeColor = Color.White;
        connectButton.FlatStyle = FlatStyle.Flat;
        connectButton.FlatAppearance.BorderSize = 0;
        actions.Controls.AddRange([testButton, connectButton, cancelButton]);
        content.Controls.Add(actions, 0, 3);
        content.Controls.Add(progress, 0, 4);
        content.Controls.Add(statusLabel, 0, 5);
        content.Controls.Add(new Label { Text = "密码仅用于本次连接，不保存到文件。", Dock = DockStyle.Fill, ForeColor = Color.DimGray, TextAlign = ContentAlignment.BottomLeft }, 0, 6);
        root.Controls.Add(sidebar, 0, 0);
        root.Controls.Add(content, 1, 0);
        Controls.Add(root);
        AcceptButton = connectButton;
        SetStatus("尚未连接 · 请填写 MySQL 用户名和密码。", false);
        ResumeLayout(true);
    }
}

