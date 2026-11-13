using System.Runtime.InteropServices;
using AudioSwap.Config;

namespace AudioSwap.Audio;

public enum DeviceFlow { Output, Input }

public sealed record AudioDevice(string Id, string Name, DeviceFlow Flow);

/// <summary>
/// Lists active audio endpoints, reads and sets the defaults, and raises
/// <see cref="DevicesChanged"/> (on the UI thread, debounced) when Windows reports
/// devices appearing, disappearing or a default changing.
/// </summary>
internal sealed class AudioDeviceService : IDisposable
{
    static readonly PropertyKey FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    const uint StgmRead = 0;

    readonly IMMDeviceEnumerator _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCoClass();
    readonly NotificationClient? _client;
    readonly System.Windows.Forms.Timer _debounce = new() { Interval = 300 };

    public event EventHandler? DevicesChanged;

    public AudioDeviceService()
    {
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        };

        // Windows calls the notification client on its own threads; bounce to ours.
        if (SynchronizationContext.Current is { } ui)
        {
            _client = new NotificationClient(() => ui.Post(_ => { _debounce.Stop(); _debounce.Start(); }, null));
            _enumerator.RegisterEndpointNotificationCallback(_client);
        }
    }

    public IReadOnlyList<AudioDevice> GetDevices(DeviceFlow flow)
    {
        var devices = new List<AudioDevice>();
        if (_enumerator.EnumAudioEndpoints(ToDataFlow(flow), DeviceStates.Active, out var collection) != 0)
            return devices;
        try
        {
            collection.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) != 0) continue;
                try
                {
                    if (Describe(device, flow) is { } d) devices.Add(d);
                }
                finally { Marshal.ReleaseComObject(device); }
            }
        }
        finally { Marshal.ReleaseComObject(collection); }

        // Identical names (e.g. three monitors of the same model) get " #1", " #2"... in
        // ID order, so they can be told apart in menus and popups.
        foreach (var group in devices.GroupBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).Where(g => g.Count() > 1).ToList())
        {
            int n = 1;
            foreach (var device in group.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
                devices[devices.IndexOf(device)] = device with { Name = $"{device.Name} #{n++}" };
        }

        devices.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return devices;
    }

    public AudioDevice? GetDefault(DeviceFlow flow, ERole role = ERole.Multimedia)
    {
        var id = GetDefaultId(flow, role);
        return id is null ? null : GetDevices(flow).FirstOrDefault(d => d.Id == id);
    }

    /// <summary>Finds a saved device among the connected ones.</summary>
    public AudioDevice? Resolve(DeviceRef? target, DeviceFlow flow) => Resolve(target, GetDevices(flow));

    /// <summary>
    /// Matches by endpoint ID first. If that fails (USB devices can get a new ID on a
    /// different port) it falls back to the name, but only when exactly one connected
    /// device has it.
    /// </summary>
    public static AudioDevice? Resolve(DeviceRef? target, IReadOnlyList<AudioDevice> connected)
    {
        if (target is null) return null;
        var byId = connected.FirstOrDefault(d => string.Equals(d.Id, target.Id, StringComparison.OrdinalIgnoreCase));
        if (byId is not null) return byId;
        var byName = connected.Where(d => string.Equals(d.Name, target.Name, StringComparison.CurrentCultureIgnoreCase)).ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    /// <summary>Makes the device the default for apps and system sounds, and optionally for calls.</summary>
    public void SetDefault(string deviceId, bool includeCommunications)
    {
        SetDefaultForRole(deviceId, ERole.Console);
        SetDefaultForRole(deviceId, ERole.Multimedia);
        if (includeCommunications) SetDefaultForRole(deviceId, ERole.Communications);
    }

    public void SetDefaultForRole(string deviceId, ERole role)
    {
        var policy = (IPolicyConfig)new PolicyConfigCoClass();
        try { Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role)); }
        finally { Marshal.ReleaseComObject(policy); }
    }

    public string? GetDefaultId(DeviceFlow flow, ERole role)
    {
        if (_enumerator.GetDefaultAudioEndpoint(ToDataFlow(flow), role, out var device) != 0) return null;
        try { return device.GetId(out var id) == 0 ? id : null; }
        finally { Marshal.ReleaseComObject(device); }
    }

    public void Dispose()
    {
        _debounce.Dispose();
        if (_client is not null) _enumerator.UnregisterEndpointNotificationCallback(_client);
        Marshal.ReleaseComObject(_enumerator);
    }

    static EDataFlow ToDataFlow(DeviceFlow flow) => flow == DeviceFlow.Output ? EDataFlow.Render : EDataFlow.Capture;

    static AudioDevice? Describe(IMMDevice device, DeviceFlow flow)
    {
        if (device.GetId(out var id) != 0) return null;
        return new AudioDevice(id, ReadFriendlyName(device) ?? id, flow);
    }

    static string? ReadFriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(StgmRead, out var store) != 0) return null;
        try
        {
            var key = FriendlyNameKey;
            if (store.GetValue(ref key, out var value) != 0) return null;
            try
            {
                return value.VarType == PropVariant.VT_LPWSTR ? Marshal.PtrToStringUni(value.Value) : null;
            }
            finally { NativeAudio.PropVariantClear(ref value); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    sealed class NotificationClient(Action changed) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, uint newState) => changed();
        public void OnDeviceAdded(string deviceId) => changed();
        public void OnDeviceRemoved(string deviceId) => changed();
        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) => changed();
        // Fires constantly for some drivers (volume, levels) and never changes what we show.
        public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
    }
}
