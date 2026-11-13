using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace AudioSwap.UI.Controls;

internal static class Draw
{
    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>A filled, optionally outlined, rounded rectangle on pixel centres so 1px edges stay sharp.</summary>
    public static void Rounded(Graphics g, Rectangle bounds, float radius, Color fill, Color? border = null)
    {
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1, bounds.Height - 1);
        using var path = RoundedRect(r, radius);
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        if (border is { } c)
        {
            using var pen = new Pen(c);
            g.DrawPath(pen, path);
        }
        g.SmoothingMode = smoothing;
    }

    /// <summary>Keyboard focus ring, drawn just inside the bounds.</summary>
    public static void FocusRing(Graphics g, Rectangle bounds, float radius)
    {
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(new RectangleF(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2), radius);
        using var pen = new Pen(Theme.Current.Text, 2);
        g.DrawPath(pen, path);
        g.SmoothingMode = smoothing;
    }

    /// <summary>An icon-font glyph, smoothed in greyscale like Windows' own icons (ClearType fringes thin strokes).</summary>
    public static void Glyph(Graphics g, string glyph, float points, Color color, Rectangle bounds)
    {
        using var font = new Font(Glyphs.Family, points);
        var hint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        Glyphs.Draw(g, glyph, font, color, bounds);
        g.TextRenderingHint = hint;
    }
}

/// <summary>Controls that hold colours Windows doesn't inherit (native text boxes, list boxes).</summary>
internal interface IThemeAware
{
    void ApplyTheme();
}
