using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using AudioSwap.Audio;
using AudioSwap.Config;
using AudioSwap.Hotkeys;
using AudioSwap.UI;
using AudioSwap.UI.Controls;

namespace AudioSwap;

/// <summary>
/// Command-line helpers for troubleshooting and for producing screenshots. None of
/// these change your audio setup. The render commands take an optional light|dark.
///   --list-devices &lt;file&gt;   write connected devices and current defaults
///   --self-test &lt;file&gt;      same, plus re-apply the current defaults (a no-op) to prove switching works
///   --render-popups &lt;file&gt;  render sample popups to a PNG
///   --render-window &lt;file&gt;  render the settings window to a PNG
///   --render-menu &lt;file&gt;    render a sample tray menu to a PNG
///   --list-displays &lt;file&gt;  write connected displays as the popup's Display setting names them
///   --render-sounds &lt;folder&gt; write the built-in sounds as WAV files, with a loudness report
///   --check-sound &lt;file&gt; &lt;sound&gt;  decode a sound (builtin:id or a file) and play it silently on the default output
/// </summary>
internal static class DevTools
{
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length < 2) return false;
        var path = Path.GetFullPath(args[1]);
        if (args.Length >= 3 && args[2].ToLowerInvariant() is "light" or "dark") Theme.Force(dark: args[2].Equals("dark", StringComparison.OrdinalIgnoreCase));
        switch (args[0].ToLowerInvariant())
        {
            case "--list-devices": exitCode = DeviceReport(path, selfTest: false); return true;
            case "--self-test": exitCode = DeviceReport(path, selfTest: true); return true;
            case "--render-popups": exitCode = RenderPopups(path); return true;
            case "--render-window": exitCode = RenderWindow(path); return true;
            case "--render-menu": exitCode = RenderMenu(path); return true;
            case "--list-displays": exitCode = ListDisplays(path); return true;
            case "--render-sounds": exitCode = RenderSounds(path); return true;
            case "--check-sound" when args.Length >= 3: exitCode = CheckSound(path, args[2]); return true;
            default: return false;
        }
    }

    static int DeviceReport(string path, bool selfTest)
    {
        using var audio = new AudioDeviceService();
        var report = new StringBuilder();
        int failures = 0;
        foreach (var flow in new[] { DeviceFlow.Output, DeviceFlow.Input })
        {
            report.AppendLine($"== {flow} ==");
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                report.AppendLine($"default ({role}): {audio.GetDefaultId(flow, role)}");
            foreach (var device in audio.GetDevices(flow))
                report.AppendLine($"  {device.Name}  [{device.Id}]");

            if (!selfTest) continue;
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            {
                var before = audio.GetDefaultId(flow, role);
                if (before is null) { report.AppendLine($"self-test {flow}/{role}: no default, skipped"); continue; }
                try
                {
                    audio.SetDefaultForRole(before, role);
                    var after = audio.GetDefaultId(flow, role);
                    bool ok = after == before;
                    if (!ok) failures++;
                    report.AppendLine($"self-test {flow}/{role}: {(ok ? "OK" : "CHANGED to " + after)}");
                }
                catch (Exception ex)
                {
                    failures++;
                    report.AppendLine($"self-test {flow}/{role}: FAILED {ex.Message}");
                }
            }
        }
        File.WriteAllText(path, report.ToString());
        return failures == 0 ? 0 : 1;
    }

    static int RenderPopups(string path)
    {
        OsdContent[] samples =
        [
            new("Gaming", null,
            [
                new(DeviceFlow.Output, OsdRowState.Switched, "Speakers (Realtek Audio)"),
                new(DeviceFlow.Input, OsdRowState.Switched, "Headset Microphone (USB Audio)"),
            ]),
            new("Webcam mic", null,
            [
                new(DeviceFlow.Output, OsdRowState.Unchanged, "Speakers (Realtek Audio)"),
                new(DeviceFlow.Input, OsdRowState.Switched, "Microphone (HD Webcam)"),
            ]),
            new("Headset", null,
            [
                new(DeviceFlow.Output, OsdRowState.Missing, "Headphones (USB Audio)"),
                new(DeviceFlow.Input, OsdRowState.Switched, "Headset Microphone (USB Audio)"),
            ]) { Warning = "Sound not found: wake-up.mp3" },
            OsdContent.Notice("Tray icon hidden", "Your hotkeys still work. Open AudioSwap again to bring the icon back."),
        ];

        float scale = 1.25f;
        var frames = samples.Select(s => OsdRenderer.Render(s, scale)).ToList();
        int gap = 8, width = frames.Max(f => f.Width) * 2 + gap * 3;
        int rowHeight = Math.Max(frames[0].Height, frames[1].Height);
        int height = rowHeight + Math.Max(frames[2].Height, frames[3].Height) + gap * 3;

        using var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(sheet))
        {
            using var wallpaper = Theme.Current.IsDark
                ? new LinearGradientBrush(new Rectangle(0, 0, width, height),
                    Color.FromArgb(38, 52, 92), Color.FromArgb(92, 62, 120), LinearGradientMode.ForwardDiagonal)
                : new LinearGradientBrush(new Rectangle(0, 0, width, height),
                    Color.FromArgb(186, 208, 240), Color.FromArgb(222, 200, 236), LinearGradientMode.ForwardDiagonal);
            g.FillRectangle(wallpaper, 0, 0, width, height);
            int half = frames.Max(f => f.Width);
            g.DrawImage(frames[0], gap, gap);
            g.DrawImage(frames[1], gap * 2 + half, gap);
            g.DrawImage(frames[2], gap, gap * 2 + rowHeight);
            g.DrawImage(frames[3], gap * 2 + half, gap * 2 + rowHeight);
        }
        sheet.Save(path, ImageFormat.Png);
        frames.ForEach(f => f.Dispose());
        return 0;
    }

    /// <summary>Renders the window's client area with made-up devices, so screenshots don't show real hardware.</summary>
    static int RenderWindow(string path)
    {
        static AudioDevice Device(string id, string name, DeviceFlow flow) => new($"{{sample}}.{{{id}}}", name, flow);
        AudioDevice[] outputs =
        [
            Device("1", "Headphones (USB Audio)", DeviceFlow.Output),
            Device("2", "Speakers (Realtek Audio)", DeviceFlow.Output),
            Device("3", "Monitor (NVIDIA High Definition Audio)", DeviceFlow.Output),
        ];
        AudioDevice[] inputs =
        [
            Device("4", "Headset Microphone (USB Audio)", DeviceFlow.Input),
            Device("5", "Microphone (Realtek Audio)", DeviceFlow.Input),
        ];
        DeviceRef Ref(AudioDevice d) => new(d.Id, d.Name);
        var config = new AppConfig
        {
            Profiles =
            {
                new Profile
                {
                    Name = "Desk speakers",
                    Hotkey = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Keys.D1),
                    Output = Ref(outputs[1]),
                    Input = Ref(inputs[1]),
                },
                new Profile
                {
                    Name = "Gaming",
                    Hotkey = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Keys.D2),
                    Output = Ref(outputs[0]),
                    Input = Ref(inputs[0]),
                },
                new Profile
                {
                    Name = "Webcam mic",
                    Hotkey = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Keys.D3),
                    Input = new DeviceRef("{sample}.{6}", "Microphone (HD Webcam)"),
                },
            },
            CycleHotkey = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Keys.Oemtilde),
            DeviceSounds = { new DeviceSound { Device = Ref(outputs[1]), Sound = BuiltInSounds.All[0].Ref } },
        };

        using var form = new SettingsForm(new NullHost(), flow => flow == DeviceFlow.Output ? outputs : inputs, config)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            ShowInTaskbar = false,
        };
        form.Show();
        Application.DoEvents();
        using var whole = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(whole, new Rectangle(Point.Empty, form.Size));
        var client = form.RectangleToScreen(form.ClientRectangle);
        client.Offset(-form.Left, -form.Top);
        using var bitmap = whole.Clone(client, whole.PixelFormat);
        bitmap.Save(path, ImageFormat.Png);
        form.Close();
        return 0;
    }

    static int RenderMenu(string path)
    {
        using var warning = Glyphs.Render(Glyphs.Warning, Color.FromArgb(214, 140, 0), 16);
        using var menu = new ContextMenuStrip();
        ThemedMenu.Apply(menu);
        menu.Items.Add(new ToolStripMenuItem("Desk") { ShortcutKeyDisplayString = "Ctrl + Alt + 1", Checked = true });
        menu.Items.Add(new ToolStripMenuItem("Headset") { ShortcutKeyDisplayString = "Ctrl + Alt + 2", Image = warning });
        menu.Items.Add(new ToolStripMenuItem("Webcam mic") { ShortcutKeyDisplayString = "Ctrl + Alt + 3" });
        menu.Items.Add(new ToolStripSeparator());
        var output = new ToolStripMenuItem("Output device");
        output.DropDownItems.Add("Speakers (Realtek Audio)");
        menu.Items.Add(output);
        var input = new ToolStripMenuItem("Input device");
        input.DropDownItems.Add("Microphone (HD Webcam)");
        menu.Items.Add(input);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open AudioSwap") { Font = Theme.CreateFont(9.75f, semibold: true) });
        menu.Items.Add(new ToolStripMenuItem("Sound settings"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Start with Windows") { Checked = true });
        menu.Items.Add(new ToolStripMenuItem("Hide tray icon"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit"));
        ThemedMenu.StyleItems(menu);

        _ = menu.Handle;
        menu.Size = menu.GetPreferredSize(Size.Empty);
        menu.PerformLayout();
        using var bitmap = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size));
        bitmap.Save(path, ImageFormat.Png);
        return 0;
    }

    static int ListDisplays(string path)
    {
        var report = new StringBuilder();
        foreach (var display in Displays.GetAll())
            report.AppendLine($"{display.Name}  {display.Screen.DeviceName}  {display.Screen.Bounds}  [{display.Id}]");
        File.WriteAllText(path, report.ToString());
        return 0;
    }

    /// <summary>Writes each built-in sound as a 16-bit WAV, with its loudness every quarter second.</summary>
    static int RenderSounds(string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new StringBuilder();
        foreach (var sound in BuiltInSounds.All)
        {
            var clip = BuiltInSounds.Render(sound);
            WriteWav(Path.Combine(folder, sound.Id + ".wav"), clip);
            report.AppendLine($"{sound.Name}: {clip.Seconds:0.00}s, peak {Db(clip.Samples.Max(MathF.Abs)):0.0} dBFS");
            report.AppendLine("  dB RMS per 0.25s: " + string.Join(" ", Loudness(clip, 0.25).Select(db => $"{db:0}")));
        }
        File.WriteAllText(Path.Combine(folder, "report.txt"), report.ToString());
        return 0;
    }

    /// <summary>Decodes a sound and plays it at zero volume: the whole path runs without making a sound.</summary>
    static int CheckSound(string path, string sound)
    {
        if (BuiltInSounds.Find(sound) is null) sound = Path.GetFullPath(sound);
        var report = new StringBuilder();
        int exitCode = 0;
        try
        {
            using var sounds = new SoundService();
            var watch = Stopwatch.StartNew();
            var clip = sounds.Load(sound);
            report.AppendLine($"decoded {SoundService.DisplayName(sound)}: {clip.SampleRate} Hz, {clip.Seconds:0.00}s, " +
                $"peak {Db(clip.Samples.Max(MathF.Abs)):0.0} dBFS, in {watch.ElapsedMilliseconds} ms");
            watch.Restart();
            SoundService.Render(clip, null, 0, CancellationToken.None);
            report.AppendLine($"played silently on the default output in {watch.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            exitCode = 1;
            report.AppendLine($"FAILED: {SoundService.Describe(ex)} ({ex.GetType().Name}: {ex.Message})");
        }
        File.WriteAllText(path, report.ToString());
        return exitCode;
    }

    static double Db(double amplitude) => 20 * Math.Log10(Math.Max(amplitude, 1e-6));

    static IEnumerable<double> Loudness(SoundClip clip, double window)
    {
        int size = (int)(clip.SampleRate * window) * 2;
        for (int start = 0; start < clip.Samples.Length; start += size)
        {
            int end = Math.Min(clip.Samples.Length, start + size);
            double sum = 0;
            for (int i = start; i < end; i++) sum += clip.Samples[i] * clip.Samples[i];
            yield return Db(Math.Sqrt(sum / (end - start)));
        }
    }

    static void WriteWav(string path, SoundClip clip)
    {
        using var writer = new BinaryWriter(File.Create(path));
        int dataBytes = clip.Samples.Length * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)2);
        writer.Write(clip.SampleRate);
        writer.Write(clip.SampleRate * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in clip.Samples) writer.Write((short)Math.Round(Math.Clamp(sample, -1f, 1f) * 32767));
    }

    sealed class NullHost : ISettingsHost
    {
        public IReadOnlyDictionary<int, string> HotkeyProblems { get; } = new Dictionary<int, string>();
        public event EventHandler? HotkeyProblemsChanged { add { } remove { } }
        public void Save(AppConfig config) { }
        public void SuspendHotkeys(bool suspend) { }
        public bool IsHotkeyFree(Hotkey hotkey) => true;
        public void TryProfile(Profile profile) { }
        public void PreviewPopup(AppConfig settings, bool withProblem) { }
        public void PreviewSound(DeviceSound sound) { }
        public void Quit() { }
    }
}
