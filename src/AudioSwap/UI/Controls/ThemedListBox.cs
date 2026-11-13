namespace AudioSwap.UI.Controls;

/// <summary>An owner-drawn list on a card, with hover tracking and themed scroll bars.</summary>
internal sealed class ThemedListBox : ListBox, IThemeAware
{
    public ThemedListBox()
    {
        DrawMode = DrawMode.OwnerDrawVariable;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        ApplyTheme();
    }

    public int HoverIndex { get; private set; } = -1;

    public void ApplyTheme()
    {
        BackColor = Theme.Current.Card;
        ForeColor = Theme.Current.Text;
        Theme.ApplyControlTheme(this);
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyControlTheme(this);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = IndexFromPoint(e.Location);
        if (index >= Items.Count) index = -1;
        SetHover(index);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHover(-1);
    }

    void SetHover(int index)
    {
        if (index == HoverIndex) return;
        int old = HoverIndex;
        HoverIndex = index;
        InvalidateItem(old);
        InvalidateItem(index);
    }

    void InvalidateItem(int index)
    {
        if (index >= 0 && index < Items.Count) Invalidate(GetItemRectangle(index));
    }
}
