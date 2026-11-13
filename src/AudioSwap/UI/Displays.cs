using System.Runtime.InteropServices;

namespace AudioSwap.UI;

internal sealed record DisplayInfo(string Id, string Name, Screen Screen);

/// <summary>
/// Connected displays, named so you can tell them apart ("Left · XV272U V"), and the
/// display the popup should use. IDs are the monitor's device path, which survives
/// restarts and reconnects better than the \\.\DISPLAYn numbers.
/// </summary>
internal static class Displays
{
    public const string Primary = "primary";

    public static IReadOnlyList<DisplayInfo> GetAll()
    {
        var screens = Screen.AllScreens;
        var monitors = QueryMonitors();
        var positions = PositionNames(screens.Select(s => s.Bounds).ToList());
        var displays = new List<DisplayInfo>(screens.Length);
        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            monitors.TryGetValue(screen.DeviceName, out var monitor);
            var id = string.IsNullOrEmpty(monitor.Path) ? screen.DeviceName : monitor.Path;
            var model = string.IsNullOrEmpty(monitor.Name) ? $"{screen.Bounds.Width} × {screen.Bounds.Height}" : monitor.Name;
            var where = positions?[i] ?? $"Display {new string(screen.DeviceName.Where(char.IsDigit).ToArray())}";
            displays.Add(new DisplayInfo(id, $"{where} · {model}{(screen.Primary ? " (main)" : "")}", screen));
        }
        return displays.OrderBy(d => d.Screen.Bounds.X).ThenBy(d => d.Screen.Bounds.Y).ToList();
    }

    /// <summary>The screen for a saved choice. A display that isn't connected falls back to the active window's.</summary>
    public static Screen Resolve(string? choice)
    {
        if (choice == Primary) return Screen.PrimaryScreen!;
        if (choice is not null && GetAll().FirstOrDefault(d => string.Equals(d.Id, choice, StringComparison.OrdinalIgnoreCase)) is { } chosen)
            return chosen.Screen;
        var foreground = GetForegroundWindow();
        return foreground != IntPtr.Zero ? Screen.FromHandle(foreground) : Screen.PrimaryScreen!;
    }

    /// <summary>Left/Middle/Right (or Top/Middle/Bottom) when two or three displays sit in one row or column.</summary>
    static string[]? PositionNames(IReadOnlyList<Rectangle> bounds)
    {
        int n = bounds.Count;
        if (n is < 2 or > 3) return null;
        var byX = Enumerable.Range(0, n).OrderBy(i => bounds[i].X).ToArray();
        if (InSequence(byX, i => bounds[i].Left, i => bounds[i].Right))
            return Assign(byX, n == 2 ? ["Left", "Right"] : ["Left", "Middle", "Right"]);
        var byY = Enumerable.Range(0, n).OrderBy(i => bounds[i].Y).ToArray();
        if (InSequence(byY, i => bounds[i].Top, i => bounds[i].Bottom))
            return Assign(byY, n == 2 ? ["Top", "Bottom"] : ["Top", "Middle", "Bottom"]);
        return null;

        static bool InSequence(int[] order, Func<int, int> start, Func<int, int> end) =>
            order.Zip(order.Skip(1)).All(pair => start(pair.Second) >= end(pair.First));

        static string[] Assign(int[] order, string[] names)
        {
            var result = new string[order.Length];
            for (int i = 0; i < order.Length; i++) result[order[i]] = names[i];
            return result;
        }
    }

    /// <summary>GDI device name (\\.\DISPLAY1) to the monitor's device path and model name.</summary>
    static Dictionary<string, (string Path, string Name)> QueryMonitors()
    {
        var monitors = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        DISPLAYCONFIG_PATH_INFO[] paths = [];
        // The topology can change between the two calls; ask again if the buffers came up short.
        for (int attempt = 0; ; attempt++)
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0) return monitors;
            paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            int result = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (result == 0)
            {
                Array.Resize(ref paths, (int)pathCount);
                break;
            }
            if (result != ERROR_INSUFFICIENT_BUFFER || attempt == 2) return monitors;
        }

        foreach (var path in paths)
        {
            var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
            {
                Header = new(DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(), path.SourceAdapter, path.SourceId),
            };
            var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                Header = new(DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(), path.TargetAdapter, path.TargetId),
            };
            if (DisplayConfigGetDeviceInfo(ref source) != 0 || DisplayConfigGetDeviceInfo(ref target) != 0) continue;
            monitors.TryAdd(source.ViewGdiDeviceName, (target.MonitorDevicePath, target.MonitorFriendlyDeviceName));
        }
        return monitors;
    }

    #region Win32

    const uint QDC_ONLY_ACTIVE_PATHS = 2;
    const int ERROR_INSUFFICIENT_BUFFER = 122;
    const int DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1, DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct LUID { public uint LowPart; public int HighPart; }

    // Only the IDs are read; the rest of each struct is there to give it its native size (72 bytes).
    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_PATH_INFO
    {
        public LUID SourceAdapter;
        public uint SourceId, SourceModeIndex, SourceStatus;
        public LUID TargetAdapter;
        public uint TargetId, TargetModeIndex, OutputTechnology, Rotation, Scaling, RefreshNumerator, RefreshDenominator,
            ScanLineOrdering, TargetAvailable, TargetStatus;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    struct DISPLAYCONFIG_MODE_INFO { [FieldOffset(0)] public uint InfoType; }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_DEVICE_INFO_HEADER(int type, int size, LUID adapter, uint id)
    {
        public int Type = type, Size = size;
        public LUID Adapter = adapter;
        public uint Id = id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
        public uint Flags, OutputTechnology;
        public ushort EdidManufactureId, EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
    }

    [DllImport("user32.dll")]
    static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DISPLAYCONFIG_PATH_INFO[] paths,
        ref uint modeCount, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr currentTopologyId);

    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME request);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    #endregion
}
