namespace AudioSwap.UI.Controls;

/// <summary>A Windows 11 style button: standard, accent (primary) or subtle (icon-only, no chrome).</summary>
internal sealed class FluentButton : Button
{
    public enum Kind { Standard, Accent, Subtle }

    bool _hover, _pressed;

    public FluentButton(string text, string? glyph = null, Kind kind = Kind.Standard, string? tooltip = null)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        Glyph = glyph;
        Style = kind;
        AutoSize = true;
        Font = Theme.CreateFont(9.75f);
        if (tooltip is not null)
        {
            AccessibleName = tooltip;
            SharedTips.SetToolTip(this, tooltip);
        }
    }

    internal static readonly ToolTip SharedTips = new();

    public string? Glyph { get; set; }

    public Kind Style { get; set; }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int height = LogicalToDeviceUnits(32);
        if (string.IsNullOrEmpty(Text)) return new Size(height, height);
        int text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        int glyph = Glyph is null ? 0 : LogicalToDeviceUnits(16 + 8);
        int width = text + glyph + LogicalToDeviceUnits(Glyph is null ? 24 : 22);
        return new Size(Math.Max(width, LogicalToDeviceUnits(Glyph is null ? 84 : 0)), height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        var back = Parent?.BackColor ?? p.Card;
        g.Clear(back);

        Color fill, text;
        Color? border = null;
        if (!Enabled)
        {
            fill = Style == Kind.Subtle ? back : p.ControlDisabled;
            text = p.TextDisabled;
            if (Style == Kind.Standard) border = p.ControlBorder;
        }
        else if (Style == Kind.Accent)
        {
            fill = _pressed ? p.AccentPressed : _hover ? p.AccentHover : p.Accent;
            text = p.OnAccent;
        }
        else if (Style == Kind.Subtle)
        {
            fill = _pressed ? p.Subtle : _hover ? p.SubtleHover : back;
            text = _pressed ? p.TextSecondary : p.Text;
        }
        else
        {
            fill = _pressed ? p.ControlPressed : _hover ? p.ControlHover : p.Control;
            text = _pressed ? p.TextSecondary : p.Text;
            border = p.ControlBorder;
        }

        float radius = LogicalToDeviceUnits(4);
        if (fill != back || border is not null) Draw.Rounded(g, ClientRectangle, radius, fill, border);

        // Glyph and text, centred together.
        int gap = LogicalToDeviceUnits(8), glyphSize = LogicalToDeviceUnits(16);
        bool hasText = !string.IsNullOrEmpty(Text);
        int textWidth = hasText ? TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width : 0;
        int contentWidth = textWidth + (Glyph is null ? 0 : glyphSize + (hasText ? gap : 0));
        int x = (Width - contentWidth) / 2;
        if (Glyph is not null)
        {
            Draw.Glyph(g, Glyph, 10f, text, new Rectangle(x, 0, glyphSize, Height));
            x += glyphSize + gap;
        }
        if (hasText)
        {
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, textWidth + 2, Height), text, fill,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        if (Focused && ShowFocusCues) Draw.FocusRing(g, ClientRectangle, radius + 1);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
}
