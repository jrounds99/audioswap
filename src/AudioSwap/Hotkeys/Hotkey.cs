using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioSwap.Hotkeys;

// Values match the MOD_* flags RegisterHotKey expects.
[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

/// <summary>A global key combination, stored in config as text like "Ctrl+Alt+F1".</summary>
[JsonConverter(typeof(HotkeyJsonConverter))]
public sealed record Hotkey(HotkeyModifiers Modifiers, Keys Key)
{
    static readonly Dictionary<Keys, string> FriendlyNames = new()
    {
        [Keys.Oemtilde] = "`", [Keys.OemMinus] = "-", [Keys.Oemplus] = "=",
        [Keys.OemOpenBrackets] = "[", [Keys.OemCloseBrackets] = "]", [Keys.OemPipe] = "\\",
        [Keys.OemSemicolon] = ";", [Keys.OemQuotes] = "'", [Keys.Oemcomma] = ",",
        [Keys.OemPeriod] = ".", [Keys.OemQuestion] = "/", [Keys.Return] = "Enter",
        [Keys.PageUp] = "PageUp", [Keys.PageDown] = "PageDown", [Keys.Back] = "Backspace",
        [Keys.Escape] = "Esc", [Keys.Space] = "Space", [Keys.Insert] = "Ins", [Keys.Delete] = "Del",
        [Keys.Multiply] = "Num *", [Keys.Add] = "Num +", [Keys.Subtract] = "Num -",
        [Keys.Divide] = "Num /", [Keys.Decimal] = "Num .", [Keys.Scroll] = "ScrollLock",
        [Keys.Pause] = "Pause", [Keys.VolumeMute] = "Mute", [Keys.VolumeUp] = "Vol+",
        [Keys.VolumeDown] = "Vol-", [Keys.MediaPlayPause] = "Play/Pause",
        [Keys.MediaNextTrack] = "Next track", [Keys.MediaPreviousTrack] = "Prev track",
    };

    /// <summary>
    /// A hotkey needs a modifier, except F-keys and media keys, which are safe to grab
    /// on their own (handy for macro keys mapped to F13-F24).
    /// </summary>
    public bool IsValid => Key != Keys.None && (Modifiers != HotkeyModifiers.None || IsStandaloneKey(Key));

    public static bool IsStandaloneKey(Keys key) =>
        key is >= Keys.F1 and <= Keys.F24
            or >= Keys.BrowserBack and <= Keys.LaunchApplication2
            or Keys.Pause or Keys.Scroll;

    public static bool IsModifierKey(Keys key) =>
        key is Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;

    public override string ToString() => Format(Modifiers, StorageName(Key), "+");

    public string ToDisplayString() => Format(Modifiers, DisplayName(Key), " + ");

    public static string FormatModifiers(HotkeyModifiers modifiers) => Format(modifiers, null, " + ");

    public static bool TryParse(string? text, out Hotkey? hotkey)
    {
        hotkey = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = HotkeyModifiers.None;
        Keys key = Keys.None;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= HotkeyModifiers.Ctrl; break;
                case "alt": modifiers |= HotkeyModifiers.Alt; break;
                case "shift": modifiers |= HotkeyModifiers.Shift; break;
                case "win" or "windows": modifiers |= HotkeyModifiers.Win; break;
                default:
                    if (key != Keys.None) return false;
                    if (raw.Length == 1 && char.IsDigit(raw[0])) key = Keys.D0 + (raw[0] - '0');
                    else if (!Enum.TryParse(raw, ignoreCase: true, out key)) return false;
                    break;
            }
        }
        if (key == Keys.None) return false;
        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    static string Format(HotkeyModifiers modifiers, string? key, string separator)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        if (key is not null) parts.Add(key);
        return string.Join(separator, parts);
    }

    static string StorageName(Keys key) =>
        key is >= Keys.D0 and <= Keys.D9 ? ((char)('0' + (key - Keys.D0))).ToString() : key.ToString();

    static string DisplayName(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + (key - Keys.NumPad0),
        _ when FriendlyNames.TryGetValue(key, out var name) => name,
        _ => key.ToString(),
    };
}

internal sealed class HotkeyJsonConverter : JsonConverter<Hotkey>
{
    public override Hotkey? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && Hotkey.TryParse(reader.GetString(), out var hotkey) ? hotkey : null;

    public override void Write(Utf8JsonWriter writer, Hotkey value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
