using AudioSwap.Hotkeys;

namespace AudioSwap.Config;

public sealed class AppConfig
{
    public List<Profile> Profiles { get; set; } = [];

    /// <summary>Optional hotkey that steps through the profiles in order.</summary>
    public Hotkey? CycleHotkey { get; set; }

    public PopupMode PopupMode { get; set; } = PopupMode.Always;
    public PopupPosition PopupPosition { get; set; } = PopupPosition.BottomRight;
    public double PopupSeconds { get; set; } = 2.5;

    public bool ShowTrayIcon { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
}

public sealed class Profile
{
    public string Name { get; set; } = "";
    public Hotkey? Hotkey { get; set; }

    /// <summary>Device to switch output to, or null to leave output alone.</summary>
    public DeviceRef? Output { get; set; }

    /// <summary>Device to switch input to, or null to leave input alone.</summary>
    public DeviceRef? Input { get; set; }

    /// <summary>Also make the devices the default for calls (Discord, Teams, Zoom...).</summary>
    public bool IncludeCommunications { get; set; } = true;
}

/// <summary>
/// A saved device. The ID is what Windows uses; the name is kept for display and as a
/// fallback when the ID changes (e.g. a USB headset moved to another port).
/// </summary>
public sealed record DeviceRef(string Id, string Name);

public enum PopupMode { Always, ProblemsOnly, Never }

public enum PopupPosition { BottomRight, BottomCenter, TopRight, TopCenter, Center }
