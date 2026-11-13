using AudioSwap.Audio;

namespace AudioSwap.UI;

internal enum OsdRowState { Switched, Unchanged, Missing, Failed }

internal sealed record OsdRow(DeviceFlow Flow, OsdRowState State, string DeviceName);

/// <summary>What the popup shows: a title, an optional message line, and device rows.</summary>
internal sealed record OsdContent(string Title, string? Message, IReadOnlyList<OsdRow> Rows, bool IsNotice = false)
{
    public static OsdContent Notice(string title, string message) => new(title, message, [], IsNotice: true);

    public bool HasProblem => Rows.Any(r => r.State is OsdRowState.Missing or OsdRowState.Failed);
}
