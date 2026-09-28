using SulfuricSQL.Services;

namespace SulfuricSQL.Forms;

public sealed partial class WorkspaceForm
{
    // 菜单关闭后 WinForms 仍会访问它；由工作区统一管理生命周期。
    private readonly ContextMenuStrip managementMenu = new();

    private void WireManagementEvents()
    {
        codeManageButton.Click += (_, _) => ShowManagementMenu(null, codeManageButton, new Point(0, codeManageButton.Height));
        bool rightClicking = false;
        tree.MouseDown += (_, e) => rightClicking = e.Button == MouseButtons.Right;
        manageButton.Click += (_, _) => ShowManagementMenu(null, manageButton, new Point(0, manageButton.Height));
        tree.AfterSelect += async (_, e) =>
        {
            if (rightClicking || syncing || busy || e.Node == null || e.Node.Tag is true) return;
            var node = e.Node;
            await RunAsync(token => SelectTreeDatabaseAsync(node, token), "正在选择当前数据库…");
        };
        tree.NodeMouseClick += async (_, e) =>
        {
            rightClicking = false;
            if (e.Node == null || e.Button != MouseButtons.Right || busy || e.Node.Tag is true) return;
            syncing = true;
            try { tree.SelectedNode = e.Node; } finally { syncing = false; }
            await RunAsync(token => SelectTreeDatabaseAsync(e.Node, token), null);
            if (!closing && !disconnected) ShowManagementMenu(e.Node, tree, e.Location);
        };
    }

    private async Task SelectTreeDatabaseAsync(TreeNode node, CancellationToken token)
    {
        string db = node.Level == 0 ? node.Text : node.Parent!.Text;
        if (!string.Equals(databasePicker.SelectedItem as string, db, StringComparison.Ordinal))
        {
            syncing = true;
            try
            {
                if (!databasePicker.Items.Contains(db)) { showSystem.Checked = true; PopulateDatabaseItems(); }
                databasePicker.SelectedItem = db;
            }
            finally { syncing = false; }
            await LoadSelectedTablesAsync(token);
        }
        if (node.Level == 1) tablePicker.SelectedItem = node.Text;
    }

    private void ShowManagementMenu(TreeNode? node, Control anchor, Point location)
    {
        if (busy || closing || disconnected) return;
        string? db = node == null ? databasePicker.SelectedItem as string : node.Level == 0 ? node.Text : node.Parent!.Text;
        string? table = node == null ? tablePicker.SelectedItem as string : node.Level == 1 ? node.Text : null;
        var menu = managementMenu;
        while (menu.Items.Count > 0) menu.Items[0].Dispose();
        void Add(string title, Func<CancellationToken, Task> action, bool enabled = true)
        {
            var item = new ToolStripMenuItem(title) { Enabled = enabled };
            item.Click += async (_, _) => await RunAsync(action, "正在处理管理操作…");
            menu.Items.Add(item);
        }
        Add("创建数据库…", async token =>
        {
            using var collations = await databaseService.GetCollationsAsync(token);
            string? sql = ManagementDialogs.EditDatabase(this, collations);
            if (sql != null) await ExecuteSqlCoreAsync(sql, token);
        });
        Add("数据库属性…", async token =>
        {
            using var properties = await databaseService.GetPropertiesAsync(db!, token);
            ManagementDialogs.ShowProperties(this, properties);
        }, db != null);
        Add("修改字符集 / 排序规则…", async token =>
        {
            using var collations = await databaseService.GetCollationsAsync(token);
            using var properties = await databaseService.GetPropertiesAsync(db!, token);
            if (properties.Rows.Count == 0) throw new ArgumentException("数据库已不存在，请刷新。");
            string? sql = ManagementDialogs.EditDatabase(this, collations, db, Convert.ToString(properties.Rows[0][1]), Convert.ToString(properties.Rows[0][2]));
            if (sql != null) await ExecuteSqlCoreAsync(sql, token);
        }, db != null);
        Add("删除数据库…", token => ExecuteSqlCoreAsync(DatabaseService.DropDatabase(db!), token), db != null && !IsSystemDatabase(db));
        Add("刷新数据库", LoadDatabasesAsync);
        menu.Items.Add(new ToolStripSeparator());
        Add("新建表…", async token =>
        {
            string? sql = ManagementDialogs.CreateTable(this, db!);
            if (sql != null) await ExecuteSqlCoreAsync(sql, token);
        }, db != null);
        Add("查看数据", token => OpenTableAsync(db!, table!, token), table != null);
        Add("查看表结构", async token =>
        {
            BindGrid(structureGrid, await browser.GetColumnsAsync(db!, table!, token));
            if (!tabs.TabPages.Contains(structureTab)) tabs.TabPages.Insert(1, structureTab);
            tabs.SelectedTab = structureTab;
        }, table != null);
        Add("重命名表…", async token =>
        {
            string? newName = ManagementDialogs.Rename(this, table!);
            if (newName != null && newName != table) await ExecuteSqlCoreAsync(DatabaseService.RenameTable(db!, table!, newName), token);
        }, table != null);
        Add("清空表…", token => ExecuteSqlCoreAsync(DatabaseService.TruncateTable(db!, table!), token), table != null);
        Add("删除表…", token => ExecuteSqlCoreAsync(DatabaseService.DropTable(db!, table!), token), table != null);
        Add("刷新当前表", token => OpenTableAsync(db!, table!, token), table != null);
        Add("刷新表列表", async token =>
        {
            await LoadSelectedTablesAsync(token);
            var dbNode = tree.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Text == db);
            if (dbNode != null)
            {
                dbNode.Nodes.Clear();
                foreach (string name in await browser.GetTablesAsync(db!, token)) dbNode.Nodes.Add(new TreeNode(name));
                dbNode.Expand();
            }
        }, db != null);
        menu.Show(anchor, location);
    }
}


