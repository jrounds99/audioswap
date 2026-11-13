using System.Diagnostics;
using AudioSwap.Audio;
using AudioSwap.Config;
using AudioSwap.Hotkeys;
using AudioSwap.UI.Controls;

namespace AudioSwap.UI;

/// <summary>
/// The AudioSwap window: profiles on the left, the selected profile on the right,
/// app-wide options below. Every change saves itself shortly after it's made.
/// </summary>
internal sealed class SettingsForm : ThemedForm
{
    static readonly object CycleKey = new();
    static readonly double[] Durations = [1, 1.5, 2, 2.5, 3, 4, 5, 7, 10];
    const TextFormatFlags ItemText = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
        | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;

    readonly ISettingsHost _host;
    readonly Func<DeviceFlow, IReadOnlyList<AudioDevice>> _devices;
    readonly AppConfig _config;
    readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 400 };
    readonly Dictionary<object, string> _problems = [];
    readonly Font _itemTitleFont = Theme.CreateFont(9.75f, semibold: true);
    readonly Font _keycapFont = Theme.CreateFont(8f);
    IReadOnlyList<AudioDevice> _outputs = [];
    IReadOnlyList<AudioDevice> _inputs = [];
    bool _loading, _dirty, _active, _capturing, _previewProblem;

    readonly ThemedListBox _list = new() { Dock = DockStyle.Fill };
    readonly FluentButton _add = new("Add", "");
    readonly FluentButton _copy = new("", "", FluentButton.Kind.Subtle, "Duplicate");
    readonly FluentButton _remove = new("", "", FluentButton.Kind.Subtle, "Remove");
    readonly FluentButton _up = new("", "", FluentButton.Kind.Subtle, "Move up");
    readonly FluentButton _down = new("", "", FluentButton.Kind.Subtle, "Move down");

    readonly TableLayoutPanel _editor = MakeGrid(2);
    readonly TextBox _name = new() { MaxLength = 60 };
    readonly HotkeyBox _hotkey = new();
    readonly InputFrame _nameFrame, _hotkeyFrame, _cycleFrame;
    readonly ThemedLabel _hotkeyStatus = new("") { IsWarning = true };
    readonly DropDown _output = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    readonly DropDown _input = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    readonly ToggleSwitch _comms = new("Also set as the communications device");
    readonly FluentButton _try = new("Switch now", "");

    readonly DropDown _popupMode = new() { Width = 250 };
    readonly DropDown _position = new() { Width = 250 };
    readonly DropDown _seconds = new() { Width = 130 };
    readonly FluentButton _preview = new("Preview", "");
    readonly HotkeyBox _cycle = new();
    readonly ThemedLabel _cycleStatus = new("") { IsWarning = true };
    readonly ToggleSwitch _startup = new("Start with Windows");
    readonly ToggleSwitch _minimizeToTray = new("Minimize to tray");
    readonly ToggleSwitch _showTray = new("Show icon in the system tray");

    public SettingsForm(ISettingsHost host, Func<DeviceFlow, IReadOnlyList<AudioDevice>> devices, AppConfig config)
    {
        _host = host;
        _devices = devices;
        _config = config;
        _nameFrame = new InputFrame(_name) { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _hotkeyFrame = new InputFrame(_hotkey, clearButton: true) { Width = 230 };
        _cycleFrame = new InputFrame(_cycle, clearButton: true) { Width = 250 };

        SuspendLayout();
        Text = "AudioSwap";
        Icon = AppIcon.Load(new Size(32, 32));
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(920, 720);
        MinimumSize = new Size(860, 700);
        Padding = new Padding(24, 18, 24, 18);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        foreach (var height in new[] { -1, -1, 100, -1, -1, -1 })
            root.RowStyles.Add(height < 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Percent, height));

        AddSpanning(root, BuildHeader(), 0);
        root.Controls.Add(MakeSection("Profiles"), 0, 1);
        root.Controls.Add(MakeSection("Selected profile"), 1, 1);
        root.Controls.Add(BuildProfilesCard(), 0, 2);
        root.Controls.Add(BuildEditorCard(), 1, 2);
        AddSpanning(root, MakeSection("General"), 3);
        AddSpanning(root, BuildGeneralCard(), 4);
        AddSpanning(root, BuildFooter(), 5);
        Controls.Add(root);
        ResumeLayout(false);
        PerformLayout();

        WireEvents();
        LoadGeneral();
        _outputs = _devices(DeviceFlow.Output);
        _inputs = _devices(DeviceFlow.Input);
        foreach (var profile in _config.Profiles) _list.Items.Add(profile);
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        else LoadEditor();

        _host.HotkeyProblemsChanged += OnHostProblemsChanged;
        OnHostProblemsChanged(this, EventArgs.Empty);
    }

    Profile? Selected => _list.SelectedItem as Profile;

    public void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>A setting changed from the tray menu while this window is open.</summary>
    public void ApplyExternalChange(Action<AppConfig> change)
    {
        change(_config);
        LoadGeneral();
        _dirty = true;
        FlushSave();
    }

    public void RefreshDevices()
    {
        _outputs = _devices(DeviceFlow.Output);
        _inputs = _devices(DeviceFlow.Input);
        LoadDeviceLists();
        _list.Invalidate();
    }

    #region Layout

    static Control BuildHeader()
    {
        var logo = new PictureBox
        {
            Image = AppIcon.LoadBitmap(64), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(44, 44),
            Margin = new Padding(0, 0, 14, 0), Anchor = AnchorStyles.Left,
        };
        var titles = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
        titles.Controls.Add(new ThemedLabel("AudioSwap", ThemedLabel.Style.Title) { Margin = Padding.Empty });
        titles.Controls.Add(new ThemedLabel("Switch speakers and microphones with a single hotkey.", ThemedLabel.Style.Secondary) { Margin = Padding.Empty });

        var header = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        header.Controls.Add(logo, 0, 0);
        header.Controls.Add(titles, 1, 0);
        return header;
    }

    Control BuildProfilesCard()
    {
        var card = new Card { Margin = new Padding(0, 0, 8, 0), Padding = new Padding(6, 6, 6, 8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_list, 0, 0);

        var bar = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(6, 8, 2, 0) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _add.Margin = Padding.Empty;
        bar.Controls.Add(_add, 0, 0);
        bar.Controls.Add(MakeFlow(4, _copy, _remove, _up, _down), 1, 0);
        layout.Controls.Add(bar, 0, 1);

        card.Controls.Add(layout);
        return card;
    }

    Control BuildEditorCard()
    {
        var card = new Card { Margin = new Padding(8, 0, 0, 0), Padding = new Padding(20, 16, 20, 16) };
        _editor.Dock = DockStyle.Fill;
        _editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(_editor, "Name", _nameFrame);
        AddRow(_editor, "Hotkey", MakeFlow(12, _hotkeyFrame, _hotkeyStatus));
        AddRow(_editor, "Output", _output);
        AddRow(_editor, "Input", _input);
        AddRow(_editor, "", _comms);
        AddRow(_editor, "", new ThemedLabel("Discord, Teams and Zoom use the communications device for calls.", ThemedLabel.Style.Caption)
        {
            Margin = new Padding(52, 0, 0, 6),
        });
        AddRow(_editor, "", MakeFlow(12, _try,
            new ThemedLabel("Choose “No change” to leave a device as it is.", ThemedLabel.Style.Secondary)));
        _editor.RowCount++;
        _editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // soaks up spare height
        card.Controls.Add(_editor);
        return card;
    }

    Control BuildGeneralCard()
    {
        var card = new Card { AutoSize = true, Margin = Padding.Empty, Padding = new Padding(20, 14, 20, 14) };
        var columns = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

        var popup = MakeGrid(2);
        AddRow(popup, "Show popup", _popupMode);
        AddRow(popup, "Position", _position);
        AddRow(popup, "Duration", MakeFlow(8, _seconds, _preview));
        AddRow(popup, "Cycle hotkey", MakeFlow(12, _cycleFrame, _cycleStatus));
        columns.Controls.Add(popup, 0, 0);

        var app = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(16, 0, 0, 0) };
        foreach (var toggle in new[] { _startup, _minimizeToTray, _showTray })
        {
            toggle.Margin = new Padding(0, 4, 0, 4);
            app.Controls.Add(toggle);
        }
        app.Controls.Add(new ThemedLabel("When hidden, open AudioSwap again to get back here.", ThemedLabel.Style.Caption)
        {
            Margin = new Padding(52, 0, 0, 0),
        });
        columns.Controls.Add(app, 1, 0);

        card.Controls.Add(columns);
        return card;
    }

    Control BuildFooter()
    {
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 16, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var info = MakeFlow(0,
            new ThemedLabel("", ThemedLabel.Style.Secondary) { Font = new Font(Glyphs.Family, 9f), Margin = new Padding(0, 1, 6, 0) },
            new ThemedLabel("Changes save automatically", ThemedLabel.Style.Secondary) { Margin = new Padding(0, 0, 14, 0) },
            new ThemedLink("Config folder", OpenConfigFolder) { Margin = new Padding(0, 0, 14, 0) },
            new ThemedLink("Sound settings", () => Open("ms-settings:sound")));
        info.Anchor = AnchorStyles.Left;
        footer.Controls.Add(info, 0, 0);

        var quit = new FluentButton("Quit AudioSwap", "");
        var done = new FluentButton("Done", kind: FluentButton.Kind.Accent);
        quit.Click += (_, _) => { FlushSave(); _host.Quit(); };
        done.Click += (_, _) => Close();
        footer.Controls.Add(MakeFlow(8, quit, done), 1, 0);
        return footer;
    }

    static void AddSpanning(TableLayoutPanel grid, Control control, int row)
    {
        grid.Controls.Add(control, 0, row);
        grid.SetColumnSpan(control, grid.ColumnCount);
    }

    static void AddRow(TableLayoutPanel grid, string label, Control field)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (label.Length > 0)
            grid.Controls.Add(new ThemedLabel(label) { Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 20, 6) }, 0, row);
        if (field.Margin == new Padding(3)) field.Margin = new Padding(0, 5, 0, 5);
        grid.Controls.Add(field, 1, row);
    }

    static TableLayoutPanel MakeGrid(int columns) => new() { ColumnCount = columns, AutoSize = true, Margin = Padding.Empty };

    static FlowLayoutPanel MakeFlow(int gap, params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 5, 0, 5), Anchor = AnchorStyles.Left };
        for (int i = 0; i < controls.Length; i++)
        {
            var c = controls[i];
            if (c.Margin == new Padding(3)) c.Margin = new Padding(0, 0, i < controls.Length - 1 ? gap : 0, 0);
            if (c is Label) c.Anchor = AnchorStyles.Left;
        }
        flow.Controls.AddRange(controls);
        return flow;
    }

    static ThemedLabel MakeSection(string text) =>
        new(text, ThemedLabel.Style.Heading) { Margin = new Padding(2, 8, 0, 8) };

    #endregion

    #region Behaviour

    void WireEvents()
    {
        _list.MeasureItem += (_, e) => e.ItemHeight = Math.Min(255, _list.Font.Height * 3 + LogicalToDeviceUnits(22));
        _list.DrawItem += DrawProfile;
        _list.SelectedIndexChanged += (_, _) => LoadEditor();
        _add.Click += (_, _) => AddProfile(new Profile { Name = NextName() });
        _copy.Click += (_, _) =>
        {
            if (Selected is { } p)
                AddProfile(new Profile { Name = $"{p.Name} copy", Output = p.Output, Input = p.Input, IncludeCommunications = p.IncludeCommunications });
        };
        _remove.Click += (_, _) => RemoveSelected();
        _up.Click += (_, _) => MoveSelected(-1);
        _down.Click += (_, _) => MoveSelected(1);

        _name.TextChanged += (_, _) => Edit(p => p.Name = _name.Text);
        _hotkey.HotkeyChanged += (_, _) => Edit(p =>
        {
            p.Hotkey = _hotkey.Hotkey;
            CheckHotkey(p, p.Hotkey);
        });
        _hotkeyFrame.Cleared += (_, _) => _hotkey.Hotkey = null;
        _output.SelectedIndexChanged += (_, _) => Edit(p => p.Output = _output.SelectedItem?.Value as DeviceRef);
        _input.SelectedIndexChanged += (_, _) => Edit(p => p.Input = _input.SelectedItem?.Value as DeviceRef);
        _comms.CheckedChanged += (_, _) => Edit(p => p.IncludeCommunications = _comms.Checked);
        _try.Click += (_, _) =>
        {
            if (Selected is not { } p) return;
            FlushSave();
            _host.TryProfile(p);
        };

        _popupMode.SelectedIndexChanged += (_, _) => EditGeneral(c => c.PopupMode = (PopupMode)_popupMode.SelectedItem!.Value!);
        _position.SelectedIndexChanged += (_, _) => EditGeneral(c => c.PopupPosition = (PopupPosition)_position.SelectedItem!.Value!);
        _seconds.SelectedIndexChanged += (_, _) => EditGeneral(c => c.PopupSeconds = (double)_seconds.SelectedItem!.Value!);
        _preview.Click += (_, _) =>
        {
            FlushSave();
            _host.PreviewPopup(_config, _previewProblem);
            _previewProblem = !_previewProblem;
        };
        _cycle.HotkeyChanged += (_, _) => EditGeneral(c =>
        {
            c.CycleHotkey = _cycle.Hotkey;
            CheckHotkey(CycleKey, c.CycleHotkey);
        });
        _cycleFrame.Cleared += (_, _) => _cycle.Hotkey = null;
        _startup.CheckedChanged += (_, _) =>
        {
            if (!_loading) StartupManager.SetEnabled(_startup.Checked);
        };
        _showTray.CheckedChanged += (_, _) =>
        {
            _minimizeToTray.Enabled = _showTray.Checked;
            EditGeneral(c => c.ShowTrayIcon = _showTray.Checked);
        };
        _minimizeToTray.CheckedChanged += (_, _) => EditGeneral(c => c.MinimizeToTray = _minimizeToTray.Checked);

        foreach (var box in new[] { _hotkey, _cycle })
        {
            box.Enter += (_, _) => BeginInvoke(UpdateCapture);
            box.Leave += (_, _) => BeginInvoke(UpdateCapture);
        }
        Activated += (_, _) =>
        {
            _active = true;
            UpdateCapture();
            // The tray menu can toggle this while the window is open.
            _loading = true;
            _startup.Checked = StartupManager.IsEnabled;
            _loading = false;
        };
        Deactivate += (_, _) =>
        {
            _active = false;
            UpdateCapture();
        };
        _saveTimer.Tick += (_, _) => FlushSave();
    }

    void LoadGeneral()
    {
        _loading = true;
        _popupMode.SetItems(
        [
            new(PopupMode.Always, "Always"),
            new(PopupMode.ProblemsOnly, "Only when a device is missing"),
            new(PopupMode.Never, "Never"),
        ], (int)_config.PopupMode);
        _position.SetItems(
        [
            new(PopupPosition.BottomRight, "Bottom right"),
            new(PopupPosition.BottomCenter, "Bottom center"),
            new(PopupPosition.TopRight, "Top right"),
            new(PopupPosition.TopCenter, "Top center"),
            new(PopupPosition.Center, "Center"),
        ], (int)_config.PopupPosition);

        var seconds = Durations.Contains(_config.PopupSeconds) ? Durations : [.. Durations, _config.PopupSeconds];
        Array.Sort(seconds);
        _seconds.SetItems(seconds.Select(s => new DropDownItem(s, $"{s:0.#} seconds")), Array.IndexOf(seconds, _config.PopupSeconds));

        _cycle.Hotkey = _config.CycleHotkey;
        _startup.Checked = StartupManager.IsEnabled;
        _showTray.Checked = _config.ShowTrayIcon;
        _minimizeToTray.Checked = _config.MinimizeToTray;
        _minimizeToTray.Enabled = _config.ShowTrayIcon;
        _loading = false;
    }

    void LoadEditor()
    {
        var p = Selected;
        _loading = true;
        _editor.Enabled = p is not null;
        _name.Text = p?.Name ?? "";
        _hotkey.Hotkey = p?.Hotkey;
        _comms.Checked = p?.IncludeCommunications ?? true;
        _loading = false;
        LoadDeviceLists();
        UpdateStatus();
        int index = _list.SelectedIndex;
        _copy.Enabled = _remove.Enabled = p is not null;
        _up.Enabled = index > 0;
        _down.Enabled = index >= 0 && index < _list.Items.Count - 1;
    }

    void LoadDeviceLists()
    {
        FillDevices(_output, _outputs, Selected?.Output);
        FillDevices(_input, _inputs, Selected?.Input);
    }

    static void FillDevices(DropDown list, IReadOnlyList<AudioDevice> devices, DeviceRef? current)
    {
        var items = new List<DropDownItem> { new(null, "No change") };
        int selected = 0;
        var match = AudioDeviceService.Resolve(current, devices);
        foreach (var device in devices)
        {
            if (device.Id == match?.Id) selected = items.Count;
            items.Add(new DropDownItem(new DeviceRef(device.Id, device.Name), device.Name));
        }
        if (current is not null && match is null)
        {
            selected = items.Count;
            items.Add(new DropDownItem(current, $"{current.Name} (not connected)", IsWarning: true));
        }
        list.SetItems(items, selected);
    }

    void Edit(Action<Profile> change)
    {
        if (_loading || Selected is not { } profile) return;
        change(profile);
        _list.Invalidate();
        QueueSave();
    }

    void EditGeneral(Action<AppConfig> change)
    {
        if (_loading) return;
        change(_config);
        QueueSave();
    }

    void AddProfile(Profile profile)
    {
        int index = _list.SelectedIndex < 0 ? _list.Items.Count : _list.SelectedIndex + 1;
        _config.Profiles.Insert(index, profile);
        _list.Items.Insert(index, profile);
        _list.SelectedIndex = index;
        QueueSave();
        _name.Focus();
        _name.SelectAll();
    }

    void RemoveSelected()
    {
        if (Selected is not { } profile) return;
        if (!ConfirmDialog.Ask(this, $"Remove “{DisplayName(profile)}”?",
                "Its hotkey stops working right away. This can't be undone.", "Remove"))
            return;

        int index = _list.SelectedIndex;
        _config.Profiles.RemoveAt(index);
        _list.Items.RemoveAt(index);
        _problems.Remove(profile);
        if (_list.Items.Count > 0) _list.SelectedIndex = Math.Min(index, _list.Items.Count - 1);
        else LoadEditor();
        QueueSave();
    }

    void MoveSelected(int delta)
    {
        int index = _list.SelectedIndex, target = index + delta;
        if (index < 0 || target < 0 || target >= _list.Items.Count) return;
        var profile = _config.Profiles[index];
        _config.Profiles.RemoveAt(index);
        _config.Profiles.Insert(target, profile);
        _list.Items.RemoveAt(index);
        _list.Items.Insert(target, profile);
        _list.SelectedIndex = target;
        QueueSave();
    }

    string NextName()
    {
        for (int n = _config.Profiles.Count + 1; ; n++)
        {
            var name = $"Profile {n}";
            if (!_config.Profiles.Any(p => p.Name == name)) return name;
        }
    }

    static string DisplayName(Profile p) => string.IsNullOrWhiteSpace(p.Name) ? "Untitled" : p.Name;

    #endregion

    #region Hotkey capture and problems

    // While a hotkey box has focus in the active window, the app's hotkeys are released
    // so pressing an existing combo records it instead of switching audio.
    void UpdateCapture()
    {
        if (IsDisposed) return;
        bool capturing = _active && ActiveControl is HotkeyBox;
        if (capturing == _capturing) return;
        if (!capturing) FlushSave();
        _capturing = capturing;
        _host.SuspendHotkeys(capturing);
    }

    void CheckHotkey(object owner, Hotkey? hotkey)
    {
        _problems.Remove(owner);
        if (hotkey is not null)
        {
            var clash = _config.Profiles.FirstOrDefault(p => p != owner && p.Hotkey == hotkey);
            if (clash is not null) _problems[owner] = $"Also used by “{DisplayName(clash)}”";
            else if (owner != CycleKey && _config.CycleHotkey == hotkey) _problems[owner] = "Also used by the cycle hotkey";
            else if (_capturing && !_host.IsHotkeyFree(hotkey)) _problems[owner] = "Taken by another app";
        }
        UpdateStatus();
        _list.Invalidate();
    }

    void OnHostProblemsChanged(object? sender, EventArgs e)
    {
        if (_capturing) return; // the box's own check is fresher while recording
        _problems.Clear();
        foreach (var (index, message) in _host.HotkeyProblems)
        {
            if (index == -1) _problems[CycleKey] = message;
            else if (index < _config.Profiles.Count) _problems[_config.Profiles[index]] = message;
        }
        UpdateStatus();
        _list.Invalidate();
    }

    void UpdateStatus()
    {
        _hotkeyStatus.Text = Selected is { } p && _problems.TryGetValue(p, out var message) ? message : "";
        _cycleStatus.Text = _problems.TryGetValue(CycleKey, out var cycle) ? cycle : "";
    }

    #endregion

    #region Saving

    void QueueSave()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void FlushSave()
    {
        _saveTimer.Stop();
        if (!_dirty) return;
        _dirty = false;
        _host.Save(_config);
    }

    #endregion

    #region Profile list drawing

    void DrawProfile(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _list.Items.Count) return;
        var p = Theme.Current;
        var g = e.Graphics;
        var profile = (Profile)_list.Items[e.Index];
        bool selected = e.State.HasFlag(DrawItemState.Selected);
        bool hover = e.Index == _list.HoverIndex;

        using (var background = new SolidBrush(p.Card)) g.FillRectangle(background, e.Bounds);
        var row = Rectangle.Inflate(e.Bounds, -LogicalToDeviceUnits(2), -LogicalToDeviceUnits(2));
        var fill = selected ? p.Subtle : hover ? p.SubtleHover : p.Card;
        if (selected || hover) Draw.Rounded(g, row, LogicalToDeviceUnits(4), fill);
        if (selected)
        {
            int pill = LogicalToDeviceUnits(16);
            Draw.Rounded(g, new Rectangle(row.X, row.Y + (row.Height - pill) / 2, LogicalToDeviceUnits(3), pill), 1.5f, p.Accent);
        }

        // Three lines: name and hotkey, then the output, then the input.
        int pad = LogicalToDeviceUnits(14);
        var content = new Rectangle(row.X + pad, row.Y + LogicalToDeviceUnits(8), row.Width - pad - LogicalToDeviceUnits(10), row.Height - LogicalToDeviceUnits(16));
        int line = content.Height / 3;
        var text = selected ? p.SubtleText : p.Text;

        int capsWidth = DrawKeycaps(g, profile.Hotkey, _problems.ContainsKey(profile), new Rectangle(content.X, content.Y, content.Width, line), fill);
        TextRenderer.DrawText(g, DisplayName(profile), _itemTitleFont,
            new Rectangle(content.X, content.Y, content.Width - capsWidth - LogicalToDeviceUnits(10), line), text, fill, ItemText);
        DrawDevice(g, Glyphs.Speaker, profile.Output, _outputs, new Rectangle(content.X, content.Y + line, content.Width, line), fill);
        DrawDevice(g, Glyphs.Microphone, profile.Input, _inputs, new Rectangle(content.X, content.Y + line * 2, content.Width, line), fill);
    }

    void DrawDevice(Graphics g, string glyph, DeviceRef? device, IReadOnlyList<AudioDevice> connected, Rectangle bounds, Color back)
    {
        var p = Theme.Current;
        bool missing = device is not null && AudioDeviceService.Resolve(device, connected) is null;
        var color = missing ? p.Warning : p.TextSecondary;
        int icon = LogicalToDeviceUnits(18);
        Draw.Glyph(g, missing ? Glyphs.Warning : glyph, 8.5f, color, new Rectangle(bounds.X, bounds.Y, icon, bounds.Height));
        TextRenderer.DrawText(g, device?.Name ?? "No change", Font,
            new Rectangle(bounds.X + icon + LogicalToDeviceUnits(4), bounds.Y, bounds.Width - icon - LogicalToDeviceUnits(4), bounds.Height),
            color, back, ItemText);
    }

    /// <summary>Draws the hotkey as keycaps, right-aligned. Returns the width used.</summary>
    int DrawKeycaps(Graphics g, Hotkey? hotkey, bool problem, Rectangle bounds, Color back)
    {
        var p = Theme.Current;
        if (hotkey is null)
        {
            const string none = "No hotkey";
            int w = TextRenderer.MeasureText(g, none, _keycapFont, Size.Empty, ItemText).Width;
            TextRenderer.DrawText(g, none, _keycapFont, new Rectangle(bounds.Right - w, bounds.Y, w, bounds.Height), p.TextSecondary, back, ItemText);
            return w;
        }

        var keys = hotkey.ToDisplayString().Split(" + ");
        int padX = LogicalToDeviceUnits(6), gap = LogicalToDeviceUnits(3);
        int height = Math.Min(bounds.Height, LogicalToDeviceUnits(20));
        int y = bounds.Y + (bounds.Height - height) / 2;
        int x = bounds.Right;
        for (int i = keys.Length - 1; i >= 0; i--)
        {
            int w = TextRenderer.MeasureText(g, keys[i], _keycapFont, Size.Empty, ItemText).Width + padX * 2;
            x -= w;
            var cap = new Rectangle(x, y, w, height);
            Draw.Rounded(g, cap, LogicalToDeviceUnits(4), p.Control, problem ? p.Danger : p.ControlBorder);
            TextRenderer.DrawText(g, keys[i], _keycapFont, cap, problem ? p.Danger : p.TextSecondary, p.Control,
                ItemText | TextFormatFlags.HorizontalCenter);
            x -= gap;
        }
        return bounds.Right - x;
    }

    #endregion

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && _config.MinimizeToTray && _config.ShowTrayIcon) Hide();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        FlushSave();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _host.HotkeyProblemsChanged -= OnHostProblemsChanged;
            _saveTimer.Dispose();
            _itemTitleFont.Dispose();
            _keycapFont.Dispose();
        }
        base.Dispose(disposing);
    }

    static void OpenConfigFolder()
    {
        Directory.CreateDirectory(ConfigStore.Folder);
        Open(ConfigStore.Folder);
    }

    static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
