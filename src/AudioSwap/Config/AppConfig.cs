using AudioSwap.Audio;
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

    /// <summary>Where the popup appears: null follows the active window, "primary" is the main display, anything else is a display ID.</summary>
    public string? PopupDisplay { get; set; }

    /// <summary>Sounds tied to output devices. Each plays whenever its device is picked, even if it's already in use.</summary>
    public List<DeviceSound> DeviceSounds { get; set; } = [];

    public bool ShowTrayIcon { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>The sound saved for an output device, matched the way profiles match devices: by ID, then by a unique name.</summary>
    public DeviceSound? SoundFor(DeviceRef? device, IReadOnlyList<AudioDevice> connected)
    {
        if (device is null) return null;
        var exact = DeviceSounds.FirstOrDefault(s => string.Equals(s.Device.Id, device.Id, StringComparison.OrdinalIgnoreCase));
        if (exact is not null || AudioDeviceService.Resolve(device, connected) is not { } match) return exact;
        return DeviceSounds.FirstOrDefault(s => AudioDeviceService.Resolve(s.Device, connected)?.Id == match.Id);
    }
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

/// <summary>A sound for an output device: "builtin:&lt;id&gt;" or a file path, at a volume from 0 to 100.</summary>
public sealed class DeviceSound
{
    public DeviceRef Device { get; set; } = new("", "");
    public string Sound { get; set; } = "";
    public int Volume { get; set; } = 80;
}

public enum PopupMode { Always, ProblemsOnly, Never }

public enum PopupPosition { BottomRight, BottomCenter, TopRight, TopCenter, Center }
