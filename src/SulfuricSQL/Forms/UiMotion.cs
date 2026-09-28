using System.Diagnostics;

namespace SulfuricSQL.Forms;

internal static class UiMotion
{
    // 使用真实经过时间缓动，避免界面忙碌时动画被拖长；尊重系统关闭动画的设置。
    public static void Attach(Form form)
    {
        if (!SystemInformation.IsMenuAnimationEnabled) return;
        var timer = new System.Windows.Forms.Timer { Interval = 15 };
        var clock = new Stopwatch();
        double target = 1;
        form.Shown += (_, _) =>
        {
            target = form.Opacity;
            if (target <= 0) return;
            form.Opacity = 0; clock.Restart(); timer.Start();
        };
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / 180);
            form.Opacity = target * (1 - Math.Pow(1 - t, 3));
            if (t >= 1) timer.Stop();
        };
        form.Disposed += (_, _) => timer.Dispose();
    }
}

internal sealed class MotionButton : Button
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    private readonly Stopwatch clock = new();
    private double level, from, target;
    public MotionButton()
    {
        DoubleBuffered = true;
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / 140);
            level = from + (target - from) * (1 - Math.Pow(1 - t, 3));
            Invalidate(); if (t >= 1) timer.Stop();
        };
    }
    private void Animate(double value)
    {
        from = level; target = value;
        if (!SystemInformation.IsMenuAnimationEnabled) { level = value; Invalidate(); return; }
        clock.Restart(); timer.Start();
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Animate(1); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Animate(0); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Animate(1.8); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Animate(ClientRectangle.Contains(e.Location) ? 1 : 0); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Color.White);
        using var shape = AppTheme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 8);
        using var fill = new SolidBrush(Enabled ? BackColor : Color.FromArgb(236, 241, 244));
        using var edge = new Pen(BackColor == Color.White ? Color.FromArgb(213, 226, 233) : BackColor);
        e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(edge, shape);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(144, 157, 168), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
        using var brush = new SolidBrush(Color.FromArgb((int)(level * 28), 37, 202, 186));
        e.Graphics.FillPath(brush, shape);
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}

