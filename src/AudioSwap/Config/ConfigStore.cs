using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioSwap.Config;

/// <summary>Reads and writes %APPDATA%\AudioSwap\config.json.</summary>
internal static class ConfigStore
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioSwap");

    public static string FilePath { get; } = Path.Combine(Folder, "config.json");

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Returns null on first run. A file that can't be read is moved aside (so a typo
    /// in a hand edit is never silently overwritten) and reported through <paramref name="error"/>.
    /// </summary>
    public static AppConfig? Load(out string? error)
    {
        error = null;
        if (!File.Exists(FilePath)) return null;
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Options) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            var backup = Path.Combine(Folder, $"config.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try { File.Move(FilePath, backup); } catch (IOException) { }
            error = $"config.json couldn't be read ({ex.Message}). It was saved as {Path.GetFileName(backup)} and AudioSwap started fresh.";
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(Folder);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, Options));
        File.Move(temp, FilePath, overwrite: true);
    }

    public static AppConfig Clone(AppConfig config) =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config, Options), Options)!;
}
