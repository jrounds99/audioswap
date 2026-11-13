using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AudioSwap.UI;

/// <summary>Colours for one Windows appearance (light, dark or high contrast) with the user's accent.</summary>
internal sealed record Palette
{
    public required bool IsDark { get; init; }
    public required bool IsHighContrast { get; init; }
    public required Color Window { get; init; }
    public required Color Card { get; init; }
    public required Color CardBorder { get; init; }
    public required Color Control { get; init; }
    public required Color ControlHover { get; init; }
    public required Color ControlPressed { get; init; }
    public required Color ControlDisabled { get; init; }
    public required Color ControlBorder { get; init; }
    /// <summary>Text box bottom edge and the toggle outline.</summary>
    public required Color ControlStrongBorder { get; init; }
    public required Color InputFocused { get; init; }
    public required Color Text { get; init; }
    public required Color TextSecondary { get; init; }
    public required Color TextDisabled { get; init; }
    public required Color Accent { get; init; }
    public required Color AccentHover { get; init; }
    public required Color AccentPressed { get; init; }
    public required Color OnAccent { get; init; }
    public required Color AccentText { get; init; }
    /// <summary>Selected list row / menu hover background, and the text on it.</summary>
    public required Color Subtle { get; init; }
    public required Color SubtleHover { get; init; }
    public required Color SubtleText { get; init; }
    public required Color Divider { get; init; }
    public required Color Menu { get; init; }
    public required Color Warning { get; init; }
    public required Color Danger { get; init; }
}

