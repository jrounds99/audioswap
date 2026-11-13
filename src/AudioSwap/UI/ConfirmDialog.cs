using AudioSwap.UI.Controls;

namespace AudioSwap.UI;

/// <summary>A small themed yes/no prompt (the stock MessageBox ignores dark mode).</summary>
internal sealed class ConfirmDialog : ThemedForm
{
    ConfirmDialog(string title, string message, string confirmText)
    {
        Text = "AudioSwap";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(24, 20, 24, 20);

        var confirm = new FluentButton(confirmText, kind: FluentButton.Kind.Accent) { DialogResult = DialogResult.OK };
        var cancel = new FluentButton("Cancel") { DialogResult = DialogResult.Cancel, Margin = new Padding(8, 0, 0, 0) };
        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 20, 0, 0) };
        buttons.Controls.AddRange([confirm, cancel]);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(new ThemedLabel(title, ThemedLabel.Style.Heading) { Margin = new Padding(0, 0, 0, 8) });
        layout.Controls.Add(new ThemedLabel(message) { MaximumSize = new Size(380, 0) });
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        AcceptButton = confirm;
        CancelButton = cancel;
    }

    public static bool Ask(IWin32Window owner, string title, string message, string confirmText)
    {
        using var dialog = new ConfirmDialog(title, message, confirmText);
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }
}
