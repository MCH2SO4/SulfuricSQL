using SulfuricSQL.Services;

namespace SulfuricSQL.Forms;

public sealed partial class WorkspaceForm
{
    private SqlBatchResult? lastBatch;
    private readonly ContextMenuStrip templateMenu = new();
    private sealed record HistoryItem(DateTime Time, string Database, string Sql, string State)
    {
        public override string ToString() => $"{Time:HH:mm:ss} · {State} · {Database} · {Sql.Replace('\n', ' ').Replace('\r', ' ')[..Math.Min(Sql.Length, 65)]}";
    }

    private void WireSqlEvents()
    {
        compatibilityButton.Click += (_, _) => ManagementDialogs.ShowCompatibility(this, connection.ServerVersion, []);
        executeSql.Click += async (_, _) => await RunAsync(token => ExecuteSqlCoreAsync(
            sqlEditor.SelectionLength > 0 ? sqlEditor.SelectedText : sqlEditor.Text, token), "正在执行 SQL…");
        stopSql.Click += (_, _) => operation?.Cancel();
        clearSql.Click += (_, _) => sqlEditor.Clear();
        templateButton.Click += (_, _) =>
        {
            var menu = templateMenu;
            while (menu.Items.Count > 0) menu.Items[0].Dispose();
            foreach (var pair in SqlTemplates.Create(databasePicker.SelectedItem as string, tablePicker.SelectedItem as string))
            {
                string text = pair.Value;
                menu.Items.Add(pair.Key, null, (_, _) => InsertSql(text));
            }
            menu.Show(templateButton, new Point(0, templateButton.Height));
        };
        historyPicker.SelectionChangeCommitted += (_, _) =>
        {
            if (historyPicker.SelectedItem is HistoryItem item) InsertSql(item.Sql);
        };
        resultPicker.SelectedIndexChanged += (_, _) =>
        {
            if (resultPicker.SelectedItem is ResultItem item) BindGrid(sqlGrid, item.Result.Rows?.Copy());
        };
        queryExample.Click += (_, _) =>
        {
            if (currentDatabase == null || currentTable == null) return;
            SetMode(true); tabs.SelectedTab = sqlTab;
            InsertSql($"SELECT *\nFROM {DatabaseBrowserService.Quote(currentDatabase)}.{DatabaseBrowserService.Quote(currentTable)}\nLIMIT 100;");
            SetStatus("已插入查询示例。模板和历史记录只插入文本，不会自动执行。");
        };
        sqlEditor.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.Enter))
            { e.SuppressKeyPress = true; executeSql.PerformClick(); }
        };
    }

    private void InsertSql(string text)
    {
        if (sqlEditor.TextLength > 0) sqlEditor.AppendText("\n\n");
        int start = sqlEditor.TextLength;
        sqlEditor.AppendText(text);
        sqlEditor.Select(start, text.Length);
        sqlEditor.Focus();
    }

    private sealed record ResultItem(int Number, SqlExecutionResult Result)
    {
        public override string ToString() => $"语句 {Number} · {Result.Kind} · {Result.Rows?.Rows.Count ?? 0} 行{(Result.Truncated ? "（仅前 500 行）" : "")}";
    }

    private bool ConfirmDangerousSql(string description) => confirmationForTests?.Invoke(description) ?? ManagementDialogs.Confirm(this, description);

    private async Task ExecuteSqlCoreAsync(string text, CancellationToken token)
    {
        if (professional) tabs.SelectedTab = sqlTab;
        if (tabs.SelectedTab == sqlTab && !outputTabs.Visible)
            ((Button)sqlTab.Controls.Find("focusEditor", true).Single()).PerformClick();
        string? db = databasePicker.SelectedItem as string;
        BindGrid(sqlGrid, null);
        resultPicker.Items.Clear();
        lastBatch?.Dispose(); lastBatch = null;
        sqlOutput.Clear(); sqlResult.Text = "正在执行…";
        var batch = await execution.ExecuteAsync(db, text, ConfirmDangerousSql, token,
            issues => ManagementDialogs.ShowCompatibility(this, connection.ServerVersion, issues));
        lastBatch = batch;
        foreach (var issue in batch.CompatibilityIssues)
            sqlOutput.AppendText($"[版本兼容 · {connection.ServerVersion}] {issue.Message}" + Environment.NewLine);
        for (int i = 0; i < batch.Results.Count; i++)
        {
            var result = batch.Results[i];
            sqlOutput.AppendText($"[{i + 1}] {result.Sql}\r\n" +
                (result.Success ? $"成功 · 影响行数 {result.AffectedRows} · 返回 {result.Rows?.Rows.Count ?? 0} 行{(result.Truncated ? "（仅显示前 500 行）" : "")}" : "失败 · " + result.Error) +
                $" · 耗时 {result.Milliseconds} 毫秒\r\n\r\n");
            if (result.Rows != null && result.Success) resultPicker.Items.Add(new ResultItem(i + 1, result));
        }
        if (batch.Cancelled) sqlOutput.AppendText("已取消或停止。未执行的后续语句不会继续；已完成的语句不会自动撤销。\r\n");
        if (!batch.Success && batch.Results.Count > 1) sqlOutput.AppendText("脚本已停止。请根据逐条结果核对已完成的操作。\r\n");
        if (resultPicker.Items.Count > 0) resultPicker.SelectedIndex = resultPicker.Items.Count - 1;
        outputTabs.SelectedIndex = batch.Success && resultPicker.Items.Count > 0 ? 0 : 1;
        string state = batch.Cancelled ? "已取消" : batch.Success ? "成功" : "失败";
        sqlResult.Text = $"{state} · {batch.Results.Count} 条结果 · 合计 {batch.Results.Sum(r => r.Milliseconds)} 毫秒";
        historyPicker.Items.Insert(0, new HistoryItem(DateTime.Now, db ?? "服务器", text, state));
        while (historyPicker.Items.Count > 50) historyPicker.Items.RemoveAt(50);
        historyPicker.SelectedIndex = 0;
        if (connection.State != System.Data.ConnectionState.Open) { MarkDisconnected(); return; }
        // 即使脚本中途报错，前面的 DDL/USE 也可能已经生效。
        if (batch.ChangesSchema || !string.Equals(db, batch.CurrentDatabase, StringComparison.Ordinal))
        {
            try { await RefreshAfterExecutionAsync(batch.CurrentDatabase, lifetime.Token); }
            catch (Exception error) { sqlOutput.AppendText("自动刷新失败：" + SqlExecutionService.DescribeError(error) + "\r\n"); }
        }
        else if (batch.ChangesData && currentTable != null)
            resultHint.Text = "数据库内容已变更。点击“刷新数据”读取最新内容。";
        SetStatus(batch.Success ? "执行完成。查询结果和逐条执行信息可在下方切换查看。" : "本次执行未完成，详细信息见“执行输出”。", !batch.Success && !batch.Cancelled);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (tabs.SelectedTab == sqlTab && (keyData == Keys.F5 || keyData == (Keys.Control | Keys.Enter)))
        { executeSql.PerformClick(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async Task RefreshAfterExecutionAsync(string? selected, CancellationToken token)
    {
        databases = await browser.GetDatabasesAsync(token);
        tree.Nodes.Clear();
        foreach (string name in databases)
        {
            var node = new TreeNode(name);
            node.Nodes.Add(new TreeNode("展开以加载表…") { Tag = true });
            tree.Nodes.Add(node);
        }
        syncing = true;
        try
        {
            if (selected != null && IsSystemDatabase(selected)) showSystem.Checked = true;
            PopulateDatabaseItems();
            databasePicker.SelectedItem = selected;
            if (selected == null) databasePicker.SelectedIndex = -1;
        }
        finally { syncing = false; }
        await LoadSelectedTablesAsync(token);
    }
}




