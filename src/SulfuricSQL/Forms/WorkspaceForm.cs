using System.Data;
using MySqlConnector;
using SulfuricSQL.Models;
using SulfuricSQL.Services;

namespace SulfuricSQL.Forms;

public sealed partial class WorkspaceForm : Form
{
    private readonly MySqlConnection connection;
    private readonly DatabaseBrowserService browser;
    private readonly SqlExecutionService execution;
    private readonly DatabaseService databaseService;
    private readonly Func<string, bool>? confirmationForTests;
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer heartbeat = new() { Interval = 10000 };
    private CancellationTokenSource? operation;
    private bool busy, closing, disconnected, hasMore, syncing, professional;
    private string? currentDatabase, currentTable;
    private List<string> databases = [];
    private BrowseOptions appliedOptions = new();
    private int page;
    private const int PageSize = 100;

    public WorkspaceForm(MySqlConnection connection, string serverDisplay, Func<string, bool>? confirmationForTests = null)
    {
        this.connection = connection;
        browser = new DatabaseBrowserService(connection);
        execution = new SqlExecutionService(connection);
        databaseService = new DatabaseService(connection);
        this.confirmationForTests = confirmationForTests;
        InitializeLayout(serverDisplay);
        AppTheme.Apply(this); AppTheme.Apply(structureTab); AppTheme.Apply(sqlTab);
        UiMotion.Attach(this);
        openCode.Click += (_, _) => { if (!tabs.TabPages.Contains(sqlTab)) tabs.TabPages.Add(sqlTab); tabs.SelectedTab = sqlTab; sqlEditor.Focus(); };
        WireBrowsingEvents();
        WireSqlEvents();
        WireManagementEvents();
        tabs.SelectedIndexChanged += (_, _) => PageTransition.Play(tabs.SelectedTab);
        outputTabs.SelectedIndexChanged += (_, _) => PageTransition.Play(outputTabs.SelectedTab);
        simpleMode.Click += (_, _) => SetMode(false);
        proMode.Click += (_, _) => SetMode(true);
        cancelOperation.Click += (_, _) => operation?.Cancel();
        disconnect.Click += (_, _) => Close();
        Shown += async (_, _) => await RunAsync(LoadDatabasesAsync, "正在加载可用数据库…");
        heartbeat.Tick += async (_, _) =>
        {
            if (busy || closing || disconnected) return;
            await RunAsync(async token => { if (!await connection.PingAsync(token)) MarkDisconnected(); }, null);
        };
        FormClosing += (_, e) =>
        {
            heartbeat.Stop();
            closing = true;
            lifetime.Cancel();
            if (busy) { e.Cancel = true; status.Text = "正在结束当前操作并断开…"; }
        };
        SetMode(false);
        heartbeat.Start();
    }

