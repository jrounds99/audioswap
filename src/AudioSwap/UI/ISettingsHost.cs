using AudioSwap.Config;
using AudioSwap.Hotkeys;

namespace AudioSwap.UI;

/// <summary>What the settings window needs from the running app.</summary>
internal interface ISettingsHost
{
    /// <summary>Persists the config and applies it (hotkeys, tray icon).</summary>
    void Save(AppConfig config);

    /// <summary>Hotkeys that couldn't be registered: profile index (-1 = cycle hotkey) to reason.</summary>
    IReadOnlyDictionary<int, string> HotkeyProblems { get; }

    event EventHandler? HotkeyProblemsChanged;

    /// <summary>Releases every hotkey while one is being recorded, so the keys reach the box.</summary>
    void SuspendHotkeys(bool suspend);

    /// <summary>While suspended: whether another app already owns the combination.</summary>
    bool IsHotkeyFree(Hotkey hotkey);

    void TryProfile(Profile profile);

    void PreviewPopup(AppConfig settings, bool withProblem);

    void Quit();
}
