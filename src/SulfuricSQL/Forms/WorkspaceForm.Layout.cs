namespace SulfuricSQL.Forms;

// 布局与事件分开：这里负责“长什么样”，其他 partial 文件负责“点了做什么”。
public sealed partial class WorkspaceForm
{
    private static readonly Color Accent = Color.FromArgb(20, 112, 117);
    private static readonly Color Ink = Color.FromArgb(29, 47, 64);
    private readonly Label serverHeader = new() { Dock = DockStyle.Fill, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button simpleMode = MakeButton("简单模式", "simpleMode");
    private readonly Button proMode = MakeButton("专业模式", "proMode");
    private readonly Button openCode = MakeButton("编写 SQL", "openCode");
    private readonly Button disconnect = MakeButton("断开连接", "disconnect");
    private readonly Button compatibilityButton = MakeButton("版本兼容", "compatibilityButton");
    private readonly Button codeManageButton = MakeButton("库表管理 ▾", "codeManageButton");
    private readonly Button manageButton = MakeButton("库表管理 ▾", "manageButton");
    private readonly Label guide = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Accent };
    private readonly ComboBox databasePicker = MakePicker("databasePicker");
    private readonly ComboBox tablePicker = MakePicker("tablePicker");
    private readonly CheckBox showSystem = new() { Name = "showSystem", Text = "显示系统库", AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button openTable = MakeButton("③  查看数据", "openTable");
    private readonly Button refreshSources = MakeButton("刷新列表", "refreshSources");
    private readonly SplitContainer split = new() { Size = new Size(1140, 550), Dock = DockStyle.Fill, SplitterDistance = 230, Panel1MinSize = 170, Panel2MinSize = 600 };
    private readonly TreeView tree = new() { Name = "tree", Dock = DockStyle.Fill, HideSelection = false, BorderStyle = BorderStyle.None, AccessibleName = "数据库和表" };
    private readonly Button refreshTree = MakeButton("刷新数据库树", "refreshTree");
    private readonly TabControl tabs = new() { Name = "tabs", Dock = DockStyle.Fill };
    private readonly TabPage dataTab = new("查看数据");
    private readonly TabPage structureTab = new("表结构");
    private readonly TabPage sqlTab = new("SQL 查询");
    private readonly Label tableTitle = new() { Name = "tableTitle", Text = "你的数据会显示在这里", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label resultHint = new() { Text = "完成上方的三个步骤，即可开始查看。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ForeColor = Color.DimGray };
    private readonly DataGridView grid = MakeGrid("grid");
    private readonly DataGridView structureGrid = MakeGrid("structureGrid");
    private readonly DataGridView sqlGrid = MakeGrid("sqlGrid");
    private readonly TableLayoutPanel filterPanel = new() { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 6 };
    private readonly ComboBox searchColumn = MakePicker("searchColumn");
    private readonly ComboBox sortColumn = MakePicker("sortColumn");
    private readonly ComboBox sortDirection = MakePicker("sortDirection");
    private readonly TextBox searchText = new() { Name = "searchText", PlaceholderText = "输入想找的内容", Dock = DockStyle.Fill, AccessibleName = "查找内容" };
    private readonly Button applyFilter = MakeButton("应用", "applyFilter");
    private readonly Button resetFilter = MakeButton("清空条件", "resetFilter");
    private readonly Button previous = MakeButton("上一批", "previous");
    private readonly Button next = MakeButton("下一批", "next");
    private readonly Button refreshData = MakeButton("刷新数据", "refreshData");
    private readonly Button queryExample = MakeButton("生成查询示例", "queryExample");
    private readonly Label pageLabel = new() { Name = "pageLabel", Text = "尚未打开表", AutoSize = true, Padding = new Padding(8, 10, 0, 0) };
    private readonly SqlCodeEditor sqlEditor = new() { Name = "sqlEditor", Dock = DockStyle.Fill, Font = new Font("Consolas", 12F), AcceptsTab = true, WordWrap = false, DetectUrls = false, AccessibleName = "SQL 编辑器", BorderStyle = BorderStyle.None };
    private readonly Button executeSql = MakeButton("执行 F5", "executeSql");
    private readonly Button stopSql = MakeButton("停止", "stopSql");
    private readonly Button clearSql = MakeButton("清空", "clearSql");
    private readonly Button templateButton = MakeButton("SQL 模板 ▾", "templateButton");
    private readonly ComboBox historyPicker = MakePicker("historyPicker");
    private readonly ComboBox resultPicker = MakePicker("resultPicker");
    private readonly TextBox sqlOutput = new() { Name = "sqlOutput", Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = Color.White };
    private readonly TabControl outputTabs = new() { Name = "outputTabs", Dock = DockStyle.Fill };
    private readonly Label sqlContext = new() { Text = "请先选择一个数据库，再运行查询。", Dock = DockStyle.Fill, ForeColor = Accent, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Label sqlResult = new() { Name = "sqlResult", Text = "运行结果会显示在下方。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label status = new() { Name = "status", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Button cancelOperation = MakeButton("停止", "cancelOperation");
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Visible = false };

    private static Button MakeButton(string text, string name) => new MotionButton()
    {
        Name = name, Text = text, AutoSize = true, MinimumSize = new Size(78, 36),
        FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Ink,
        Padding = new Padding(8, 2, 8, 2), Cursor = Cursors.Hand
    };

    private static ComboBox MakePicker(string name) => new()
    {
        Name = name, Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 280,
        AutoCompleteSource = AutoCompleteSource.ListItems, AutoCompleteMode = AutoCompleteMode.SuggestAppend,
        AccessibleName = name
    };

    private static DataGridView MakeGrid(string name)
    {
        var target = new DataGridView
        {
            Name = name, Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AllowUserToOrderColumns = true, AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, BackgroundColor = Color.White,
            RowHeadersVisible = false, BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false,
            AccessibleName = name, SelectionMode = DataGridViewSelectionMode.CellSelect
        };
        target.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(231, 240, 242);
        target.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        target.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 250, 251);
        target.DefaultCellStyle.NullValue = "NULL";
        target.CellFormatting += (_, e) =>
        {
            if (e.Value is byte[] bytes) { e.Value = $"[二进制 · {bytes.Length} 字节]"; e.FormattingApplied = true; }
        };
        target.DataError += (_, e) => e.ThrowException = false;
        return target;
    }

    private void InitializeLayout(string serverDisplay)
    {
        SuspendLayout();
        Text = "SulfuricSQL · 数据库工作区";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 10F);
        ClientSize = new Size(1180, 820);
        MinimumSize = new Size(1040, 740);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(243, 247, 249);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(20) };
        foreach (int height in new[] { 78, 46, 102 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 5));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Ink, Padding = new Padding(16, 8, 12, 8) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        serverHeader.Text = $"SulfuricSQL\n{serverDisplay} · MySQL {connection.ServerVersion}";
        var modes = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false };
        modes.Controls.AddRange([simpleMode, proMode, openCode, manageButton, disconnect]);
        header.Controls.Add(serverHeader, 0, 0);
        header.Controls.Add(modes, 1, 0);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(guide, 0, 1);

        var selectors = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selectors.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        selectors.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        selectors.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        selectors.Controls.Add(new Label { Text = "①  选择数据库", AutoSize = true }, 0, 0);
        selectors.Controls.Add(new Label { Text = "②  选择一张表", AutoSize = true }, 1, 0);
        selectors.Controls.Add(databasePicker, 0, 1);
        selectors.Controls.Add(tablePicker, 1, 1);
        selectors.Controls.Add(openTable, 2, 1);
        selectors.Controls.Add(refreshSources, 3, 1);
        selectors.Controls.Add(showSystem, 0, 2);
        openTable.BackColor = Accent;
        openTable.ForeColor = Color.White;
        root.Controls.Add(selectors, 0, 2);

        var treeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(0, 0, 10, 0) };
        treeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        treeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        treeLayout.Controls.Add(refreshTree, 0, 0);
        treeLayout.Controls.Add(tree, 0, 1);
        split.Panel1.Controls.Add(treeLayout);
        BuildDataTab();
        BuildSqlTab();
        structureTab.Controls.Add(structureGrid);
        structureTab.Padding = new Padding(10);
        tabs.TabPages.Add(dataTab);
        split.Panel2.Controls.Add(tabs);
        root.Controls.Add(split, 0, 3);
        root.Controls.Add(progress, 0, 4);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        footer.Controls.Add(status, 0, 0);
        footer.Controls.Add(cancelOperation, 1, 0);
        root.Controls.Add(footer, 0, 5);
        Controls.Add(root);
        ResumeLayout(true);
    }

    private void BuildDataTab()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.Controls.Add(tableTitle, 0, 0);
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        filterPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        filterPanel.Controls.Add(new Label { Text = "查找", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 0);
        filterPanel.Controls.Add(searchColumn, 1, 0);
        filterPanel.Controls.Add(searchText, 2, 0);
        filterPanel.SetColumnSpan(searchText, 2);
        filterPanel.Controls.Add(applyFilter, 4, 0);
        filterPanel.Controls.Add(resetFilter, 5, 0);
        filterPanel.Controls.Add(new Label { Text = "排序", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 1);
        filterPanel.Controls.Add(sortColumn, 1, 1);
        sortDirection.Items.AddRange(["从小到大", "从大到小"]);
        sortDirection.SelectedIndex = 0;
        filterPanel.Controls.Add(sortDirection, 2, 1);
        var tip = new Label { Text = "选好条件后点“应用”", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(0, 6, 0, 0) };
        filterPanel.Controls.Add(tip, 3, 1);
        filterPanel.SetColumnSpan(tip, 3);
        layout.Controls.Add(filterPanel, 0, 1);
        layout.Controls.Add(resultHint, 0, 2);
        layout.Controls.Add(grid, 0, 3);
        var paging = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        paging.Controls.AddRange([previous, next, refreshData, queryExample, pageLabel]);
        layout.Controls.Add(paging, 0, 4);
        layout.Controls.Add(new Label { Text = "这里用于查看数据，单元格不会被误修改。", AutoSize = true, ForeColor = Color.DimGray }, 0, 5);
        dataTab.Controls.Add(layout);
    }

    private void BuildSqlTab()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        var contextRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        contextRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        contextRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        contextRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        compatibilityButton.Text = "MySQL " + connection.ServerVersion + " · 兼容性";
        compatibilityButton.AutoSize = false; compatibilityButton.MinimumSize = Size.Empty;
        compatibilityButton.Width = 230; compatibilityButton.Dock = DockStyle.Fill;
        compatibilityButton.Font = new Font("Microsoft YaHei UI", 9F);
        contextRow.Controls.Add(sqlContext, 0, 0); contextRow.Controls.Add(compatibilityButton, 1, 0);
        layout.Controls.Add(contextRow, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        actions.AutoScroll = true;
        actions.Controls.AddRange([executeSql, stopSql, clearSql, templateButton, codeManageButton]);
        var focusEditor = MakeButton("专注编辑", "focusEditor");
        actions.Controls.Add(focusEditor);
        focusEditor.Click += (_, _) =>
        {
            bool expand = outputTabs.Visible;
            outputTabs.Visible = sqlResult.Visible = !expand;
            layout.RowStyles[3].Height = expand ? 100 : 55;
            layout.RowStyles[4].Height = expand ? 0 : 30;
            layout.RowStyles[5].Height = expand ? 0 : 45;
            focusEditor.Text = expand ? "显示结果" : "专注编辑";
            PageTransition.Play(sqlTab);
        };
        layout.Controls.Add(actions, 0, 1);
        var historyRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        historyRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); historyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        historyRow.Controls.Add(new Label { Text = "执行历史", AutoSize = true }, 0, 0);
        historyRow.Controls.Add(historyPicker, 1, 0);
        historyPicker.DropDownWidth = 700;
        layout.Controls.Add(historyRow, 0, 2);
        var editorFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = Color.FromArgb(22, 32, 46) };
        editorFrame.Controls.Add(sqlEditor);
        editorFrame.Controls.Add(new Label { Text = "SQL EDITOR     /     Ctrl+Space 补全 · F5 执行", Dock = DockStyle.Top, Height = 28, ForeColor = Color.FromArgb(128, 157, 178), Font = new Font("Segoe UI", 9F) });
        layout.Controls.Add(editorFrame, 0, 3);
        layout.Controls.Add(sqlResult, 0, 4);
        var rowsPage = new TabPage("查询结果"); var logPage = new TabPage("执行输出");
        var results = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        results.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); results.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        results.Controls.Add(resultPicker, 0, 0); results.Controls.Add(sqlGrid, 0, 1);
        rowsPage.Controls.Add(results); logPage.Controls.Add(sqlOutput);
        outputTabs.TabPages.AddRange([rowsPage, logPage]);
        layout.Controls.Add(outputTabs, 0, 5);
        sqlTab.Controls.Add(layout);
    }
}








