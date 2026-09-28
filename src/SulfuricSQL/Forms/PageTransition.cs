using System.Diagnostics;
using System.Drawing.Imaging;

namespace SulfuricSQL.Forms;

internal sealed class PageTransition : Control
{
    private readonly Bitmap snapshot;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private float fraction;
    private PageTransition(Control parent, Bitmap bitmap)
    {
        snapshot = bitmap; Bounds = parent.ClientRectangle; BackColor = parent.BackColor;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        parent.Controls.Add(this); BringToFront();
        timer.Tick += (_, _) =>
        {
            fraction = (float)Math.Min(1, clock.Elapsed.TotalMilliseconds / 180);
            if (fraction >= 1) Dispose(); else Invalidate();
        };
        timer.Start();
    }
    public static void Play(Control? target)
    {
        if (target == null || !target.Visible || target.Width < 1 || target.Height < 1 || !SystemInformation.IsMenuAnimationEnabled) return;
        foreach (var old in target.Controls.OfType<PageTransition>().ToArray()) old.Dispose();
        Bitmap? image = null;
        try
        {
            image = new Bitmap(target.Width, target.Height);
            target.DrawToBitmap(image, target.ClientRectangle);
            _ = new PageTransition(target, image);
        }
        catch (ArgumentException) { image?.Dispose(); }
        catch (InvalidOperationException) { image?.Dispose(); }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        float eased = 1 - MathF.Pow(1 - fraction, 3);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = eased });
        e.Graphics.DrawImage(snapshot, new Rectangle(0, (int)(10 * (1 - eased)), Width, Height), 0, 0, snapshot.Width, snapshot.Height, GraphicsUnit.Pixel, attributes);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); snapshot.Dispose(); }
        base.Dispose(disposing);
    }
}
