namespace AudioSwap.UI.Controls;

/// <summary>A Windows 11 toggle switch with its label to the right. Behaves as a CheckBox.</summary>
internal sealed class ToggleSwitch : CheckBox
{
    const int TrackWidth = 40, TrackHeight = 20, Gap = 12;

    readonly System.Windows.Forms.Timer _animation = new() { Interval = 15 };
    bool _hover;
    float _position; // 0 = off, 1 = on; animated between

    public ToggleSwitch(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        AutoSize = true;
        Font = Theme.CreateFont(9.75f);
        _animation.Tick += (_, _) =>
        {
            float target = Checked ? 1 : 0;
            _position += Math.Sign(target - _position) * 0.2f;
            if (Math.Abs(target - _position) < 0.2f) { _position = target; _animation.Stop(); }
            Invalidate();
        };
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return new Size(LogicalToDeviceUnits(TrackWidth + Gap) + text.Width + 2, Math.Max(LogicalToDeviceUnits(30), text.Height));
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        if (IsHandleCreated && Visible) _animation.Start();
        else _position = Checked ? 1 : 0;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        var back = Parent?.BackColor ?? p.Card;
        g.Clear(back);

        int w = LogicalToDeviceUnits(TrackWidth), h = LogicalToDeviceUnits(TrackHeight);
        var track = new Rectangle(0, (Height - h) / 2, w, h);
        float radius = h / 2f;
        bool on = _position > 0.5f;

        Color knob;
        if (!Enabled)
        {
            if (on) Draw.Rounded(g, track, radius, p.TextDisabled);
            else Draw.Rounded(g, track, radius, back, p.TextDisabled);
            knob = on ? back : p.TextDisabled;
        }
        else if (on)
        {
            Draw.Rounded(g, track, radius, _hover ? p.AccentHover : p.Accent);
            knob = p.OnAccent;
        }
        else
        {
            Draw.Rounded(g, track, radius, _hover ? p.ControlHover : p.Control, p.ControlStrongBorder);
            knob = p.TextSecondary;
        }

        float size = LogicalToDeviceUnits(_hover && Enabled ? 14 : 12);
        float travel = w - h;
        float cx = track.X + h / 2f + travel * _position;
        float cy = track.Y + h / 2f;
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(knob)) g.FillEllipse(brush, cx - size / 2, cy - size / 2, size, size);
        g.SmoothingMode = smoothing;

        if (Focused && ShowFocusCues) Draw.FocusRing(g, Rectangle.Inflate(track, 3, 3), radius + 3);

        var textBounds = new Rectangle(w + LogicalToDeviceUnits(Gap), 0, Width - w - LogicalToDeviceUnits(Gap), Height);
        TextRenderer.DrawText(g, Text, Font, textBounds, Enabled ? p.Text : p.TextDisabled, back,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animation.Dispose();
        base.Dispose(disposing);
    }
}
