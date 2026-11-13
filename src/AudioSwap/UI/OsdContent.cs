using AudioSwap.Audio;

namespace AudioSwap.UI;

internal enum OsdRowState { Switched, Unchanged, Missing, Failed }

internal sealed record OsdRow(DeviceFlow Flow, OsdRowState State, string DeviceName);

/// <summary>What the popup shows: a title, an optional message line, device rows and an optional warning line.</summary>
internal sealed record OsdContent(string Title, string? Message, IReadOnlyList<OsdRow> Rows, bool IsNotice = false, string? Warning = null)
{
    public static OsdContent Notice(string title, string message) => new(title, message, [], IsNotice: true);

    public bool HasProblem => Warning is not null || Rows.Any(r => r.State is OsdRowState.Missing or OsdRowState.Failed);
}
