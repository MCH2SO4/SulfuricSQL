using SulfuricSQL.Forms;
using SulfuricSQL.Models;
using SulfuricSQL.Services;

// 在真正的 WinForms 消息循环中检查交互，不依赖鼠标坐标。
internal static class WorkspaceUiChecks
{
    public static Task RunAsync(ConnectionSettings settings, string database)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                ApplicationConfiguration.Initialize();
                using var connection = new MySqlConnectionService().ConnectAsync(settings).GetAwaiter().GetResult();
                bool approveDangerous = false;
                int dangerRequests = 0;
                Exception? uiThreadError = null;
                Application.ThreadException += (_, e) => uiThreadError = e.Exception;
                using var form = new WorkspaceForm(connection, "界面验证 · 临时数据库", _ => { dangerRequests++; return approveDangerous; }) { ShowInTaskbar = false, Opacity = 0 };
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        int checks = 0;
                        T Find<T>(string name) where T : Control => (T)form.Controls.Find(name, true).Single();
                        void Check(bool ok, string name)
                        {
                            if (!ok) throw new Exception("UI FAIL: " + name);
                            Console.WriteLine("UI PASS: " + name);
                            checks++;
                        }
                        async Task Idle()
                        {
                            var timer = System.Diagnostics.Stopwatch.StartNew();
                            while (!Find<Button>("simpleMode").Enabled)
                            {
                                if (timer.Elapsed.TotalSeconds > 20) throw new TimeoutException("UI operation did not complete: " + Find<Label>("status").Text);
                                await Task.Delay(40);
                            }
                        }
                        void Capture(string name, Control? target = null)
                        {
                            string? directory = Environment.GetEnvironmentVariable("SULFURICSQL_PREVIEW_DIRECTORY");
                            if (string.IsNullOrEmpty(directory)) return;
                            Directory.CreateDirectory(directory);
                            target ??= form;
                            using var bitmap = new Bitmap(target.Width, target.Height);
                            target.DrawToBitmap(bitmap, new Rectangle(Point.Empty, target.Size));
                            bitmap.Save(Path.Combine(directory, name + ".png"));
                        }
                        await Idle();
                        // 用户实际触发路径：简单模式先打开 SQL，再切换专业模式。
                        Find<Button>("openCode").PerformClick();
                        Find<Button>("proMode").PerformClick();
                        await Task.Delay(100);
                        var initialTabs = Find<TabControl>("tabs");
                        Check(uiThreadError == null, "simple SQL to professional mode has no tab drawing exception");
                        Check(initialTabs.TabPages.Count == 3 && initialTabs.TabPages[0].Text == "查看数据" &&
                            initialTabs.TabPages[1].Text == "表结构" && initialTabs.TabPages[2].Text == "SQL 查询",
                            "professional tabs remain ordered after SQL was opened first");
                        Find<Button>("simpleMode").PerformClick();
                        initialTabs.SelectedIndex = 0;
                        var db = Find<ComboBox>("databasePicker");
                        Check(db.Items.Cast<string>().All(name => name != "mysql" && name != "sys"), "simple mode hides system databases");
                        db.SelectedItem = database;
                        await Idle();
                        Find<ComboBox>("tablePicker").SelectedItem = "学生";
                        Find<Button>("openTable").PerformClick();
                        await Idle();
                        var grid = Find<DataGridView>("grid");
                        Check(grid.Rows.Count == 100 && grid.Columns.Count == 4, "three-step open loads data");
                        Check(Find<TabControl>("tabs").TabPages.Count == 2, "simple mode retains the SQL page opened by the user");
                        Check(grid.ReadOnly, "data grid remains read-only");
                        foreach (string name in new[] { "openTable", "refreshSources", "applyFilter", "resetFilter" })
                        {
                            var button = Find<Button>(name);
                            Check(button.Width >= button.GetPreferredSize(Size.Empty).Width, name + " label fits at current DPI");
                        }
                        Find<ComboBox>("searchColumn").SelectedItem = "姓名";
                        Find<TextBox>("searchText").Text = "同学";
                        Find<ComboBox>("sortColumn").SelectedItem = "id";
                        Find<ComboBox>("sortDirection").SelectedIndex = 1;
                        Find<Button>("applyFilter").PerformClick();
                        await Idle();
                        Check(Convert.ToInt32(grid.Rows[0].Cells[0].Value) == 205, "simple filter and sort apply");
                        Find<Button>("next").PerformClick();
                        await Idle();
                        Check(Convert.ToInt32(grid.Rows[0].Cells[0].Value) == 105, "paging retains applied filter");
                        Find<Button>("proMode").PerformClick();
                        Check(Find<TabControl>("tabs").TabPages.Count == 3, "professional mode exposes structure and SQL");
                        Check(Find<DataGridView>("structureGrid").Rows.Count == 4, "structure tab has field metadata");
                        Find<Button>("manageButton").PerformClick();
                        await Task.Delay(100);
                        Check(form.Visible && !form.IsDisposed, "opening management menu keeps workspace open");
                        ContextMenuStrip Menu(string field) => (ContextMenuStrip)typeof(WorkspaceForm)
                            .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(form)!;
                        var managementMenu = Menu("managementMenu");
                        for (int i = 0; i < 3; i++)
                        {
                            managementMenu.Close(ToolStripDropDownCloseReason.Keyboard);
                            await Task.Delay(30);
                            Find<Button>("manageButton").PerformClick();
                        }
                        Check(managementMenu.Visible && !managementMenu.IsDisposed, "management menu can close and reopen repeatedly");
                        var editor = Find<RichTextBox>("sqlEditor");
                        Find<TabControl>("tabs").SelectedIndex = 2;
                        editor.Clear();
                        Find<Button>("templateButton").PerformClick();
                        var templates = Menu("templateMenu");
                        var template = templates.Items[0];
                        templates.Close(ToolStripDropDownCloseReason.ItemClicked);
                        template.PerformClick();
                        await Task.Delay(30);
                        Check(editor.Text.Contains("SHOW DATABASES") && Find<ComboBox>("historyPicker").Items.Count == 0,
                            "template selection inserts text without executing or closing workspace");
                        Find<Button>("templateButton").PerformClick();
                        templates.Close(ToolStripDropDownCloseReason.AppClicked);
                        await Task.Delay(30);
                        Check(form.Visible && !templates.IsDisposed, "template menu remains reusable after dismissal");
                        editor.Text = "SELECT '保留我的草稿';";
                        Find<Button>("simpleMode").PerformClick();
                        Check(Convert.ToInt32(grid.Rows[0].Cells[0].Value) == 105 && Find<TextBox>("searchText").Text == "同学", "mode switch retains page and filter");
                        Capture("simple-mode");
                        Find<Button>("proMode").PerformClick();
                        Check(editor.Text.Contains("保留我的草稿"), "SQL draft survives mode switch");
                        Find<TabControl>("tabs").SelectedIndex = 0;
                        Find<Button>("queryExample").PerformClick();
                        Check(editor.Text.Contains("保留我的草稿") && editor.SelectedText.Contains("LIMIT 100"), "example appends and selects without destroying draft");
                        Find<Button>("executeSql").PerformClick();
                        await Idle();
                        Check(Find<DataGridView>("sqlGrid").Rows.Count == 100, "run selected SQL displays query results");
                        Capture("professional-mode");
                        editor.Text = "SELECT SLEEP(5)";
                        Find<Button>("executeSql").PerformClick();
                        await Task.Delay(200);
                        Find<Button>("stopSql").PerformClick();
                        await Idle();
                        Check(Find<Button>("executeSql").Enabled, "stop query restores usable UI");
                        Find<Button>("simpleMode").PerformClick();
                        Find<TabControl>("tabs").SelectedIndex = 0;
                        Find<Button>("resetFilter").PerformClick();
                        await Idle();
                        Check(Convert.ToInt32(grid.Rows[0].Cells[0].Value) == 1 && Find<TextBox>("searchText").Text == "", "reset returns to first unfiltered page");
                        var refreshItem = managementMenu.Items.Cast<ToolStripItem>().Single(item => item.Text == "刷新数据库");
                        managementMenu.Close(ToolStripDropDownCloseReason.ItemClicked);
                        refreshItem.PerformClick();
                        await Idle();
                        Check(form.Visible && await connection.PingAsync(), "menu refresh retains workspace and MySQL session");


