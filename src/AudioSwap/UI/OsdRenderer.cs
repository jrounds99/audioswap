using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using AudioSwap.Audio;
using AudioSwap.UI.Controls;

namespace AudioSwap.UI;

/// <summary>Paints the popup card (with its soft shadow) into a premultiplied-alpha bitmap.</summary>
internal static class OsdRenderer
{
    sealed record Colors(
        Color CardFill, Color CardBorder, Color Divider, Color TextMain, Color TextDim,
        Color OutputAccent, Color InputAccent, Color Unchanged, Color Warning, Color Error, int ShadowStrength);

    static readonly Colors Dark = new(
        CardFill: Color.FromArgb(247, 32, 32, 36), CardBorder: Color.FromArgb(34, 255, 255, 255),
        Divider: Color.FromArgb(22, 255, 255, 255), TextMain: Color.FromArgb(244, 244, 248), TextDim: Color.FromArgb(150, 150, 162),
        OutputAccent: Color.FromArgb(178, 150, 255), InputAccent: Color.FromArgb(56, 214, 240),
        Unchanged: Color.FromArgb(120, 120, 134), Warning: Color.FromArgb(251, 191, 36), Error: Color.FromArgb(248, 113, 113),
        ShadowStrength: 26);

    static readonly Colors Light = new(
        CardFill: Color.FromArgb(250, 251, 251, 252), CardBorder: Color.FromArgb(26, 0, 0, 0),
        Divider: Color.FromArgb(18, 0, 0, 0), TextMain: Color.FromArgb(27, 27, 30), TextDim: Color.FromArgb(98, 98, 108),
        OutputAccent: Color.FromArgb(124, 58, 237), InputAccent: Color.FromArgb(8, 132, 160),
        Unchanged: Color.FromArgb(140, 140, 150), Warning: Color.FromArgb(180, 83, 9), Error: Color.FromArgb(196, 43, 28),
        ShadowStrength: 10);

    static readonly Color BrandStart = Color.FromArgb(139, 92, 246);
    static readonly Color BrandEnd = Color.FromArgb(6, 182, 212);

    // Layout in 96-DPI pixels.
    const float Shadow = 20, Pad = 16, Radius = 14, MarkSize = 20, HeaderHeight = 22;
    const float RowHeight = 44, RowGap = 6, Bubble = 34, MinWidth = 320, MaxWidth = 460;

    public static Bitmap Render(OsdContent content, float scale)
    {
        float S(float v) => v * scale;
        var c = Theme.Current.IsDark ? Dark : Light;

        using var titleFont = new Font(Theme.SemiboldFamily, S(14.5f), GraphicsUnit.Pixel);
        using var nameFont = new Font(Theme.FontFamily, S(14), GraphicsUnit.Pixel);
        using var captionFont = new Font(Theme.SemiboldFamily, S(10.5f), GraphicsUnit.Pixel);
        using var messageFont = new Font(Theme.FontFamily, S(13), GraphicsUnit.Pixel);
        using var glyphFont = Glyphs.CreateFont(S(16));
        using var statusFont = Glyphs.CreateFont(S(13));

        // Size the card to its text, within limits.
        using var measure = Graphics.FromHwnd(IntPtr.Zero);
        float textWidth = measure.MeasureString(content.Title, titleFont).Width + S(MarkSize + 10);
        foreach (var row in content.Rows)
            textWidth = Math.Max(textWidth, measure.MeasureString(row.DeviceName, nameFont).Width + S(Bubble + 12 + 28));
        if (content.Message is not null)
            textWidth = Math.Max(textWidth, Math.Min(measure.MeasureString(content.Message, messageFont).Width, S(MaxWidth - 2 * Pad)));
        float cardW = Math.Clamp(textWidth + S(2 * Pad), S(MinWidth), S(MaxWidth));
        float contentW = cardW - S(2 * Pad);

        float messageH = 0;
        if (content.Message is not null)
            messageH = measure.MeasureString(content.Message, messageFont, (int)contentW).Height;

        float rowsH = content.Rows.Count == 0 ? 0 : content.Rows.Count * S(RowHeight) + (content.Rows.Count - 1) * S(RowGap);
        float cardH = S(Pad) + S(HeaderHeight)
            + (messageH > 0 ? S(8) + messageH : 0)
            + (rowsH > 0 ? S(12) + 1 + S(10) + rowsH : 0)
            + S(Pad);

        int width = (int)Math.Ceiling(cardW + 2 * S(Shadow));
        int height = (int)Math.Ceiling(cardH + 2 * S(Shadow));
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);

        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var card = new RectangleF(S(Shadow), S(Shadow) - S(3), cardW, cardH);
        DrawShadow(g, card, S(Radius), S(Shadow), c.ShadowStrength);

        using var cardPath = Draw.RoundedRect(card, S(Radius));
        using (var fill = new SolidBrush(c.CardFill)) g.FillPath(fill, cardPath);

