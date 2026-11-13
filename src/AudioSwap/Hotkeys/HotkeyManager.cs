using System.Runtime.InteropServices;

namespace AudioSwap.Hotkeys;

/// <summary>Registers system-wide hotkeys on a hidden message-only window.</summary>
internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_NOREPEAT = 0x4000;
    static readonly IntPtr HWND_MESSAGE = new(-3);

    readonly HashSet<int> _registered = [];

    public event Action<int>? Pressed;

    public HotkeyManager() => CreateHandle(new CreateParams { Parent = HWND_MESSAGE });

    /// <returns>False when another app already owns the combination.</returns>
    public bool Register(int id, Hotkey hotkey)
    {
        if (_registered.Contains(id)) Unregister(id);
        if (!RegisterHotKey(Handle, id, (uint)hotkey.Modifiers | MOD_NOREPEAT, (uint)hotkey.Key)) return false;
        _registered.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id)) UnregisterHotKey(Handle, id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered) UnregisterHotKey(Handle, id);
        _registered.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY) Pressed?.Invoke(m.WParam.ToInt32());
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