/// <summary>
/// Follows the Windows app theme (light/dark), accent colour and high-contrast mode,
/// and raises <see cref="Changed"/> when the user changes any of them.
/// </summary>
internal static class Theme
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    const string DwmKey = @"Software\Microsoft\Windows\DWM";

    static Palette? _current;
    static bool? _forcedDark;
    static bool _watching;

    public static Palette Current => _current ??= Build();

    public static event EventHandler? Changed;

    public static bool IsWindows11 { get; } = Environment.OSVersion.Version.Build >= 22000;

    public static string FontFamily { get; } = Installed("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";

    public static string SemiboldFamily { get; } =
        Installed("Segoe UI Variable Text Semibold") ? "Segoe UI Variable Text Semibold" : "Segoe UI Semibold";

    public static Font CreateFont(float points, bool semibold = false) => new(semibold ? SemiboldFamily : FontFamily, points);

    public static void Watch()
    {
        if (_watching) return;
        _watching = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static void Unwatch()
    {
        if (!_watching) return;
        _watching = false;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    /// <summary>For screenshots: pin light or dark regardless of the Windows setting.</summary>
    public static void Force(bool dark)
    {
        _forcedDark = dark;
        _current = Build();
    }

    static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        var next = Build();
        if (next == _current) return;
        _current = next;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    static Palette Build()
    {
        if (SystemInformation.HighContrast) return HighContrast();
        var accent = ReadAccent();
        bool dark = _forcedDark ?? ReadDword(PersonalizeKey, "AppsUseLightTheme") == 0;
        return dark ? Dark(accent) : Light(accent);
    }

    // Values follow the Windows 11 Fluent tokens, flattened onto their usual backgrounds.
    static Palette Light(AccentShades a) => new()
    {
        IsDark = false,
        IsHighContrast = false,
        Window = Hex(0xF3F3F3),
        Card = Hex(0xFBFBFB),
        CardBorder = Hex(0xE5E5E5),
        Control = Hex(0xFEFEFE),
        ControlHover = Hex(0xF6F6F6),
        ControlPressed = Hex(0xF1F1F1),
        ControlDisabled = Hex(0xF5F5F5),
        ControlBorder = Hex(0xE0E0E0),
        ControlStrongBorder = Hex(0x8D8D8D),
        InputFocused = Hex(0xFFFFFF),
        Text = Hex(0x1B1B1B),
        TextSecondary = Hex(0x616161),
        TextDisabled = Hex(0xA0A0A0),
        Accent = a.Dark1,
        AccentHover = Blend(a.Dark1, Hex(0xFBFBFB), 0.1),
        AccentPressed = Blend(a.Dark1, Hex(0xFBFBFB), 0.2),
        OnAccent = Color.White,
        AccentText = a.Dark2,
        Subtle = Hex(0xEAEAEA),
        SubtleHover = Hex(0xF2F2F2),
        SubtleText = Hex(0x1B1B1B),
        Divider = Hex(0xE5E5E5),
        Menu = Hex(0xF9F9F9),
        Warning = Hex(0x9D5D00),
        Danger = Hex(0xC42B1C),
    };

    static Palette Dark(AccentShades a) => new()
    {
        IsDark = true,
        IsHighContrast = false,
        Window = Hex(0x202020),
        Card = Hex(0x2B2B2B),
        CardBorder = Hex(0x1D1D1D),
        Control = Hex(0x373737),
        ControlHover = Hex(0x3D3D3D),
        ControlPressed = Hex(0x323232),
        ControlDisabled = Hex(0x303030),
        ControlBorder = Hex(0x434343),
        ControlStrongBorder = Hex(0x9E9E9E),
        InputFocused = Hex(0x1F1F1F),
        Text = Hex(0xFFFFFF),
        TextSecondary = Hex(0xADADAD),
        TextDisabled = Hex(0x787878),
        Accent = a.Light2,
        AccentHover = Blend(a.Light2, Hex(0x2B2B2B), 0.1),
        AccentPressed = Blend(a.Light2, Hex(0x2B2B2B), 0.2),
        OnAccent = Color.Black,
        AccentText = a.Light3,
        Subtle = Hex(0x383838),
        SubtleHover = Hex(0x323232),
        SubtleText = Hex(0xFFFFFF),
        Divider = Hex(0x3A3A3A),
        Menu = Hex(0x2C2C2C),
        Warning = Hex(0xFFC83D),
        Danger = Hex(0xFF99A4),
    };

    static Palette HighContrast() => new()
    {
        IsDark = SystemColors.Window.GetBrightness() < 0.5f,
        IsHighContrast = true,
        Window = SystemColors.Window,
        Card = SystemColors.Window,
        CardBorder = SystemColors.WindowText,
        Control = SystemColors.ButtonFace,
        ControlHover = SystemColors.ButtonFace,
        ControlPressed = SystemColors.ButtonFace,
        ControlDisabled = SystemColors.ButtonFace,
        ControlBorder = SystemColors.ButtonShadow,
        ControlStrongBorder = SystemColors.WindowText,
        InputFocused = SystemColors.Window,
        Text = SystemColors.WindowText,
        TextSecondary = SystemColors.WindowText,
        TextDisabled = SystemColors.GrayText,
        Accent = SystemColors.Highlight,
        AccentHover = SystemColors.Highlight,
        AccentPressed = SystemColors.Highlight,
        OnAccent = SystemColors.HighlightText,
        AccentText = SystemColors.HotTrack,
        Subtle = SystemColors.Highlight,
        SubtleHover = SystemColors.Window,
        SubtleText = SystemColors.HighlightText,
        Divider = SystemColors.WindowText,
        Menu = SystemColors.Menu,
        Warning = SystemColors.WindowText,
        Danger = SystemColors.WindowText,
    };

    sealed record AccentShades(Color Light3, Color Light2, Color Light1, Color Base, Color Dark1, Color Dark2);

    /// <summary>
    /// Windows stores the accent and its derived shades as eight RGBA entries
    /// (light3, light2, light1, base, dark1, dark2, dark3, complement).
    /// </summary>
    static AccentShades ReadAccent()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(AccentKey))
        {
            if (key?.GetValue("AccentPalette") is byte[] { Length: >= 24 } bytes)
            {
                Color At(int i) => Color.FromArgb(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2]);
                return new AccentShades(At(0), At(1), At(2), At(3), At(4), At(5));
            }
        }

        // Older builds: only the base accent (ABGR); derive the shades from it.
        var abgr = ReadDword(DwmKey, "AccentColor");
        var accent = abgr is int v ? Color.FromArgb(v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF) : Hex(0x0078D4);
        return new AccentShades(
            Blend(Color.White, accent, 0.6), Blend(Color.White, accent, 0.4), Blend(Color.White, accent, 0.2),
            accent, Blend(Color.Black, accent, 0.15), Blend(Color.Black, accent, 0.35));
    }

    #region Window frame

    /// <summary>Dark or light title bar to match, blended into the window on Windows 11.</summary>
    public static void ApplyFrame(Form form)
    {
        if (!form.IsHandleCreated) return;
        var p = Current;
        var handle = form.Handle;

        int dark = p.IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));

        if (IsWindows11)
        {
            // Respect "Show accent colour on title bars": only blend when it's off.
            bool accentTitleBars = ReadDword(DwmKey, "ColorPrevalence") == 1 || p.IsHighContrast;
            int caption = accentTitleBars ? DWMWA_COLOR_DEFAULT : ColorTranslator.ToWin32(p.Window);
            int text = accentTitleBars ? DWMWA_COLOR_DEFAULT : ColorTranslator.ToWin32(p.Text);
            DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            DwmSetWindowAttribute(handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));
        }
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    /// <summary>Windows 11 rounded corners for popups such as menus (ignored elsewhere).</summary>
    public static void RoundCorners(IntPtr handle, bool small = false)
    {
        int preference = small ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND;
        DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    /// <summary>Dark scroll bars and such for native controls.</summary>
    public static void ApplyControlTheme(Control control)
    {
        if (control.IsHandleCreated)
            SetWindowTheme(control.Handle, Current.IsDark && !Current.IsHighContrast ? "DarkMode_Explorer" : "Explorer", null);
    }

    #endregion

    #region Helpers

    public static Color Blend(Color top, Color bottom, double topOpacity)
    {
        int Mix(int a, int b) => (int)Math.Round(a * topOpacity + b * (1 - topOpacity));
        return Color.FromArgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    static Color Hex(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    static int? ReadDword(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(name) is int value ? value : null;
    }

    static bool Installed(string family)
    {
        using var probe = new Font(family, 10);
        return probe.Name == family;
    }

    const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19, DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;
    const int DWMWCP_ROUND = 2, DWMWCP_ROUNDSMALL = 3;
    const int DWMWA_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    #endregion
}
