namespace AudioSwap.UI.Controls;

/// <summary>Windows 11 look for context menus (tray menu, dropdown lists), in the current theme.</summary>
internal static class ThemedMenu
{
    /// <summary>Put on an item's Tag to draw its text in the warning colour.</summary>
    public static readonly object WarningTag = new();

    /// <summary>Themes a menu once; call <see cref="StyleItems"/> again whenever its items are rebuilt.</summary>
    public static void Apply(ToolStripDropDown menu)
    {
        menu.Renderer = new Renderer();
        menu.Font = Theme.CreateFont(9.75f);
        menu.Padding = new Padding(4, 4, 4, 4);
        menu.HandleCreated += (_, _) => Theme.RoundCorners(menu.Handle);
        StyleItems(menu);
    }

    public static void StyleItems(ToolStripDropDown menu)
    {
        foreach (ToolStripItem item in menu.Items)
        {
            if (item is not ToolStripMenuItem menuItem) continue;
            menuItem.Padding = new Padding(4, 5, 4, 5);
            if (menuItem.HasDropDownItems) Apply(menuItem.DropDown);
        }
    }

    sealed class Renderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) =>
            e.Graphics.Clear(Theme.Current.Menu);

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (Theme.IsWindows11) return; // the system draws the rounded border
            using var pen = new Pen(Theme.Current.ControlBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var bounds = new Rectangle(0, 1, e.Item.Width, e.Item.Height - 2);
            Draw.Rounded(e.Graphics, bounds, e.ToolStrip?.LogicalToDeviceUnits(4) ?? 4, Theme.Current.Subtle);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var p = Theme.Current;
            bool isShortcut = e.Item is ToolStripMenuItem item && e.Text == item.ShortcutKeyDisplayString && e.Text != item.Text;
            e.TextColor = !e.Item.Enabled ? p.TextDisabled
                : isShortcut ? p.TextSecondary
                : e.Item.Tag == WarningTag ? p.Warning
                : e.Item.Selected ? p.SubtleText
                : p.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var p = Theme.Current;
            if (e.Item is ToolStripMenuItem { Image: not null })
            {
                // Checked item that also has an icon: mark it with an accent pill on the left.
                int h = e.ImageRectangle.Height;
                using var brush = new SolidBrush(p.Accent);
                e.Graphics.FillRectangle(brush, 1, e.ImageRectangle.Y + h / 4, 3, h / 2);
                return;
            }
            Draw.Glyph(e.Graphics, Glyphs.Check, 9f, p.AccentText, Rectangle.Inflate(e.ImageRectangle, 2, 2));
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var pen = new Pen(Theme.Current.Divider);
            e.Graphics.DrawLine(pen, -4, y, e.Item.Width + 4, y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            var p = Theme.Current;
            Draw.Glyph(e.Graphics, "", 7.5f, e.Item?.Enabled == false ? p.TextDisabled : p.TextSecondary, e.ArrowRectangle);
        }
    }
}
