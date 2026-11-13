using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace AudioSwap.UI;

/// <summary>Icons from the Windows icon font (Segoe Fluent Icons on 11, MDL2 Assets on 10).</summary>
internal static class Glyphs
{
    public const string Speaker = "";
    public const string Microphone = "";
    public const string Check = "";
    public const string Warning = "";
    public const string Error = "";
    public const string Info = "";
    public const string Sound = "";
    public const string Play = "";

    public static string Family { get; } = PickFamily();

    public static Font CreateFont(float pixels) => new(Family, pixels, GraphicsUnit.Pixel);

    public static void Draw(Graphics g, string glyph, Font font, Color color, RectangleF bounds)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(glyph, font, brush, bounds, format);
    }

    /// <summary>A small glyph bitmap for menus.</summary>
    public static Bitmap Render(string glyph, Color color, int size)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = CreateFont(size * 0.8f);
        Draw(g, glyph, font, color, new RectangleF(0, 0, size, size));
        return bitmap;
    }

    static string PickFamily()
    {
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            using var probe = new Font(name, 10);
            if (probe.Name == name) return name;
        }
        return "Segoe UI Symbol";
    }
}
