using System.ComponentModel;
using System.Runtime.InteropServices;
using AudioSwap.Hotkeys;

namespace AudioSwap.UI;

/// <summary>A text box that records a key combination instead of text.</summary>
internal sealed class HotkeyBox : TextBox
{
    const string Placeholder = "Click, then press keys";
    Hotkey? _hotkey;

    public event EventHandler? HotkeyChanged;

    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        ShortcutsEnabled = false;
        PlaceholderText = Placeholder;
        Cursor = Cursors.Hand;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Hotkey? Hotkey
    {
        get => _hotkey;
        set
        {
            if (_hotkey == value) { ShowCurrent(); return; }
            _hotkey = value;
            ShowCurrent();
            HotkeyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Only Tab / Shift+Tab are left for focus navigation; every other key is captured.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
        if (msg.Msg is not (WM_KEYDOWN or WM_SYSKEYDOWN)) return base.ProcessCmdKey(ref msg, keyData);

        var key = keyData & Keys.KeyCode;
        var modifiers = CurrentModifiers(keyData);

        if (key == Keys.Tab && modifiers is HotkeyModifiers.None or HotkeyModifiers.Shift)
            return base.ProcessCmdKey(ref msg, keyData);

        if (modifiers == HotkeyModifiers.None && key is Keys.Back or Keys.Delete or Keys.Escape)
        {
            Hotkey = null;
            return true;
        }

        if (Hotkey.IsModifierKey(key))
        {
            Text = Hotkey.FormatModifiers(modifiers) + " + …";
            return true;
        }

        var candidate = new Hotkey(modifiers, key);
        if (candidate.IsValid) Hotkey = candidate;
        else Text = "Add Ctrl, Alt, Shift or Win";
        return true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        // Released the modifiers without finishing a combo: put the real value back.
        if (CurrentModifiers(Control.ModifierKeys) == HotkeyModifiers.None) ShowCurrent();
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        ShowCurrent();
    }

    void ShowCurrent() => Text = _hotkey?.ToDisplayString() ?? "";

    static HotkeyModifiers CurrentModifiers(Keys keyData)
    {
        var modifiers = HotkeyModifiers.None;
        if (keyData.HasFlag(Keys.Control)) modifiers |= HotkeyModifiers.Ctrl;
        if (keyData.HasFlag(Keys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (keyData.HasFlag(Keys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (IsDown(VK_LWIN) || IsDown(VK_RWIN)) modifiers |= HotkeyModifiers.Win;
        return modifiers;
    }

    const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    static bool IsDown(int vk) => (GetKeyState(vk) & 0x8000) != 0;

    [DllImport("user32.dll")]
    static extern short GetKeyState(int vk);
}
