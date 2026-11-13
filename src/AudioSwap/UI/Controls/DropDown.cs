namespace AudioSwap.UI.Controls;

internal sealed record DropDownItem(object? Value, string Text, bool IsWarning = false);

/// <summary>A Windows 11 style combo box whose list opens as a themed menu.</summary>
internal sealed class DropDown : Control
{
    readonly List<DropDownItem> _items = [];
    int _selected = -1;
    bool _hover, _open;

    public DropDown()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Height = 32;
        Font = Theme.CreateFont(9.75f);
        AccessibleRole = AccessibleRole.ComboBox;
    }

    public event EventHandler? SelectedIndexChanged;

    public IReadOnlyList<DropDownItem> Items => _items;

    public DropDownItem? SelectedItem => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            value = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
            if (value == _selected) return;
            _selected = value;
            AccessibleDescription = SelectedItem?.Text;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Replaces the items and selection without raising <see cref="SelectedIndexChanged"/>.</summary>
    public void SetItems(IEnumerable<DropDownItem> items, int selected)
    {
        _items.Clear();
        _items.AddRange(items);
        _selected = _items.Count == 0 ? -1 : Math.Clamp(selected, 0, _items.Count - 1);
        AccessibleDescription = SelectedItem?.Text;
        Invalidate();
    }

    void Open()
    {
        if (_items.Count == 0 || _open) return;
        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
        for (int i = 0; i < _items.Count; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(_items[i].Text) { Checked = i == _selected };
            if (_items[i].IsWarning) item.Tag = ThemedMenu.WarningTag;
            item.Click += (_, _) => SelectedIndex = index;
            menu.Items.Add(item);
        }
        ThemedMenu.Apply(menu);
        menu.MinimumSize = new Size(Width, 0);
        menu.Closed += (_, _) =>
        {
            _open = false;
            Invalidate();
            BeginInvoke(menu.Dispose);
        };
        _open = true;
        Invalidate();
        menu.Show(this, new Point(0, Height + LogicalToDeviceUnits(2)));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        Open();
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Space or Keys.Enter or Keys.F4:
            case Keys.Down when e.Alt:
                Open();
                e.Handled = true;
                break;
            case Keys.Down:
                SelectedIndex = _selected + 1;
                e.Handled = true;
                break;
            case Keys.Up:
                SelectedIndex = Math.Max(0, _selected - 1);
                e.Handled = true;
                break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? p.Card);
        float radius = LogicalToDeviceUnits(4);
        var fill = !Enabled ? p.ControlDisabled : _open ? p.ControlPressed : _hover ? p.ControlHover : p.Control;
        Draw.Rounded(g, ClientRectangle, radius, fill, p.ControlBorder);

        int pad = LogicalToDeviceUnits(11), chevron = LogicalToDeviceUnits(16);
        var item = SelectedItem;
        var color = !Enabled ? p.TextDisabled : item?.IsWarning == true ? p.Warning : p.Text;
        TextRenderer.DrawText(g, item?.Text ?? "", Font, new Rectangle(pad, 0, Width - pad * 2 - chevron, Height), color, fill,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        Draw.Glyph(g, "", 7.5f, Enabled ? p.TextSecondary : p.TextDisabled,
            new Rectangle(Width - pad - chevron, 0, chevron, Height));

        if (Focused && ShowFocusCues) Draw.FocusRing(g, ClientRectangle, radius + 1);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
}
