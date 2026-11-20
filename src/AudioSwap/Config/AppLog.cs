namespace AudioSwap.Config;

/// <summary>
/// A short diagnostic log next to config.json (audioswap.log): how AudioSwap was started,
/// whether the settings loaded and saved, and anything that went wrong. Trimmed so it
/// stays small; never throws.
/// </summary>
internal static class AppLog
{
    const long MaxBytes = 128 * 1024;
    static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(ConfigStore.Folder, "audioswap.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(ConfigStore.Folder);
                if (new FileInfo(FilePath) is { Exists: true, Length: > MaxBytes })
                {
                    var lines = File.ReadAllLines(FilePath);
                    File.WriteAllLines(FilePath, lines[(lines.Length / 2)..]); // keep the newer half
                }
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