        // Brand stripe along the top edge.
        g.SetClip(cardPath);
        var stripe = new RectangleF(card.X, card.Y, card.Width, S(3));
        using (var brand = new LinearGradientBrush(stripe, BrandStart, BrandEnd, LinearGradientMode.Horizontal))
            g.FillRectangle(brand, stripe);
        g.ResetClip();

        using (var border = new Pen(c.CardBorder, 1)) g.DrawPath(border, cardPath);

        float x = card.X + S(Pad), y = card.Y + S(Pad);

        // Header: app mark + title (or a notice glyph for messages).
        var markRect = new RectangleF(x, y + (S(HeaderHeight) - S(MarkSize)) / 2, S(MarkSize), S(MarkSize));
        if (content.IsNotice)
        {
            Glyphs.Draw(g, Glyphs.Info, glyphFont, c.InputAccent, markRect);
        }
        else
        {
            using var mark = AppIcon.LoadBitmap((int)Math.Round(S(MarkSize)));
            g.DrawImage(mark, markRect);
        }
        var titleRect = new RectangleF(markRect.Right + S(10), y, contentW - S(MarkSize + 10), S(HeaderHeight));
        DrawText(g, content.Title, titleFont, c.TextMain, titleRect);
        y += S(HeaderHeight);

        if (content.Message is not null)
        {
            y += S(8);
            using var brush = new SolidBrush(c.TextDim);
            g.DrawString(content.Message, messageFont, brush, new RectangleF(x, y, contentW, messageH));
            y += messageH;
        }

        if (content.Rows.Count > 0)
        {
            y += S(12);
            using (var divider = new Pen(c.Divider, 1)) g.DrawLine(divider, x, y, x + contentW, y);
            y += 1 + S(10);

            foreach (var row in content.Rows)
            {
                DrawRow(g, c, row, new RectangleF(x, y, contentW, S(RowHeight)), scale,
                    nameFont, captionFont, glyphFont, statusFont);
                y += S(RowHeight) + S(RowGap);
            }
        }

        return bitmap;
    }

    static void DrawRow(Graphics g, Colors c, OsdRow row, RectangleF bounds, float scale,
        Font nameFont, Font captionFont, Font glyphFont, Font statusFont)
    {
        float S(float v) => v * scale;

        var accent = row.Flow == DeviceFlow.Output ? c.OutputAccent : c.InputAccent;
        var (tone, caption, status) = row.State switch
        {
            OsdRowState.Switched => (accent, "", Glyphs.Check),
            OsdRowState.Unchanged => (c.Unchanged, " · NO CHANGE", null),
            OsdRowState.Missing => (c.Warning, " · NOT DETECTED", Glyphs.Warning),
            _ => (c.Error, " · COULDN'T SWITCH", Glyphs.Error),
        };
        caption = (row.Flow == DeviceFlow.Output ? "OUTPUT" : "INPUT") + caption;

        var bubble = new RectangleF(bounds.X, bounds.Y + (bounds.Height - S(Bubble)) / 2, S(Bubble), S(Bubble));
        using (var fill = new SolidBrush(Color.FromArgb(row.State == OsdRowState.Unchanged ? 26 : 40, tone)))
            g.FillEllipse(fill, bubble);
        Glyphs.Draw(g, row.Flow == DeviceFlow.Output ? Glyphs.Speaker : Glyphs.Microphone, glyphFont, tone, bubble);

        float textX = bubble.Right + S(12);
        float statusW = status is null ? 0 : S(24);
        float textW = bounds.Right - textX - statusW;
        float captionH = S(15), nameH = S(20);
        float textY = bounds.Y + (bounds.Height - captionH - nameH) / 2;

        DrawText(g, caption, captionFont, tone, new RectangleF(textX, textY, textW, captionH));
        DrawText(g, row.DeviceName, nameFont, row.State == OsdRowState.Switched ? c.TextMain : c.TextDim,
            new RectangleF(textX, textY + captionH, textW, nameH));

        if (status is not null)
            Glyphs.Draw(g, status, statusFont, tone, new RectangleF(bounds.Right - statusW, bounds.Y, statusW, bounds.Height));
    }

    static void DrawText(Graphics g, string text, Font font, Color color, RectangleF bounds)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(text, font, brush, bounds, format);
    }

    /// <summary>Layers of faint rounded rects build up a soft, slightly lowered shadow.</summary>
    static void DrawShadow(Graphics g, RectangleF card, float radius, float spread, int strength)
    {
        int steps = (int)Math.Max(6, spread);
        for (int i = steps; i >= 1; i--)
        {
            float grow = spread * i / steps;
            float t = 1 - (float)i / steps;
            int alpha = (int)(strength / 6f + strength * t * t);
            var r = RectangleF.Inflate(card, grow * 0.8f, grow * 0.8f);
            r.Offset(0, spread * 0.25f);
            using var path = Draw.RoundedRect(r, radius + grow * 0.8f);
            using var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
            g.FillPath(brush, path);
        }
    }
}
