using System.Runtime.InteropServices;

namespace SulfuricSQL.Forms;

internal static class RichEditUnderline
{
    // CHARFORMAT2W：RichEdit 原生波浪下划线，会随文字滚动、缩放。
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CharacterFormat
    {
        public uint Size, Mask, Effects;
        public int Height, Offset, TextColor;
        public byte Charset, Pitch;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Face;
        public ushort Weight;
        public short Spacing;
        public int BackColor;
        public uint Locale, Reserved;
        public short Style;
        public ushort Kerning;
        public byte Underline, Animation, Author, ReservedByte;
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendFormat(IntPtr handle, int message, IntPtr flags, ref CharacterFormat format);
    public static void Set(IntPtr handle, bool error)
    {
        var format = new CharacterFormat { Size = (uint)Marshal.SizeOf<CharacterFormat>(), Mask = 0x00800004, Effects = error ? 4u : 0, Underline = error ? (byte)8 : (byte)0, Face = "" };
        SendFormat(handle, 0x0444, new IntPtr(1), ref format);
    }
}
