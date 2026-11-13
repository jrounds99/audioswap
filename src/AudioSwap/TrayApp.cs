using System.Diagnostics;
using System.Reflection;
using AudioSwap.Audio;
using AudioSwap.Config;
using AudioSwap.Hotkeys;
using AudioSwap.UI;
using AudioSwap.UI.Controls;

namespace AudioSwap;

/// <summary>
/// The running app: owns the tray icon, the global hotkeys, the popup and the
/// settings window, and does the actual switching.
/// </summary>
internal sealed class TrayApp : ApplicationContext, ISettingsHost
{
    const int CycleHotkeyId = 0x7000, ProbeHotkeyId = 0x7FFF;

    readonly AudioDeviceService _audio = new();
    readonly HotkeyManager _hotkeys = new();
    readonly OsdWindow _osd = new();
    readonly ContextMenuStrip _menu = new() { ShowItemToolTips = true };
    readonly NotifyIcon _tray;
    readonly Bitmap _warningImage = Glyphs.Render(Glyphs.Warning, Color.FromArgb(214, 140, 0), 16);
    AppConfig _config;
    SettingsForm? _window;
    Dictionary<int, string> _problems = [];
    bool _suspended, _quitting;
    int _lastApplied = -1;

    public event EventHandler? HotkeyProblemsChanged;

    public IReadOnlyDictionary<int, string> HotkeyProblems => _problems;

    public TrayApp(AppConfig? config, string? loadError, bool showWindow)
    {
        bool firstRun = config is null;
        _config = config ?? CreateStarterConfig();
        if (firstRun) TrySave();

        ThemedMenu.Apply(_menu);
        _menu.Opening += (_, _) => BuildMenu();
        _tray = new NotifyIcon
        {
            Icon = AppIcon.Load(SystemInformation.SmallIconSize),
            ContextMenuStrip = _menu,
            Text = "AudioSwap",
            Visible = _config.ShowTrayIcon,
        };
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowTrayMenu();
        };

        _hotkeys.Pressed += OnHotkey;
        _audio.DevicesChanged += (_, _) =>
        {
            UpdateTrayText();
            _window?.RefreshDevices();
        };

        StartupManager.RefreshPath();
        RegisterHotkeys();
        UpdateTrayText();

        if (loadError is not null) Notice("Settings were reset", loadError);
        else if (_problems.Count > 0) Notice("Some hotkeys aren't available", DescribeProblems());