    private void SetMode(bool advanced)
    {
        tabs.SuspendLayout();
        split.SuspendLayout();
        try
        {
        professional = advanced;
        manageButton.Visible = advanced;
        split.Panel1Collapsed = !advanced;
        if (advanced)
        {
            if (!tabs.TabPages.Contains(structureTab)) tabs.TabPages.Insert(1, structureTab);
            if (!tabs.TabPages.Contains(sqlTab)) tabs.TabPages.Add(sqlTab);
        }
        else
        {
            tabs.TabPages.Remove(structureTab);
            // 简单模式按需打开编辑器，切换模式保留已经打开的 SQL 页面。
        }
        simpleMode.BackColor = !advanced ? Accent : Color.White;
        simpleMode.ForeColor = !advanced ? Color.White : Ink;
        proMode.BackColor = advanced ? Accent : Color.White;
        proMode.ForeColor = advanced ? Color.White : Ink;
        guide.Text = advanced
            ? "专业模式  ·  左侧管理数据库对象，右侧查看数据、表结构或编写 SQL。"
            : "简单模式  ·  ① 选数据库   →   ② 选一张表   →   ③ 查看数据，不需要写 SQL。";
        previous.Text = advanced ? "上一页" : "上一批";
        next.Text = advanced ? "下一页" : "下一批";
        UpdateButtons();
        }
        finally
        {
            split.ResumeLayout(true);
            tabs.ResumeLayout(true);
            tabs.Invalidate();
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, string? message)
    {
        if (busy || closing || disconnected) return;
        busy = true;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = pending;
        if (message != null) SetStatus(message);
        UpdateButtons();
        try { await action(pending.Token); }
        catch (OperationCanceledException) { if (!closing) SetStatus("已停止。可以修改选项后重试。"); }
        catch (Exception error)
        {
            if (connection.State != ConnectionState.Open) MarkDisconnected();
            else if (error is ArgumentException) SetStatus(error.Message, true);
            else if (error is MySqlException sql)
            {
                string detail = sql.Number switch
                {
                    1146 => "这张表已经不存在，请刷新数据库列表。",
                    1044 or 1142 => "当前账号没有读取权限，请使用有权限的账号。",
                    1054 => "字段不存在，表结构可能已改变，请重新打开这张表。",
                    1064 => "SQL 语法有误，请检查关键字、逗号和引号。",
                    1317 or 3024 => "查询已停止或等待超时，可以减少查询范围后重试。",
                    _ => "请检查选择的数据库、表名和查询内容后重试。"
                };
                SetStatus($"操作未完成（MySQL {sql.Number}）· {detail}", true);
                sqlOutput.AppendText(Environment.NewLine + SqlExecutionService.DescribeError(sql) + Environment.NewLine);
            }
            else SetStatus("操作未完成，请刷新或重新连接后重试。", true);
        }
        finally
        {
            operation = null;
            busy = false;
            if (!IsDisposed) { UpdateButtons(); if (closing) Close(); }
        }
    }

    private void SetStatus(string text, bool error = false)
    {
        status.Text = text;
        status.ForeColor = error ? Color.Firebrick : Accent;
    }

    private void UpdateButtons()
    {
        bool ready = !busy && !closing && !disconnected;
        tree.Enabled = refreshTree.Enabled = refreshSources.Enabled = ready;
        databasePicker.Enabled = tablePicker.Enabled = showSystem.Enabled = ready;
        openTable.Enabled = ready && tablePicker.SelectedItem != null;
        simpleMode.Enabled = proMode.Enabled = ready;
        filterPanel.Enabled = ready && currentTable != null;
        refreshData.Enabled = queryExample.Enabled = ready && currentTable != null;
        previous.Enabled = ready && currentTable != null && page > 0;
        next.Enabled = ready && currentTable != null && hasMore;
        executeSql.Enabled = openCode.Enabled = ready;
        clearSql.Enabled = templateButton.Enabled = historyPicker.Enabled = ready;
        manageButton.Enabled = ready && professional;
        codeManageButton.Enabled = compatibilityButton.Enabled = ready;
        sqlEditor.ReadOnly = busy;
        cancelOperation.Enabled = stopSql.Enabled = busy && !closing;
        progress.Visible = busy;
    }

    private void MarkDisconnected()
    {
        disconnected = true;
        heartbeat.Stop();
        serverHeader.Text = "SulfuricSQL / 连接已中断";
        SetStatus("连接已中断。点击右上角“断开连接”，然后重新连接。", true);
    }

    private static void BindGrid(DataGridView target, DataTable? rows)
    {
        var old = target.DataSource as DataTable;
        target.DataSource = null;
        target.Columns.Clear();
        if (rows != null)
        {
            foreach (DataColumn column in rows.Columns)
                target.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = column.ColumnName, HeaderText = column.ColumnName,
                    DataPropertyName = column.ColumnName, SortMode = DataGridViewColumnSortMode.NotSortable
                });
            target.DataSource = rows;
        }
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            heartbeat.Dispose();
            managementMenu.Dispose();
            templateMenu.Dispose();
            lifetime.Dispose();
            (grid.DataSource as DataTable)?.Dispose();
            (structureGrid.DataSource as DataTable)?.Dispose();
            (sqlGrid.DataSource as DataTable)?.Dispose();
            lastBatch?.Dispose();
            structureTab.Dispose();
            sqlTab.Dispose();
        }
        base.Dispose(disposing);
    }
}






