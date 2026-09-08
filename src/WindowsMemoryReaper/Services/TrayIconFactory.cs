using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WindowsMemoryReaper.Services;

/// <summary>
/// Generates a simple tray icon programmatically. No external asset required.
/// </summary>
public static class TrayIconFactory
{
    private const int Size = 32;

    /// <summary>Creates an icon in the given color with an "R" glyph.</summary>
    public static Icon Create(Color color)
    {
        using var bitmap = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, 1, 1, Size - 3, Size - 3);

            using var font = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            var rect = new RectangleF(0, 0, Size, Size);
            graphics.DrawString("R", font, textBrush, rect, format);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    public static readonly Color NormalColor = Color.FromArgb(0x2D, 0x7D, 0x32);   // green
    public static readonly Color WarningColor = Color.FromArgb(0xD9, 0x9A, 0x2B);  // amber
    public static readonly Color CleaningColor = Color.FromArgb(0x1F, 0x6F, 0xB2); // blue
    public static readonly Color ErrorColor = Color.FromArgb(0xC0, 0x39, 0x2B);    // red
}
