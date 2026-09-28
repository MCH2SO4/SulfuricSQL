using System.Runtime.InteropServices;

namespace SulfuricSQL.Forms;

// RichEdit 的着色不应占用 Ctrl+Z 的撤销记录。将原生互操作集中在这里。
internal sealed class RichEditUndoScope : IDisposable
{
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr GetOleInterface(IntPtr window, int message, IntPtr wParam, out IntPtr ole);
    private readonly object document;
    private RichEditUndoScope(object document)
    {
        this.document = document;
        ((dynamic)document).Undo(-9999995); // tomSuspend: 暂停记录，不清除已有撤销历史。
    }
    public static RichEditUndoScope Create(IntPtr handle)
    {
        GetOleInterface(handle, 0x043C, IntPtr.Zero, out var ole);
        if (ole == IntPtr.Zero) throw new InvalidOperationException("RichEdit document unavailable.");
        try { return new RichEditUndoScope(Marshal.GetObjectForIUnknown(ole)); }
        finally { Marshal.Release(ole); }
    }
    public void Dispose()
    {
        try { ((dynamic)document).Undo(-9999994); } // tomResume
        finally { Marshal.ReleaseComObject(document); }
    }
}
