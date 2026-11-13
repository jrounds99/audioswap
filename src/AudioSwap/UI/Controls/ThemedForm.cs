namespace AudioSwap.UI.Controls;

/// <summary>
/// A window that follows the Windows theme: matching title bar, background and text,
/// re-applied live when the user switches light/dark or changes the accent.
/// </summary>
internal class ThemedForm : Form
{
    public ThemedForm()
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = Theme.CreateFont(9.75f);
        Theme.Changed += OnThemeChanged;
    }

    // Children inherit these, so one theme switch recolours every label and panel.
    public override Color BackColor { get => Theme.Current.Window; set { } }
    public override Color ForeColor { get => Theme.Current.Text; set { } }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyFrame(this);
        ApplyToChildren(this);
    }

    void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        Theme.ApplyFrame(this);
        ApplyToChildren(this);
        Invalidate(invalidateChildren: true);
    }

    static void ApplyToChildren(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is IThemeAware aware) aware.ApplyTheme();
            ApplyToChildren(child);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.Changed -= OnThemeChanged;
        base.Dispose(disposing);
    }
}

/// <summary>A rounded surface that groups related settings.</summary>
internal sealed class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Padding = new Padding(16, 14, 16, 14);
        Dock = DockStyle.Fill;
    }

    public override Color BackColor { get => Theme.Current.Card; set { } }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var p = Theme.Current;
        e.Graphics.Clear(Parent?.BackColor ?? p.Window);
        Draw.Rounded(e.Graphics, ClientRectangle, LogicalToDeviceUnits(8), p.Card, p.CardBorder);
    }
}

/// <summary>Text in one of the app's type styles, drawn in theme colours (no grey "etched" look when disabled).</summary>
internal sealed class ThemedLabel : Label
{
    public enum Style { Body, Secondary, Heading, Title, Caption }

    readonly Style _style;

    public ThemedLabel(string text, Style style = Style.Body)
    {
        _style = style;
        Text = text;
        AutoSize = true;
        UseMnemonic = false;
        Font = style switch
        {
            Style.Heading => Theme.CreateFont(10.5f, semibold: true),
            Style.Title => Theme.CreateFont(16f, semibold: true),
            Style.Caption => Theme.CreateFont(8.25f),
            _ => Theme.CreateFont(9.75f),
        };
    }

    public bool IsWarning { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var color = !Enabled ? p.TextDisabled
            : IsWarning ? p.Danger
            : _style is Style.Secondary or Style.Caption ? p.TextSecondary
            : p.Text;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, color, BackColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
    }
}

/// <summary>A link in the accent colour.</summary>
internal sealed class ThemedLink : LinkLabel, IThemeAware
{
    public ThemedLink(string text, Action click)
    {
        Text = text;
        AutoSize = true;
        LinkBehavior = LinkBehavior.HoverUnderline;
        Font = Theme.CreateFont(9.75f);
        LinkClicked += (_, _) => click();
        ApplyTheme();
    }

    public void ApplyTheme() => LinkColor = ActiveLinkColor = VisitedLinkColor = Theme.Current.AccentText;
}
