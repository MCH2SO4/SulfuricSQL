using System.Drawing.Drawing2D;

namespace SulfuricSQL.Forms;

internal static class AppTheme
{
    public static readonly Color Canvas = Color.FromArgb(240, 245, 249);
    public static readonly Color Ink = Color.FromArgb(29, 47, 64);
    public static readonly Color Teal = Color.FromArgb(20, 112, 117);
    public static void Apply(Control root)
    {
        if (root is Form form) form.BackColor = Canvas;
        if (root is TabPage page) { page.BackColor = Color.White; page.UseVisualStyleBackColor = false; }
        if (root is TabControl tabs)
        {
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.Padding = new Point(18, 8);
            tabs.DrawItem += (_, e) =>
            {
                // 插入/移除页签时，WinForms 可能先发送旧索引的 DrawItem。
                // 此时集合已经变短，直接读取 TabPages[e.Index] 会抛出越界异常。
                if (e.Index < 0 || e.Index >= tabs.TabPages.Count) return;
                bool selected = tabs.SelectedIndex == e.Index;
                using var background = new SolidBrush(selected ? Color.White : Canvas);
                e.Graphics.FillRectangle(background, e.Bounds);
                TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds,
                    selected ? Teal : Color.FromArgb(99, 117, 135), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (selected) { using var marker = new SolidBrush(Teal); e.Graphics.FillRectangle(marker, e.Bounds.Left + 12, e.Bounds.Bottom - 3, e.Bounds.Width - 24, 3); }
            };
        }
        if (root is ComboBox picker) picker.FlatStyle = FlatStyle.Flat;
        if (root is DataGridView grid)
        {
            grid.GridColor = Color.FromArgb(228, 236, 242);
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 242, 243);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(5);
            grid.DefaultCellStyle.SelectionBackColor = Teal;
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.RowTemplate.Height = 30;
        }
        foreach (Control child in root.Controls) Apply(child);
    }
    public static GraphicsPath Rounded(RectangleF rectangle, float radius)
    {
        float size = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, size, size, 180, 90);
        path.AddArc(rectangle.Right - size, rectangle.Top, size, size, 270, 90);
        path.AddArc(rectangle.Right - size, rectangle.Bottom - size, size, size, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - size, size, size, 90, 90);
        path.CloseFigure(); return path;
    }
}