        if (firstRun || showWindow) ShowWindow();
    }

    public void ShowWindow()
    {
        if (_window is null || _window.IsDisposed)
        {
            _window = new SettingsForm(this, _audio.GetDevices, ConfigStore.Clone(_config));
            _window.FormClosed += (_, _) => OnWindowClosed();
        }
        _window.ShowFromTray();
    }

    #region Switching

    public void TryProfile(Profile profile) => ApplyProfile(profile);

    void Apply(int index)
    {
        _lastApplied = index;
        ApplyProfile(_config.Profiles[index]);
    }

    void ApplyProfile(Profile profile)
    {
        var rows = new List<OsdRow>(2);
        foreach (var flow in new[] { DeviceFlow.Output, DeviceFlow.Input })
        {
            var target = flow == DeviceFlow.Output ? profile.Output : profile.Input;
            if (target is null)
            {
                rows.Add(new OsdRow(flow, OsdRowState.Unchanged, _audio.GetDefault(flow)?.Name ?? "None"));
                continue;
            }

            // Not plugged in: skip it quietly and say so in the popup; the other device still switches.
            var device = _audio.Resolve(target, flow);
            if (device is null)
            {
                rows.Add(new OsdRow(flow, OsdRowState.Missing, target.Name));
                continue;
            }

            try
            {
                _audio.SetDefault(device.Id, profile.IncludeCommunications);
                rows.Add(new OsdRow(flow, OsdRowState.Switched, device.Name));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"AudioSwap: switching to {device.Name} failed: {ex}");
                rows.Add(new OsdRow(flow, OsdRowState.Failed, device.Name));
            }
        }

        var title = string.IsNullOrWhiteSpace(profile.Name) ? "Audio switched" : profile.Name;
        ShowPopup(new OsdContent(title, null, rows));
        UpdateTrayText();
    }

    void Cycle()
    {
        int count = _config.Profiles.Count;
        if (count == 0) return;
        int current = _lastApplied >= 0 && _lastApplied < count ? _lastApplied : FindActiveProfile();
        Apply((current + 1) % count);
    }

    int FindActiveProfile()
    {
        var snapshot = TakeSnapshot();
        return _config.Profiles.FindIndex(p => IsActive(p, snapshot));
    }

    sealed record DeviceSnapshot(
        IReadOnlyList<AudioDevice> Outputs, IReadOnlyList<AudioDevice> Inputs, AudioDevice? Output, AudioDevice? Input);

    DeviceSnapshot TakeSnapshot() => new(
        _audio.GetDevices(DeviceFlow.Output), _audio.GetDevices(DeviceFlow.Input),
        _audio.GetDefault(DeviceFlow.Output), _audio.GetDefault(DeviceFlow.Input));

    /// <summary>True when every device the profile sets is already the default.</summary>
    static bool IsActive(Profile p, DeviceSnapshot s)
    {
        if (p.Output is null && p.Input is null) return false;
        if (p.Output is not null && (AudioDeviceService.Resolve(p.Output, s.Outputs) is not { } o || o.Id != s.Output?.Id)) return false;
        if (p.Input is not null && (AudioDeviceService.Resolve(p.Input, s.Inputs) is not { } i || i.Id != s.Input?.Id)) return false;
        return true;
    }

    static List<string> MissingDevices(Profile p, DeviceSnapshot s)
    {
        var missing = new List<string>();
        if (p.Output is not null && AudioDeviceService.Resolve(p.Output, s.Outputs) is null) missing.Add(p.Output.Name);
        if (p.Input is not null && AudioDeviceService.Resolve(p.Input, s.Inputs) is null) missing.Add(p.Input.Name);
        return missing;
    }

    #endregion

    #region Popup

    void ShowPopup(OsdContent content)
    {
        bool show = _config.PopupMode switch
        {
            PopupMode.Always => true,
            PopupMode.ProblemsOnly => content.HasProblem,
            _ => false,
        };
        // Problems linger a little longer so they're noticed without being in the way.
        if (show) _osd.ShowContent(content, _config.PopupPosition, content.HasProblem ? Math.Max(_config.PopupSeconds, 4) : _config.PopupSeconds);
    }

    void Notice(string title, string message) =>
        _osd.ShowContent(OsdContent.Notice(title, message), _config.PopupPosition, 6);

    public void PreviewPopup(AppConfig settings, bool withProblem)
    {
        var output = _audio.GetDefault(DeviceFlow.Output)?.Name ?? "Speakers";
        var input = _audio.GetDefault(DeviceFlow.Input)?.Name ?? "Microphone";
        OsdRow[] rows = withProblem
            ? [new(DeviceFlow.Output, OsdRowState.Unchanged, output), new(DeviceFlow.Input, OsdRowState.Missing, "Headset Microphone (USB Audio)")]
            : [new(DeviceFlow.Output, OsdRowState.Switched, output), new(DeviceFlow.Input, OsdRowState.Switched, input)];
        var title = withProblem ? "Preview: device not plugged in" : "Preview";
        _osd.ShowContent(new OsdContent(title, null, rows), settings.PopupPosition, settings.PopupSeconds);
    }

    #endregion

    #region Hotkeys

    void OnHotkey(int id)
    {
        if (id == CycleHotkeyId) Cycle();
        else if (id >= 1 && id <= _config.Profiles.Count) Apply(id - 1);
    }

    void RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var problems = new Dictionary<int, string>();
        var owners = new Dictionary<Hotkey, string>();

        void Register(int key, int id, Hotkey? hotkey, string owner)
        {
            if (hotkey is not { IsValid: true }) return;
            if (owners.TryGetValue(hotkey, out var first)) problems[key] = $"Also used by {first}";
            else if (!_hotkeys.Register(id, hotkey)) problems[key] = "Taken by another app";
            else owners[hotkey] = owner;
        }

        for (int i = 0; i < _config.Profiles.Count; i++)
            Register(i, i + 1, _config.Profiles[i].Hotkey, $"“{_config.Profiles[i].Name}”");
        Register(-1, CycleHotkeyId, _config.CycleHotkey, "the cycle hotkey");

        _problems = problems;
        HotkeyProblemsChanged?.Invoke(this, EventArgs.Empty);
    }

    string DescribeProblems() => string.Join("\n", _problems.Select(pair =>
    {
        var (owner, hotkey) = pair.Key == -1
            ? ("Cycle", _config.CycleHotkey)
            : (_config.Profiles[pair.Key].Name, _config.Profiles[pair.Key].Hotkey);
        return $"{hotkey?.ToDisplayString()} ({owner}): {pair.Value.ToLowerInvariant()}";
    }));

    public void SuspendHotkeys(bool suspend)
    {
        _suspended = suspend;
        if (suspend) _hotkeys.UnregisterAll();
        else RegisterHotkeys();
    }

    public bool IsHotkeyFree(Hotkey hotkey)
    {
        if (!_hotkeys.Register(ProbeHotkeyId, hotkey)) return false;
        _hotkeys.Unregister(ProbeHotkeyId);
        return true;
    }

    #endregion

    #region Config

    public void Save(AppConfig config)
    {
        _config = ConfigStore.Clone(config);
        TrySave();
        _tray.Visible = _config.ShowTrayIcon;
        if (!_suspended) RegisterHotkeys();
        UpdateTrayText();
    }

    void TrySave()
    {
        try { ConfigStore.Save(_config); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice("Couldn't save settings", ex.Message);
        }
    }

    /// <summary>Changes a setting from the tray, going through the window if it's open so it doesn't save over us.</summary>
    void ChangeSetting(Action<AppConfig> change)
    {
        if (_window is { IsDisposed: false }) _window.ApplyExternalChange(change);
        else
        {
            change(_config);
            Save(_config);
        }
    }

    AppConfig CreateStarterConfig()
    {
        static DeviceRef? Ref(AudioDevice? d) => d is null ? null : new DeviceRef(d.Id, d.Name);
        return new AppConfig
        {
            Profiles =
            {
                new Profile
                {
                    Name = "My setup",
                    Hotkey = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Keys.D1),
                    Output = Ref(_audio.GetDefault(DeviceFlow.Output)),
                    Input = Ref(_audio.GetDefault(DeviceFlow.Input)),
                },
            },
        };
    }

    #endregion

    #region Tray

    void BuildMenu()
    {
        _menu.Items.Clear();
        var snapshot = TakeSnapshot();

        if (_config.Profiles.Count == 0) _menu.Items.Add(new ToolStripMenuItem("No profiles yet") { Enabled = false });
        for (int i = 0; i < _config.Profiles.Count; i++)
        {
            var profile = _config.Profiles[i];
            var item = new ToolStripMenuItem(string.IsNullOrWhiteSpace(profile.Name) ? "Untitled" : profile.Name)
            {
                ShortcutKeyDisplayString = profile.Hotkey?.ToDisplayString(),
                Checked = IsActive(profile, snapshot),
            };
            var missing = MissingDevices(profile, snapshot);
            if (missing.Count > 0)
            {
                item.Image = _warningImage;
                item.ToolTipText = "Not connected: " + string.Join(", ", missing);
            }
            int index = i;
            item.Click += (_, _) => Apply(index);
            _menu.Items.Add(item);
        }

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(DeviceMenu("Output device", DeviceFlow.Output, snapshot.Outputs, snapshot.Output));
        _menu.Items.Add(DeviceMenu("Input device", DeviceFlow.Input, snapshot.Inputs, snapshot.Input));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Open AudioSwap", null, (_, _) => ShowWindow())
        {
            Font = Theme.CreateFont(9.75f, semibold: true),
        });
        _menu.Items.Add(new ToolStripMenuItem("Sound settings", null, (_, _) =>
            Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true })));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Start with Windows", null, (_, _) =>
            StartupManager.SetEnabled(!StartupManager.IsEnabled)) { Checked = StartupManager.IsEnabled });
        _menu.Items.Add(new ToolStripMenuItem("Hide tray icon", null, (_, _) => HideTrayIcon()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Quit()));
        ThemedMenu.StyleItems(_menu);
    }

    ToolStripMenuItem DeviceMenu(string text, DeviceFlow flow, IReadOnlyList<AudioDevice> devices, AudioDevice? current)
    {
        var menu = new ToolStripMenuItem(text);
        if (devices.Count == 0) menu.DropDownItems.Add(new ToolStripMenuItem("No devices found") { Enabled = false });
        foreach (var device in devices)
        {
            var item = new ToolStripMenuItem(device.Name) { Checked = device.Id == current?.Id };
            var target = new DeviceRef(device.Id, device.Name);
            item.Click += (_, _) => ApplyProfile(new Profile
            {
                Name = text,
                Output = flow == DeviceFlow.Output ? target : null,
                Input = flow == DeviceFlow.Input ? target : null,
            });
            menu.DropDownItems.Add(item);
        }
        return menu;
    }

    // NotifyIcon only opens its menu on right-click; reuse its private method so a
    // left-click opens it in the same, correctly placed, way.
    void ShowTrayMenu() =>
        typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(_tray, null);

    void HideTrayIcon()
    {
        ChangeSetting(c => c.ShowTrayIcon = false);
        Notice("Tray icon hidden", "Your hotkeys still work. Open AudioSwap again to bring the icon back.");
    }

    void UpdateTrayText()
    {
        static string Short(string? name) => name is null ? "none" : name.Length > 52 ? name[..51] + "…" : name;
        _tray.Text = $"AudioSwap\nOut: {Short(_audio.GetDefault(DeviceFlow.Output)?.Name)}\nIn: {Short(_audio.GetDefault(DeviceFlow.Input)?.Name)}";
    }

    #endregion

    void OnWindowClosed()
    {
        _window = null;
        if (_quitting) return;
        if (_suspended) SuspendHotkeys(false);
        if (!_config.ShowTrayIcon)
            Notice("AudioSwap is still running", "Your hotkeys keep working. Open AudioSwap again to change settings.");
    }

    public void Quit()
    {
        _quitting = true;
        _window?.Close();
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _hotkeys.Dispose();
        _osd.Dispose();
        _menu.Dispose();
        _warningImage.Dispose();
        _audio.Dispose();
        base.ExitThreadCore();
    }
}
