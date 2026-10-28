using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KanPan.App.Services;

/// <summary>
/// Draws a rounded badge with short text. Each icon owns a GDI handle that must be destroyed,
/// so callers dispose the returned <see cref="RenderedIcon"/> once it is no longer shown.
/// </summary>
public static class TrayIconRenderer
{
    public static RenderedIcon Render(string text, Color background, Color? foreground = null, int? size = null)
    {
        var px = size ?? SystemInformation.SmallIconSize.Width;
        using var bitmap = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            using (var path = RoundedRect(new RectangleF(0, 0, px, px), px * 0.2f))
            using (var bg = new SolidBrush(background))
                g.FillPath(bg, path);

            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
            };
            var font = FitFont(g, text, px, format);
            using (font)
            using (var fg = new SolidBrush(foreground ?? Color.White))
                g.DrawString(text, font, fg, new RectangleF(-1, 0, px + 2, px), format);
        }

        var handle = bitmap.GetHicon();
        return new RenderedIcon(Icon.FromHandle(handle), handle);
    }

    private static Font FitFont(Graphics g, string text, int px, StringFormat format)
    {
        for (var emPx = px * 0.78f; ; emPx -= 0.5f)
        {
            var font = new Font("Segoe UI", emPx, FontStyle.Bold, GraphicsUnit.Pixel);
            if (emPx <= 6 || g.MeasureString(text, font, int.MaxValue, format).Width <= px + 1)
                return font;
            font.Dispose();
        }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

public sealed class RenderedIcon(Icon icon, IntPtr handle) : IDisposable
{
    public Icon Icon { get; } = icon;

    public void Dispose()
    {
        Icon.Dispose();
        DestroyIcon(handle);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
