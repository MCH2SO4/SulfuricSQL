using System.Data;
using SulfuricSQL.Models;

namespace SulfuricSQL.Forms;

public sealed partial class WorkspaceForm
{
    private static bool IsSystemDatabase(string name) => new[] { "mysql", "sys", "information_schema", "performance_schema" }.Contains(name, StringComparer.OrdinalIgnoreCase);

    private void WireBrowsingEvents()
    {
        refreshTree.Click += async (_, _) => await RunAsync(LoadDatabasesAsync, "正在刷新数据库…");
        refreshSources.Click += async (_, _) => await RunAsync(LoadDatabasesAsync, "正在刷新数据库…");
        showSystem.CheckedChanged += async (_, _) =>
        {
            if (!syncing) await RunAsync(FillDatabasePickerAsync, "正在更新列表…");
        };
        databasePicker.SelectedIndexChanged += async (_, _) =>
        {
            if (!syncing) await RunAsync(LoadSelectedTablesAsync, "正在加载表…");
        };
        openTable.Click += async (_, _) =>
        {
            if (databasePicker.SelectedItem is string db && tablePicker.SelectedItem is string table)
                await RunAsync(token => OpenTableAsync(db, table, token), "正在打开这张表…");
        };
        tablePicker.SelectedIndexChanged += (_, _) => UpdateButtons();
        tree.BeforeExpand += async (_, e) =>
        {
            if (e.Node == null || e.Node.Level != 0 || e.Node.Nodes.Count != 1 || e.Node.Nodes[0].Tag is not true) return;
            e.Cancel = true;
            var node = e.Node;
            await RunAsync(async token =>
            {
                var tables = await browser.GetTablesAsync(node.Text, token);
                node.Nodes.Clear();
                foreach (string table in tables) node.Nodes.Add(new TreeNode(table));
                node.Expand();
                SetStatus($"{node.Text} · {tables.Count} 个表或视图，双击即可打开。");
            }, "正在加载表…");
        };
        tree.NodeMouseDoubleClick += async (_, e) =>
        {
            if (e.Node == null || e.Node.Level != 1 || e.Node.Tag is true) return;
            string db = e.Node.Parent!.Text, table = e.Node.Text;
            await RunAsync(async token =>
            {
                syncing = true;
                try
                {
                    if (!databasePicker.Items.Contains(db)) { showSystem.Checked = true; PopulateDatabaseItems(); }
                    databasePicker.SelectedItem = db;
                }
                finally { syncing = false; }
                await LoadSelectedTablesAsync(token);
                tablePicker.SelectedItem = table;
                await OpenTableAsync(db, table, token);
            }, "正在打开这张表…");
        };
        previous.Click += async (_, _) => await RunAsync(token => LoadPageAsync(page - 1, appliedOptions, token), "正在加载上一页…");
        next.Click += async (_, _) => await RunAsync(token => LoadPageAsync(page + 1, appliedOptions, token), "正在加载下一页…");
        refreshData.Click += async (_, _) => await RunAsync(token => LoadPageAsync(0, appliedOptions, token), "正在刷新数据…");
        applyFilter.Click += async (_, _) => await RunAsync(async token =>
        {
            if (searchText.Text.Length > 0 && searchColumn.SelectedIndex <= 0)
                throw new ArgumentException("先选择要在哪一列查找，再点击“应用”。");
            var options = new BrowseOptions
            {
                SearchColumn = searchColumn.SelectedIndex > 0 ? searchColumn.Text : null,
                SearchText = searchText.Text,
                SortColumn = sortColumn.SelectedIndex > 0 ? sortColumn.Text : null,
                Descending = sortDirection.SelectedIndex == 1
            };
            await LoadPageAsync(0, options, token);
        }, "正在查找符合条件的数据…");
        resetFilter.Click += async (_, _) => await RunAsync(async token =>
        {
            await LoadPageAsync(0, new BrowseOptions(), token);
            searchText.Clear();
            searchColumn.SelectedIndex = sortColumn.SelectedIndex = sortDirection.SelectedIndex = 0;
        }, "正在显示全部数据…");
        searchText.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; applyFilter.PerformClick(); } };
    }

    private async Task LoadDatabasesAsync(CancellationToken token)
    {
        databases = await browser.GetDatabasesAsync(token);
        tree.Nodes.Clear();
        foreach (string name in databases)
        {
            var node = new TreeNode(name);
            node.Nodes.Add(new TreeNode("展开以加载表…") { Tag = true });
            tree.Nodes.Add(node);
        }
        await FillDatabasePickerAsync(token);
    }

    private void PopulateDatabaseItems()
    {
        string? selected = databasePicker.SelectedItem as string;
        databasePicker.Items.Clear();
        foreach (string db in databases.Where(db => showSystem.Checked || !IsSystemDatabase(db))) databasePicker.Items.Add(db);
        databasePicker.SelectedItem = selected;
    }

    private async Task FillDatabasePickerAsync(CancellationToken token)
    {
        syncing = true;
        try
        {
            PopulateDatabaseItems();
            if (databasePicker.SelectedIndex < 0 && databasePicker.Items.Count > 0) databasePicker.SelectedIndex = 0;
        }
        finally { syncing = false; }
        await LoadSelectedTablesAsync(token);
    }

    private async Task LoadSelectedTablesAsync(CancellationToken token)
    {
        tablePicker.Items.Clear();
        ClearPage();
        if (databasePicker.SelectedItem is not string db)
        {
            SetStatus("还没有可见的学习数据库。可勾选“显示系统库”查看服务器自带数据库，或切换有权限的账号。");
            sqlContext.Text = "当前数据库：未选择 · 可执行 SHOW DATABASES / CREATE DATABASE 等服务器级 SQL";
            return;
        }
        await connection.ChangeDatabaseAsync(db, token);
        var tables = await browser.GetTablesAsync(db, token);
        tablePicker.Items.AddRange(tables.Cast<object>().ToArray());
        if (tables.Count > 0) tablePicker.SelectedIndex = 0;
        sqlContext.Text = $"当前数据库：{db} · SQL 执行模式 · 每个结果集最多显示 500 行";
        SetStatus(tables.Count == 0 ? $"“{db}”里面还没有可见的表。可以选择另一个数据库。" : $"已找到 {tables.Count} 张表或视图。选一张表，点击“查看数据”。");
    }

    private async Task OpenTableAsync(string database, string table, CancellationToken token)
    {
        var columns = await browser.GetColumnsAsync(database, table, token);
        (DataTable Rows, bool HasMore, bool Ordered) first;
        try { first = await browser.ReadPageAsync(database, table, 0, PageSize, token); }
        catch { columns.Dispose(); throw; }
        currentDatabase = database;
        currentTable = table;
        BindGrid(structureGrid, columns);
        searchText.Clear();
        searchColumn.Items.Clear();
        sortColumn.Items.Clear();
        searchColumn.Items.Add("选择查找列");
        sortColumn.Items.Add("默认顺序");
        foreach (DataColumn column in first.Rows.Columns)
        {
            searchColumn.Items.Add(column.ColumnName);
            sortColumn.Items.Add(column.ColumnName);
        }
        searchColumn.SelectedIndex = sortColumn.SelectedIndex = sortDirection.SelectedIndex = 0;
        ShowPage(first.Rows, first.HasMore, first.Ordered, 0, new BrowseOptions());
        tabs.SelectedTab = dataTab;
    }

    private async Task LoadPageAsync(int targetPage, BrowseOptions options, CancellationToken token)
    {
        if (currentDatabase == null || currentTable == null) return;
        var result = await browser.ReadPageAsync(currentDatabase, currentTable, targetPage, PageSize, token, options);
        ShowPage(result.Rows, result.HasMore, result.Ordered, targetPage, options);
    }

    private void ShowPage(DataTable rows, bool more, bool ordered, int targetPage, BrowseOptions options)
    {
        BindGrid(grid, rows);
        page = targetPage;
        appliedOptions = options;
        hasMore = more;
        tableTitle.Text = $"{currentDatabase}  /  {currentTable}";
        pageLabel.Text = $"第 {page + 1} 页 · {rows.Rows.Count} 行";
        bool filtered = options.SearchColumn != null && options.SearchText.Length > 0;
        resultHint.Text = (filtered ? $"查找：{options.SearchColumn} 包含 “{options.SearchText}”" : "显示全部数据") +
            (options.SortColumn == null ? " · 默认排序" : $" · {options.SortColumn} {(options.Descending ? "从大到小" : "从小到大")}");
        SetStatus(rows.Rows.Count == 0 ? "没有找到数据。可以清空条件，或换一张表。" :
            (ordered ? "读取成功。每页最多 100 行，可用下方按钮继续查看。" : "读取成功。这张表没有主键，翻页顺序可能变化。"));
    }

    private void ClearPage()
    {
        BindGrid(grid, null);
        BindGrid(structureGrid, null);
        currentDatabase = currentTable = null;
        hasMore = false;
        page = 0;
        appliedOptions = new BrowseOptions();
        tableTitle.Text = "你的数据会显示在这里";
        pageLabel.Text = "尚未打开表";
        resultHint.Text = "完成上方的三个步骤，即可开始查看。";
        searchText.Clear();
        searchColumn.Items.Clear();
        sortColumn.Items.Clear();
    }
}
