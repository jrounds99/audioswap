using System.Diagnostics;
using System.Runtime.InteropServices;
using AudioSwap.Config;

namespace AudioSwap.Audio;

/// <summary>
/// Plays a profile's sound on a chosen output device: a built-in sound or an audio/video
/// file. Decoding and playback run on their own thread through WASAPI, so switching is
/// never held up, and a new sound cuts off the one before it.
/// </summary>
internal sealed class SoundService : IDisposable
{
    /// <summary>Longer files are cut here (with a fade). It's a wake-up sound, not a music player.</summary>
    public const int MaxSeconds = 10;

    const int SharedMode = 0;
    const uint AutoConvertPcm = 0x80000000, SrcDefaultQuality = 0x08000000, ClsctxAll = 0x17;
    const long BufferDuration = 2_000_000; // 200 ms, in 100 ns units

    readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    readonly Dictionary<string, (DateTime Stamp, SoundClip Clip)> _files = [];
    CancellationTokenSource? _playing;

    /// <summary>Raised on the UI thread when a sound couldn't be played: the sound and why.</summary>
    public event Action<string, string>? Failed;

    public static string DisplayName(string sound) => BuiltInSounds.Find(sound)?.Name ?? Path.GetFileName(sound);

    /// <summary>False for a file that's been moved or deleted.</summary>
    public static bool Exists(string sound) => BuiltInSounds.Find(sound) is not null || File.Exists(sound);

    /// <summary>Plays on the device, or on the default output when <paramref name="deviceId"/> is null.</summary>
    public void Play(string sound, string? deviceId, int volumePercent)
    {
        Stop();
        var playing = _playing = new CancellationTokenSource();
        // Squared, so the steps sound roughly even rather than all bunched at the top.
        float gain = MathF.Pow(Math.Clamp(volumePercent, 0, 100) / 100f, 2);
        new Thread(() => Run(sound, deviceId, gain, playing.Token)) { IsBackground = true, Name = "AudioSwap sound" }.Start();
    }

    public void Stop()
    {
        _playing?.Cancel();
        _playing = null;
    }

    /// <summary>Decodes (or synthesises) a sound without playing it; for checks and the dev tools.</summary>
    public SoundClip Load(string sound)
    {
        if (BuiltInSounds.Find(sound) is { } builtIn) return BuiltInSounds.Render(builtIn);
        if (!File.Exists(sound)) throw new FileNotFoundException("The file was moved or deleted.", sound);

        var stamp = File.GetLastWriteTimeUtc(sound);
        lock (_files)
        {
            if (_files.TryGetValue(sound, out var cached) && cached.Stamp == stamp) return cached.Clip;
        }
        var clip = MediaDecoder.Decode(sound, MaxSeconds);
        lock (_files)
        {
            if (_files.Count >= 8) _files.Clear();
            _files[sound] = (stamp, clip);
        }
        return clip;
    }

    /// <summary>Plays synchronously on the calling thread. Throws if the device can't be opened.</summary>
    public static void Render(SoundClip clip, string? deviceId, float gain, CancellationToken token)
    {
        // A fresh enumerator: COM objects from the UI thread can't be used from this one.
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCoClass();
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioRenderClient? render = null;
        try
        {
            Check(deviceId is null
                ? enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device)
                : enumerator.GetDevice(deviceId, out device));
            var clientId = typeof(IAudioClient).GUID;
            Check(device.Activate(ref clientId, ClsctxAll, IntPtr.Zero, out var activated));
            client = (IAudioClient)activated;

            // Shared mode, with Windows converting the rate and channels to whatever the device uses.
            var format = WaveFormatEx.StereoFloat(clip.SampleRate);
            Check(client.Initialize(SharedMode, AutoConvertPcm | SrcDefaultQuality, BufferDuration, 0, ref format, IntPtr.Zero));
            Check(client.GetBufferSize(out uint bufferFrames));
            var renderId = typeof(IAudioRenderClient).GUID;
            Check(client.GetService(ref renderId, out var service));
            render = (IAudioRenderClient)service;

            int frames = clip.Samples.Length / 2, position = 0;
            var chunk = new float[bufferFrames * 2];
            position += Fill(render, clip, position, (int)bufferFrames, gain, chunk);
            Check(client.Start());
            while (!token.IsCancellationRequested)
            {
                Thread.Sleep(15);
                Check(client.GetCurrentPadding(out uint queued));
                if (position < frames) position += Fill(render, clip, position, (int)(bufferFrames - queued), gain, chunk);
                else if (queued == 0) break;
            }
            client.Stop();
        }
        finally
        {
            foreach (var com in new object?[] { render, client, device, enumerator })
                if (com is not null) Marshal.ReleaseComObject(com);
        }
    }

    static int Fill(IAudioRenderClient render, SoundClip clip, int position, int room, float gain, float[] chunk)
    {
        int count = Math.Min(room, clip.Samples.Length / 2 - position);
        if (count <= 0) return 0;
        Check(render.GetBuffer((uint)count, out var data));
        for (int i = 0; i < count * 2; i++) chunk[i] = clip.Samples[position * 2 + i] * gain;
        Marshal.Copy(chunk, 0, data, count * 2);
        Check(render.ReleaseBuffer((uint)count, 0));
        return count;
    }

    void Run(string sound, string? deviceId, float gain, CancellationToken token)
    {
        try
        {
            var clip = Load(sound);
            if (token.IsCancellationRequested) return;
            var watch = Stopwatch.StartNew();
            Render(clip, deviceId, gain, token);
            AppLog.Write($"sound: {DisplayName(sound)} {(token.IsCancellationRequested ? "cut short by the next one" : "played")} " +
                $"({watch.Elapsed.TotalSeconds:0.0}s of {clip.Seconds:0.0}s, gain {gain:0.00})");
        }
        catch (Exception ex)
        {
            AppLog.Write($"sound: {DisplayName(sound)} failed: {Describe(ex)} ({ex.Message})");
            Trace.WriteLine($"AudioSwap: playing {sound} failed: {ex}");
            var reason = Describe(ex);
            _ui?.Post(_ => Failed?.Invoke(sound, reason), null);
        }
    }

    /// <summary>A plain-language reason for the common failures.</summary>
    public static string Describe(Exception ex) => (uint)ex.HResult switch
    {
        0x80070002 or 0x80070003 => "The file was moved or deleted.",
        0xC00D36C4 or 0xC00D36B3 or 0xC00D36B4 or 0xC00D5212 => "Windows can't play that kind of file.",
        0x8889000A => "Another app is using the output device exclusively.",
        0x88890004 => "The output device was unplugged.",
        _ when ex is FileNotFoundException or InvalidDataException => ex.Message,
        _ => $"Windows reported error 0x{ex.HResult:X8}.",
    };

    static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    public void Dispose() => Stop();
}
