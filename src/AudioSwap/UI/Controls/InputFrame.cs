namespace AudioSwap.UI.Controls;

/// <summary>
/// Windows 11 text box chrome around a borderless TextBox: rounded outline, a bottom
/// edge that turns into an accent bar on focus, and an optional ✕ clear button.
/// </summary>
internal sealed class InputFrame : Panel, IThemeAware
{
    readonly FluentButton? _clear;
    bool _hover;

    public InputFrame(TextBox box, bool clearButton = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Box = box;
        box.BorderStyle = BorderStyle.None;
        box.Font = Theme.CreateFont(9.75f);
        Controls.Add(box);
        Height = 32;
        Cursor = Cursors.IBeam;

        if (clearButton)
        {
            _clear = new FluentButton("", Glyphs.Error, FluentButton.Kind.Subtle, "Clear") { AutoSize = false, TabStop = false, Cursor = Cursors.Default };
            _clear.Click += (_, _) => Cleared?.Invoke(this, EventArgs.Empty);
            Controls.Add(_clear);
        }

        box.GotFocus += (_, _) => UpdateState();
        box.LostFocus += (_, _) => UpdateState();
        box.TextChanged += (_, _) => UpdateClearButton();
        foreach (Control c in Controls)
        {
            c.MouseEnter += (_, _) => SetHover(true);
            c.MouseLeave += (_, _) => SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position)));
        }
        ApplyTheme();
        UpdateClearButton();
    }

    public TextBox Box { get; }

    public event EventHandler? Cleared;

    // The clear button sits on the field, so it blends with the field rather than the card.
    public override Color BackColor { get => CurrentFill(Theme.Current); set { } }

    public void ApplyTheme()
    {
        var p = Theme.Current;
        Box.BackColor = CurrentFill(p);
        Box.ForeColor = p.Text;
        Invalidate(invalidateChildren: true);
    }

    Color CurrentFill(Palette p) =>
        !Enabled ? p.ControlDisabled : Box.Focused ? p.InputFocused : _hover ? p.ControlHover : p.Control;

    void UpdateState()
    {
        ApplyTheme();
        UpdateClearButton();
    }

    void SetHover(bool hover)
    {
        if (_hover == hover) return;
        _hover = hover;
        ApplyTheme();
    }

    void UpdateClearButton()
    {
        if (_clear is not null) _clear.Visible = Box.TextLength > 0 && Enabled;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int pad = LogicalToDeviceUnits(10);
        int clearSize = _clear is null ? 0 : Height - LogicalToDeviceUnits(8);
        if (_clear is not null) _clear.Bounds = new Rectangle(Width - clearSize - LogicalToDeviceUnits(4), LogicalToDeviceUnits(4), clearSize, clearSize);
        Box.SetBounds(pad, (Height - Box.Height) / 2 + 1, Width - pad * 2 - clearSize, Box.Height);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Height = Box.PreferredHeight + LogicalToDeviceUnits(14);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = Box.PreferredHeight + LogicalToDeviceUnits(14);
    }

    protected override void OnMouseEnter(EventArgs e) { SetHover(true); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position))); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Box.Focus(); base.OnMouseDown(e); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); UpdateState(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? p.Card);
        float radius = LogicalToDeviceUnits(4);
        Draw.Rounded(g, ClientRectangle, radius, CurrentFill(p), p.ControlBorder);

        // Bottom edge: a hairline normally, a 2px accent bar while typing.
        bool focused = Box.Focused && Enabled;
        int thickness = focused ? LogicalToDeviceUnits(2) : 1;
        var clip = g.Clip;
        using (var path = Draw.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), radius))
            g.SetClip(path);
        using (var brush = new SolidBrush(focused ? p.Accent : Enabled ? p.ControlStrongBorder : p.ControlBorder))
            g.FillRectangle(brush, 0, Height - thickness, Width, thickness);
        g.Clip = clip;
    }
}