                        Find<Button>("proMode").PerformClick();
                        Find<TabControl>("tabs").SelectedIndex = 2;
                        async Task RunSql(string sql, Keys? shortcut = null)
                        {
                            editor.Text = sql;
                            if (shortcut == null) Find<Button>("executeSql").PerformClick();
                            else
                            {
                                var method = typeof(WorkspaceForm).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                                method.Invoke(form, [Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero), shortcut.Value]);
                            }
                            await Idle();
                        }
                        string uiDb = "sulfuricsql_ui_" + Guid.NewGuid().ToString("N")[..8];
                        var dialogType = typeof(WorkspaceForm).Assembly.GetType("SulfuricSQL.Forms.ManagementDialogs")!;
                        object? TestDialog(string method, object?[] arguments, Action<Form> act)
                        {
                            Exception? problem = null;
                            using var tick = new System.Windows.Forms.Timer { Interval = 70 };
                            tick.Tick += (_, _) =>
                            {
                                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f != form);
                                if (dialog == null) return;
                                tick.Stop();
                                try { act(dialog); }
                                catch (Exception error) { problem = error; dialog.Close(); }
                            };
                            tick.Start();
                            object? result = dialogType.GetMethod(method)!.Invoke(null, arguments);
                            if (problem != null) throw problem;
                            return result;
                        }
                        using (var collations = await new DatabaseService(connection).GetCollationsAsync(default))
                        {
                            var sql = (string?)TestDialog("EditDatabase", [form, collations, null, null, null], dialog =>
                            {
                                ((TextBox)dialog.Controls.Find("databaseName", true).Single()).Text = uiDb;
                                Capture("create-database", dialog);
                                ((Button)dialog.Controls.Find("saveDatabase", true).Single()).PerformClick();
                            });
                            Check(sql != null, "create database dialog produces valid SQL");
                            await RunSql(sql!);
                            Check(db.Items.Contains(uiDb), "created database appears in selector and tree");
                        }
                        await RunSql($"USE `{uiDb}`;", Keys.Control | Keys.Enter);
                        Check(db.SelectedItem?.ToString() == uiDb, "Ctrl+Enter execution and USE update current database");
                        var createSql = (string?)TestDialog("CreateTable", [form, uiDb], dialog =>
                        {
                            ((TextBox)dialog.Controls.Find("tableName", true).Single()).Text = "ui_students";
                            Capture("create-table", dialog);
                            ((Button)dialog.Controls.Find("createTable", true).Single()).PerformClick();
                        });
                        Check(createSql != null, "visual create table dialog produces SQL");
                        await RunSql(createSql!);
                        Check(Find<ComboBox>("tablePicker").Items.Contains("ui_students"), "created table appears after automatic refresh");
                        await RunSql("INSERT INTO ui_students(name) VALUES ('测试同学');", Keys.F5);
                        Check(Find<TextBox>("sqlOutput").Text.Contains("影响行数 1"), "F5 writes and displays affected row count");
                        await RunSql("SELECT * FROM ui_students;");
                        Check(Find<DataGridView>("sqlGrid").Rows.Count == 1, "inserted row visible in SQL results");
                        await RunSql("UPDATE ui_students SET name='改名同学' WHERE id=1; DELETE FROM ui_students WHERE id=1;");
                        Check(Find<TextBox>("sqlOutput").Text.Contains("[2]") && Find<TextBox>("sqlOutput").Text.Contains("影响行数 1"), "UPDATE and DELETE output shown individually");
                        await RunSql("DROP TABLE ui_students;");
                        Check(dangerRequests == 1 && Find<TextBox>("sqlOutput").Text.Contains("已取消"), "declining dangerous SQL prevents execution");
                        await RunSql("SHOW TABLES;");
                        Check(Find<DataGridView>("sqlGrid").Rows.Count == 1, "cancelled DROP leaves table present");
                        bool confirmed = (bool)TestDialog("Confirm", [form, "DROP TABLE ui_students;"], dialog =>
                        {
                            Check(((Button)dialog.AcceptButton!).DialogResult == DialogResult.Cancel, "real danger dialog defaults Enter to Cancel");
                            Capture("danger-confirmation", dialog);
                            ((Button)dialog.Controls.Find("cancelDanger", true).Single()).PerformClick();
                        })!;
                        Check(!confirmed, "real confirmation dialog Cancel returns false");
                        approveDangerous = true;
                        await RunSql("DROP TABLE ui_students;");
                        Check(Find<ComboBox>("tablePicker").Items.Count == 0, "confirmed DROP TABLE refreshes UI");
                        await RunSql($"DROP DATABASE `{uiDb}`;");
                        Check(!db.Items.Contains(uiDb) && db.SelectedIndex == -1, "confirmed DROP DATABASE removes database and clears context");
                        await RunSql("DROP DATABASE mysql;");
                        Check(Find<TextBox>("sqlOutput").Text.Contains("禁止删除系统数据库"), "system database protection visible in output");
                        await RunSql($"USE `{database}`; SELECT VERSION(); SHOW VARIABLES LIKE 'version';");
                        Check(Find<ComboBox>("resultPicker").Items.Count == 2, "multiple query results selectable");
                        Check(Find<ComboBox>("historyPicker").Items.Count > 10, "execution history populated");
                        Find<Button>("clearSql").PerformClick();
                        Check(editor.TextLength == 0, "clear only clears editor");
                        Capture("management-sql");
                        Find<Button>("simpleMode").PerformClick();
                        Find<Button>("openCode").PerformClick();
                        Check(Find<TabControl>("tabs").SelectedTab!.Text == "SQL 查询" && Find<Button>("executeSql").Enabled,
                            "simple mode opens an executable SQL editor");
                        await RunSql("SELECT 42 AS answer;", Keys.F5);
                        Check(Convert.ToInt32(Find<DataGridView>("sqlGrid").Rows[0].Cells[0].Value) == 42, "simple mode F5 executes SQL");
                        editor.Text = "SELECT 'hello', 42; -- comment";
                        editor.Select(3, 0);
                        editor.GetType().GetMethod("Highlight")!.Invoke(editor, null);
                        Check(editor.SelectionStart == 3 && editor.Text == "SELECT 'hello', 42; -- comment", "highlight preserves SQL and caret");
                        editor.Select(0, 6); Color keyword = editor.SelectionColor;
                        editor.Select(7, 7); Color literal = editor.SelectionColor;
                        editor.Select(23, 4); Color comment = editor.SelectionColor;
                        Check(keyword != literal && literal != comment && keyword != comment, "keywords strings and comments use distinct colors");
                        editor.Clear(); editor.ClearUndo(); editor.SelectedText = "SELECT 123;";
                        editor.GetType().GetMethod("Highlight")!.Invoke(editor, null);
                        editor.Undo();
                        Check(editor.TextLength == 0, "syntax colors do not consume text undo");
                        editor.Text = "SEL"; editor.Select(3, 0);
                        var editorKey = editor.GetType().GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                        editorKey.Invoke(editor, [Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero), Keys.Control | Keys.Space]);
                        editorKey.Invoke(editor, [Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero), Keys.Tab]);
                        Check(editor.Text == "SELECT", "Ctrl+Space and Tab complete SQL keyword");
                        var popup = (ToolStripDropDown)editor.GetType().GetField("suggestions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!;
                        void SuggestWord(string value)
                        {
                            editor.Text = value; editor.Select(editor.TextLength, 0);
                            editorKey.Invoke(editor, [Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero), Keys.Control | Keys.Space]);
                        }
                        Check(!popup.Visible, "completion closes after acceptance");
                        await Task.Delay(220);
                        Check(!popup.Visible, "highlight timer never reopens completed suggestion");
                        editor.Focus(); editor.Text = "SEL"; editor.Select(3, 0);
                        editor.GetType().GetMethod("OnKeyUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(editor, [new KeyEventArgs(Keys.L)]);
                        await Task.Delay(30);
                        Check(popup.Visible, "typing partial keyword automatically opens completion");
                        editor.Select(0, 0); Check(!popup.Visible, "moving caret dismisses suggestions");
                        SuggestWord("SELECT"); Check(!popup.Visible, "complete keyword has no leftover suggestion");
                        SuggestWord("XYZ"); Check(!popup.Visible, "unmatched prefix dismisses suggestions");
                        SuggestWord("'SEL"); Check(!popup.Visible, "string context suppresses suggestions");
                        SuggestWord("SEL"); editor.SelectedText = " "; Check(!popup.Visible, "space dismisses suggestions immediately");
                        SuggestWord("SEL"); editor.SelectedText = ";"; Check(!popup.Visible, "semicolon dismisses suggestions immediately");
                        SuggestWord("SEL"); Find<TabControl>("tabs").SelectedIndex = 0; Check(!popup.Visible, "leaving editor dismisses suggestions");
                        Find<Button>("simpleMode").PerformClick(); Find<Button>("openCode").PerformClick();
                        Find<Button>("codeManageButton").PerformClick();
                        Check(Menu("managementMenu").Visible, "code toolbar opens database management in simple mode");
                        Menu("managementMenu").Close();
                        editor.Text = "SELETC name FROM student;";
                        editor.GetType().GetMethod("Highlight")!.Invoke(editor, null);
                        editor.Select(0, 6);
                        Check(editor.SelectionColor == Color.FromArgb(244, 91, 105) && editor.Rtf!.Contains(@"\ulwave"), "misspelled statement keyword has red text and native wavy underline");
                        Check(SqlEditorDiagnostics.Analyze("SELECT name, order_id FROM student;").Count == 0, "table and column identifiers are not flagged");
                        Check(SqlEditorDiagnostics.Analyze("SELECT 'unclosed").Count == 1, "unclosed quote has an editor diagnostic");
                        Check(SqlEditorDiagnostics.Analyze("-- SELETC\nSELECT 'SELETC';").Count == 0, "comments and strings are not checked as SQL words");
                        editor.Select(editor.TextLength, 0);
                        await Task.Delay(220); Capture("sql-error-diagnostic");
                        editor.Text = "SELECT name FROM student;";
                        editor.GetType().GetMethod("Highlight")!.Invoke(editor, null);
                        editor.Select(0, 6);
                        Check(editor.SelectionColor != Color.FromArgb(244, 91, 105) && !editor.Rtf!.Contains(@"\ulwave"), "correcting typo clears red wavy marking");
                        editor.Text = "SELECT";
                        Find<Button>("proMode").PerformClick();
                        Find<Button>("openCode").PerformClick();
                        Check(editor.Text == "SELECT", "both mode editor buttons retain same draft");
                        Find<Button>("codeManageButton").PerformClick();
                        Check(Menu("managementMenu").Visible, "code toolbar opens management in professional mode");
                        Menu("managementMenu").Close();
                        Find<Button>("focusEditor").PerformClick();
                        await Task.Delay(220);
                        Check(!Find<TabControl>("outputTabs").Visible && editor.Height > 200, "focus editor expands code area");
                        await RunSql("SELECT 7;");
                        Check(Find<TabControl>("outputTabs").Visible, "execution automatically restores results after focus mode");
                        editor.Text = "-- 查询示例：选择数据库后按 F5 执行\nSELECT id, name\nFROM student\nWHERE id > 10\nORDER BY id DESC\nLIMIT 100;";
                        editor.GetType().GetMethod("Highlight")!.Invoke(editor, null);
                        await Task.Delay(220);
                        Capture("sql-code-editor");
                        Find<Button>("focusEditor").PerformClick();
                        await Task.Delay(220);
                        Capture("sql-focus-editor");
                        var warningIssues = new[] { new CompatibilityIssue("旧写法", "建议改为新写法", false) };
                        bool proceedCompatibility = (bool)TestDialog("ShowCompatibility", [form, "8.0.46", warningIssues], dialog =>
                        {
                            Check(((Button)dialog.AcceptButton!).DialogResult == DialogResult.Cancel, "compatibility dialog defaults to editing");
                            Capture("compatibility-warning", dialog);
                            ((Button)dialog.Controls.Find("continueCompatibility", true).Single()).PerformClick();
                        })!;
                        Check(proceedCompatibility, "deprecated syntax dialog allows explicit continuation");
                        TestDialog("ShowCompatibility", [form, "5.7.44", new[] { new CompatibilityIssue("WITH", "需要 MySQL 8.0+", true) }], dialog =>
                        {
                            Check(dialog.Controls.Find("continueCompatibility", true).Length == 0, "unsupported syntax dialog cannot continue");
                            ((Button)dialog.Controls.Find("cancelCompatibility", true).Single()).PerformClick();
                        });
                        // 查询执行中退出也应正常结束会话，不出现对象已释放异常。
                        Find<Button>("proMode").PerformClick();
                        editor.Text = "SELECT SLEEP(5)";
                        Find<Button>("executeSql").PerformClick();
                        await Task.Delay(200);
                        form.FormClosed += (_, _) =>
                        {
                            Console.WriteLine($"All {checks} UI checks passed; close during query completed.");
                            completion.TrySetResult();
                        };
                        form.Close();
                    }
                    catch (Exception error) { completion.TrySetException(error); form.Close(); }
                };
                form.ShowDialog();
            }
            catch (Exception error) { completion.TrySetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}










