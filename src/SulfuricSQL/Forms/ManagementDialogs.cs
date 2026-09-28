using System.Data;
using SulfuricSQL.Services;

namespace SulfuricSQL.Forms;

internal static class ManagementDialogs
{
    public static bool ShowCompatibility(IWin32Window owner, string serverVersion, IReadOnlyList<CompatibilityIssue> issues)
    {
        bool blocked = issues.Any(i => i.Blocking);
        using var form = Create("SQL 版本兼容", new Size(740, 490));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.Controls.Add(new Label { Text = "MySQL " + serverVersion + "\n" + (blocked ? "发现当前版本不支持的语法" : issues.Count > 0 ? "发现旧写法或行为差异" : "按连接的服务器版本自动检查"), Dock = DockStyle.Fill, ForeColor = AppTheme.Teal, Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold) }, 0, 0);
        string details = issues.Count == 0
            ? ServerCompatibility.Parse(serverVersion).Summary + "\r\n\r\n执行前检查常见版本差异：WITH、窗口函数、CHECK、排序规则和旧查询缓存写法。\r\n\r\n基础 SQL 在新旧版本间通常通用，不会仅因为写法较旧就弹窗。字符集和排序规则从服务器实际列表读取。\r\n\r\n这是有限的规则检查，不是完整 SQL 解析器，也不保证所有版本或发行分支均兼容。"
            : string.Join("\r\n\r\n", issues.Select(i => (i.Blocking ? "[不支持] " : "[注意] ") + i.Feature + "\r\n" + i.Message));
        layout.Controls.Add(new TextBox { Name = "compatibilityDetails", TabStop = false, Text = details, Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.White, ScrollBars = ScrollBars.Vertical }, 0, 1);
        layout.Controls.Add(new Label { Text = "不会自动改写 SQL。兼容检查不替代原有危险操作确认。", Dock = DockStyle.Fill, ForeColor = Color.DimGray, Padding = new Padding(0, 10, 0, 0) }, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = SaveButton(issues.Count == 0 ? "知道了" : "返回修改"); cancel.Name = "cancelCompatibility"; cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(cancel);
        if (!blocked && issues.Count > 0)
        {
            var proceed = SaveButton("仍然执行"); proceed.Name = "continueCompatibility"; proceed.DialogResult = DialogResult.OK;
            buttons.Controls.Add(proceed);
        }
        layout.Controls.Add(buttons, 0, 3); form.Controls.Add(layout); form.AcceptButton = cancel; form.CancelButton = cancel;
        form.Shown += (_, _) => cancel.Focus();
        return form.ShowDialog(owner) == DialogResult.OK;
    }
    public static bool Confirm(IWin32Window owner, string description)
    {
        using var form = Create("危险 SQL · 执行前确认", new Size(760, 500));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.Controls.Add(new Label { Text = "请核对下面的 SQL 和目标对象。继续执行可能永久删除或修改数据。", Dock = DockStyle.Fill, ForeColor = Color.Firebrick }, 0, 0);
        layout.Controls.Add(new TextBox { Name = "dangerDetails", Text = description, Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false }, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new MotionButton { Name = "cancelDanger", Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(14, 6, 14, 6) };
        var accept = SaveButton("确认执行"); accept.Name = "confirmDanger"; accept.DialogResult = DialogResult.OK;
        buttons.Controls.AddRange([cancel, accept]); layout.Controls.Add(buttons, 0, 2); form.Controls.Add(layout);
        form.AcceptButton = cancel; form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK;
    }
    private static Form Create(string title, Size size)
    {
        var form = new Form()
    {
        Text = title, ClientSize = size, MinimumSize = size, StartPosition = FormStartPosition.CenterParent,
        Font = new Font("Microsoft YaHei UI", 10), BackColor = Color.FromArgb(243, 247, 249),
        MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false
    };
        form.Shown += (_, _) => AppTheme.Apply(form);
        UiMotion.Attach(form);
        return form;
    }

    private static Button SaveButton(string text) => new MotionButton()
    {
        Text = text, AutoSize = true, Padding = new Padding(12, 5, 12, 5), BackColor = Color.FromArgb(20, 112, 117),
        ForeColor = Color.White, FlatStyle = FlatStyle.Flat
    };

    public static string? EditDatabase(IWin32Window owner, DataTable collations, string? database = null, string? charset = null, string? collation = null)
    {
        using var form = Create(database == null ? "创建数据库" : "修改数据库默认字符集 / 排序规则", new Size(640, 380));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        var name = new TextBox { Name = "databaseName", Dock = DockStyle.Fill, Text = database ?? "", ReadOnly = database != null, PlaceholderText = "例如 school" };
        var charsets = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        var rules = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 400 };
        charsets.Items.AddRange(collations.AsEnumerable().Select(r => r.Field<string>(0)!).Distinct().Cast<object>().ToArray());
        charsets.SelectedIndexChanged += (_, _) =>
        {
            rules.Items.Clear();
            var rows = collations.AsEnumerable().Where(r => r.Field<string>(0) == charsets.Text).ToList();
            rules.Items.AddRange(rows.Select(r => (object)r.Field<string>(1)!).ToArray());
            rules.SelectedItem = rows.FirstOrDefault(r => r.Field<string>(2) == "Yes")?.Field<string>(1);
            if (rules.SelectedIndex < 0 && rules.Items.Count > 0) rules.SelectedIndex = 0;
        };
        charsets.SelectedItem = charset ?? "utf8mb4";
        if (charsets.SelectedIndex < 0 && charsets.Items.Count > 0) charsets.SelectedIndex = 0;
        if (collation != null) rules.SelectedItem = collation;
        string[] labels = ["数据库名称", "字符集", "排序规则"];
        Control[] controls = [name, charsets, rules];
        for (int i = 0; i < controls.Length; i++) { layout.Controls.Add(new Label { Text = labels[i], AutoSize = true }, 0, i); layout.Controls.Add(controls[i], 1, i); }
        var note = new Label { Text = "修改的是数据库默认值，不会自动转换已有表和列的字符集。", Dock = DockStyle.Fill, ForeColor = Color.DimGray };
        layout.Controls.Add(note, 0, 3); layout.SetColumnSpan(note, 2);
        string? sql = null;
        var save = SaveButton(database == null ? "创建数据库" : "应用修改");
        save.Name = "saveDatabase";
        save.Click += (_, _) =>
        {
            try
            {
                if (rules.SelectedIndex < 0) throw new ArgumentException("请选择字符集和排序规则。");
                sql = database == null ? DatabaseService.CreateDatabase(name.Text.Trim(), charsets.Text, rules.Text) : DatabaseService.AlterDatabase(database, charsets.Text, rules.Text);
                form.DialogResult = DialogResult.OK;
            }
            catch (Exception error) { MessageBox.Show(form, error.Message, "请检查输入", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };
        layout.Controls.Add(save, 1, 4); form.Controls.Add(layout); form.AcceptButton = save;
        return form.ShowDialog(owner) == DialogResult.OK ? sql : null;
    }

    public static string? CreateTable(IWin32Window owner, string database)
    {
        using var form = Create("新建表 · " + database, new Size(960, 570));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var name = new TextBox { Name = "tableName", PlaceholderText = "输入新表名称，例如 student", Dock = DockStyle.Fill };
        var grid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true };
        grid.Columns.Add("name", "字段名");
        var types = new DataGridViewComboBoxColumn { Name = "type", HeaderText = "数据类型" };
        types.Items.AddRange("INT", "BIGINT", "VARCHAR", "TEXT", "DECIMAL", "DOUBLE", "DATE", "DATETIME", "BOOLEAN", "BLOB");
        grid.Columns.Add(types); grid.Columns.Add("size", "长度 / 精度");
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "primary", HeaderText = "主键" });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "nullable", HeaderText = "允许 NULL" });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "auto", HeaderText = "自增" });
        grid.Rows.Add("id", "INT", "", true, false, true);
        grid.Rows.Add("name", "VARCHAR", "100", false, true, false);
        grid.DataError += (_, e) => e.ThrowException = false;
        grid.DefaultValuesNeeded += (_, e) => { e.Row.Cells["type"].Value = "VARCHAR"; e.Row.Cells["size"].Value = "100"; e.Row.Cells["nullable"].Value = true; };
        var note = new Label { Text = "在最后一行添加字段；选中行头按 Delete 删除字段。VARCHAR 填长度，DECIMAL 填如 10,2。主键不能为 NULL，自增使用整数主键。", Dock = DockStyle.Fill, ForeColor = Color.DimGray };
        var save = SaveButton("创建表"); save.Name = "createTable"; string? sql = null;
        save.Click += (_, _) =>
        {
            try
            {
                grid.EndEdit();
                var columns = grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r => new ColumnDefinition(
                    Convert.ToString(r.Cells[0].Value)?.Trim() ?? "", Convert.ToString(r.Cells[1].Value) ?? "", Convert.ToString(r.Cells[2].Value) ?? "",
                    Convert.ToBoolean(r.Cells[3].Value), Convert.ToBoolean(r.Cells[4].Value), Convert.ToBoolean(r.Cells[5].Value))).ToList();
                sql = DatabaseService.CreateTable(database, name.Text.Trim(), columns);
                form.DialogResult = DialogResult.OK;
            }
            catch (Exception error) { MessageBox.Show(form, error.Message, "请检查字段定义", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };
        layout.Controls.Add(name, 0, 0); layout.Controls.Add(grid, 0, 1); layout.Controls.Add(note, 0, 2); layout.Controls.Add(save, 0, 3);
        form.Controls.Add(layout);
        return form.ShowDialog(owner) == DialogResult.OK ? sql : null;
    }

    public static string? Rename(IWin32Window owner, string oldName)
    {
        using var form = Create("重命名表", new Size(500, 190));
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), FlowDirection = FlowDirection.TopDown };
        var text = new TextBox { Text = oldName, Width = 440 };
        var save = SaveButton("应用新名称");
        save.Click += (_, _) => { try { DatabaseService.Quote(text.Text.Trim()); form.DialogResult = DialogResult.OK; } catch (Exception error) { MessageBox.Show(form, error.Message); } };
        layout.Controls.AddRange([text, save]); form.Controls.Add(layout); form.AcceptButton = save;
        return form.ShowDialog(owner) == DialogResult.OK ? text.Text.Trim() : null;
    }

    public static void ShowProperties(IWin32Window owner, DataTable properties)
    {
        using var form = Create("数据库属性", new Size(690, 320));
        var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, BackColor = Color.White };
        text.Text = properties.Rows.Count == 0 ? "数据库不存在，或当前账号无法查看。" : string.Join(Environment.NewLine + Environment.NewLine, properties.Columns.Cast<DataColumn>().Select(c => c.ColumnName + "：" + properties.Rows[0][c]));
        form.Padding = new Padding(20); form.Controls.Add(text); form.ShowDialog(owner);
    }
}




