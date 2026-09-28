using SulfuricSQL.Services;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace SulfuricSQL.Forms;

// 保留原有 RichTextBox 的编辑行为，只增加着色和本地关键词提示。
internal sealed class SqlCodeEditor : RichTextBox
{
    private static readonly string[] Keywords = "SELECT FROM WHERE INSERT INTO VALUES UPDATE SET DELETE CREATE DATABASE TABLE DROP TRUNCATE ALTER ADD COLUMN PRIMARY KEY AUTO_INCREMENT NOT NULL DEFAULT UNIQUE INDEX JOIN LEFT RIGHT INNER ON GROUP BY ORDER ASC DESC LIMIT OFFSET HAVING AS AND OR IN IS LIKE BETWEEN EXISTS DISTINCT UNION ALL USE SHOW DATABASES TABLES DESCRIBE EXPLAIN CHARSET CHARACTER COLLATE ENGINE INT BIGINT VARCHAR TEXT DATETIME DECIMAL BOOLEAN IF COUNT SUM AVG MIN MAX NOW VERSION".Split(' ');
    private static readonly HashSet<string> Words = new(Keywords, StringComparer.OrdinalIgnoreCase);
    private static readonly Regex Tokens = new(@"--[^\r\n]*|\#[^\r\n]*|/\*[\s\S]*?(?:\*/|$)|'(?:\\.|''|[^'])*(?:'|$)|""(?:\\.|""""|[^""])*(?:""|$)|`(?:``|[^`])*(?:`|$)|\b\d+(?:\.\d+)?\b|\b[A-Za-z_][A-Za-z_0-9]*\b", RegexOptions.Compiled);
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 160 };
    private readonly ToolStripDropDown suggestions = new() { AutoClose = false, Padding = Padding.Empty };
    private readonly ListBox choices = new() { BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(35, 48, 64), ForeColor = Color.FromArgb(220, 231, 242), IntegralHeight = false, Size = new Size(240, 150) };
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendPoint(IntPtr window, int message, IntPtr wParam, ref Point point);
    private bool coloring;
    private int completionStart;
    private string completionPrefix = "";
    private readonly ToolTip errorTip = new();
    public IReadOnlyList<SqlEditorIssue> Issues { get; private set; } = [];
    public SqlCodeEditor()
    {
        BackColor = Color.FromArgb(22, 32, 46);
        ForeColor = Color.FromArgb(219, 229, 241);
        BorderStyle = BorderStyle.None;
        suggestions.Items.Add(new ToolStripControlHost(choices) { Margin = Padding.Empty, Padding = Padding.Empty });
        debounce.Tick += (_, _) => { debounce.Stop(); Highlight(); };
        choices.MouseClick += (_, _) => Complete();
        MouseMove += (_, e) =>
        {
            int index = GetCharIndexFromPosition(e.Location);
            string message = Issues.FirstOrDefault(i => index >= i.Start && index < i.Start + i.Length)?.Message ?? "";
            if (errorTip.GetToolTip(this) != message) errorTip.SetToolTip(this, message);
        };
        LostFocus += (_, _) => { if (!suggestions.ContainsFocus) suggestions.Close(ToolStripDropDownCloseReason.CloseCalled); };
    }
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (coloring) return;
        suggestions.Close(ToolStripDropDownCloseReason.CloseCalled); debounce.Stop(); debounce.Start();
    }
    public void Highlight()
    {
        if (IsDisposed || TextLength > 100000) return;
        using var undo = RichEditUndoScope.Create(Handle);
        int start = SelectionStart, length = SelectionLength;
        Point scroll = default;
        SendPoint(Handle, 0x04DD, IntPtr.Zero, ref scroll);
        SendMessage(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
        coloring = true;
        try
        {
            SelectAll(); SelectionColor = ForeColor; RichEditUnderline.Set(Handle, false);
            foreach (Match token in Tokens.Matches(Text))
            {
                string value = token.Value;
                Color color = value.StartsWith("--") || value.StartsWith('#') || value.StartsWith("/*") ? Color.FromArgb(106, 153, 85)
                    : value[0] is '\'' or '"' ? Color.FromArgb(206, 145, 120)
                    : value[0] == '`' ? Color.FromArgb(78, 201, 176)
                    : char.IsDigit(value[0]) ? Color.FromArgb(181, 206, 168)
                    : Words.Contains(value) ? Color.FromArgb(86, 156, 214) : ForeColor;
                Select(token.Index, token.Length); SelectionColor = color;
            }
            Issues = SqlEditorDiagnostics.Analyze(Text);
            foreach (var issue in Issues)
            {
                Select(issue.Start, issue.Length); SelectionColor = Color.FromArgb(244, 91, 105);
                RichEditUnderline.Set(Handle, true);
            }
            Select(start, length);
        }
        finally
        {
            SendPoint(Handle, 0x04DE, IntPtr.Zero, ref scroll);
            SendMessage(Handle, 0x000B, new IntPtr(1), IntPtr.Zero);
            coloring = false; Invalidate();
        }
    }
    private int WordStart()
    {
        int start = SelectionStart;
        while (start > 0 && (char.IsLetterOrDigit(Text[start - 1]) || Text[start - 1] == '_')) start--;
        return start;
    }
    private void Suggest(bool explicitRequest)
    {
        suggestions.Close(ToolStripDropDownCloseReason.CloseCalled);
        if (ReadOnly || SelectionLength != 0 || TextLength > 100000 || !Visible) return;
        int start = WordStart();
        string prefix = Text[start..SelectionStart];
        if ((!explicitRequest && prefix.Length < 2) || Words.Contains(prefix)) return;
        if (SelectionStart < TextLength && (char.IsLetterOrDigit(Text[SelectionStart]) || Text[SelectionStart] == '_')) return;
        // 注释和字符串中的文字不触发关键词补全。
        if (Tokens.Matches(Text).Cast<Match>().Any(m => m.Index <= start && m.Index + m.Length > start && (m.Value[0] is '\'' or '"' or '`' or '#' || m.Value.StartsWith("--") || m.Value.StartsWith("/*")))) return;
        var matches = Keywords.Distinct().Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !k.Equals(prefix, StringComparison.OrdinalIgnoreCase)).Take(8).ToArray();
        if (matches.Length == 0) return;
        completionStart = start; completionPrefix = prefix;
        choices.Items.Clear(); choices.Items.AddRange(matches); choices.SelectedIndex = 0;
        var point = GetPositionFromCharIndex(SelectionStart); point.Y += Font.Height + 5;
        suggestions.Show(this, point); Focus();
    }
    private void Complete()
    {
        if (ReadOnly || !suggestions.Visible || choices.SelectedItem is not string word) return;
        if (SelectionLength != 0 || WordStart() != completionStart || Text[completionStart..SelectionStart] != completionPrefix)
        { suggestions.Close(ToolStripDropDownCloseReason.CloseCalled); return; }
        int start = WordStart(); Select(start, SelectionStart - start); SelectedText = word;
        suggestions.Close(ToolStripDropDownCloseReason.CloseCalled); Focus();
    }
    protected override void OnSelectionChanged(EventArgs e)
    {
        base.OnSelectionChanged(e);
        if (!coloring) suggestions.Close(ToolStripDropDownCloseReason.CloseCalled);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (!e.Control && !e.Alt && (e.KeyCode is >= Keys.A and <= Keys.Z || e.KeyCode == Keys.Back)) Suggest(false);
    }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) suggestions.Close(ToolStripDropDownCloseReason.CloseCalled); }
    protected override void OnReadOnlyChanged(EventArgs e) { base.OnReadOnlyChanged(e); if (ReadOnly) suggestions.Close(ToolStripDropDownCloseReason.CloseCalled); }
    protected override void OnVScroll(EventArgs e) { suggestions.Close(ToolStripDropDownCloseReason.CloseCalled); base.OnVScroll(e); }
    protected override void OnHScroll(EventArgs e) { suggestions.Close(ToolStripDropDownCloseReason.CloseCalled); base.OnHScroll(e); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Space)) { Suggest(true); return true; }
        if (suggestions.Visible)
        {
            if (keyData == Keys.Escape) { suggestions.Close(ToolStripDropDownCloseReason.CloseCalled); return true; }
            if (keyData is Keys.Tab or Keys.Enter) { Complete(); return true; }
            if (keyData is Keys.Up or Keys.Down)
            {
                choices.SelectedIndex = Math.Clamp(choices.SelectedIndex + (keyData == Keys.Up ? -1 : 1), 0, choices.Items.Count - 1); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { debounce.Dispose(); suggestions.Dispose(); errorTip.Dispose(); }
        base.Dispose(disposing);
    }
}




