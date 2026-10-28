using System.Windows.Media;
using KanPan.Infrastructure.Storage;
using DrawingColor = System.Drawing.Color;

namespace KanPan.App.Services;

/// <summary>Up/down colors. Chinese markets use red for up by default.</summary>
public sealed class Palette
{
    private static readonly Color Red = Color.FromRgb(0xE5, 0x39, 0x35);
    private static readonly Color Green = Color.FromRgb(0x1B, 0x9E, 0x4B);
    private static readonly Color Grey = Color.FromRgb(0x80, 0x86, 0x8B);

    private readonly Color _up;
    private readonly Color _down;

    public Palette(ColorScheme scheme)
    {
        (_up, _down) = scheme == ColorScheme.GreenUp ? (Green, Red) : (Red, Green);
        UpBrush = Freeze(new SolidColorBrush(_up));
        DownBrush = Freeze(new SolidColorBrush(_down));
        NeutralBrush = Freeze(new SolidColorBrush(Colors.Black));
        UpRowBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x24, _up.R, _up.G, _up.B)));
        DownRowBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x24, _down.R, _down.G, _down.B)));
    }

    public Brush UpBrush { get; }
    public Brush DownBrush { get; }
    public Brush NeutralBrush { get; }
    public Brush UpRowBrush { get; }
    public Brush DownRowBrush { get; }

    public Brush ForValue(decimal value) => value > 0 ? UpBrush : value < 0 ? DownBrush : NeutralBrush;

    public DrawingColor TrayColor(decimal value, bool inactive)
    {
        var c = inactive || value == 0 ? Grey : value > 0 ? _up : _down;
        return DrawingColor.FromArgb(c.R, c.G, c.B);
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}
