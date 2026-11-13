using System.Runtime.InteropServices;
using static AudioSwap.Audio.MediaFoundation;

namespace AudioSwap.Audio;

/// <summary>Decoded audio ready to play: interleaved stereo float samples.</summary>
internal sealed record SoundClip(float[] Samples, int SampleRate)
{
    public double Seconds => Samples.Length / 2.0 / SampleRate;
}

/// <summary>
/// Decodes the first audio track of anything Windows can play (WAV, MP3, M4A/AAC, WMA,
/// FLAC, MP4 and other videos) with the Media Foundation source reader.
/// </summary>
internal static class MediaDecoder
{
    const uint FirstAudioStream = 0xFFFFFFFD, AllStreams = 0xFFFFFFFE;
    const uint ReaderError = 0x1, EndOfStream = 0x2;

    static readonly Guid MajorTypeKey = new("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
    static readonly Guid SubtypeKey = new("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
    static readonly Guid ChannelsKey = new("37E48BF5-645E-4C5B-89DE-ADA9E29B696A");
    static readonly Guid SampleRateKey = new("5FAEEAE7-0290-4C31-9E8A-C534F68D9DBA");
    static readonly Guid AudioMajorType = new("73647561-0000-0010-8000-00AA00389B71");
    static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00AA00389B71");

    /// <summary>Reads up to <paramref name="maxSeconds"/>; a longer file fades out there instead of stopping dead.</summary>
    public static SoundClip Decode(string path, int maxSeconds)
    {
        Marshal.ThrowExceptionForHR(MFStartup(ApiVersion, StartupLite));
        IMFSourceReader? reader = null;
        try
        {
            Marshal.ThrowExceptionForHR(MFCreateSourceReaderFromURL(path, IntPtr.Zero, out reader));
            Marshal.ThrowExceptionForHR(reader.SetStreamSelection(AllStreams, false));
            Marshal.ThrowExceptionForHR(reader.SetStreamSelection(FirstAudioStream, true));

            // Ask for float samples and leave the rate and channels as they are.
            Marshal.ThrowExceptionForHR(MFCreateMediaType(out var wanted));
            try
            {
                SetGuid(wanted, MajorTypeKey, AudioMajorType);
                SetGuid(wanted, SubtypeKey, FloatSubtype);
                Marshal.ThrowExceptionForHR(reader.SetCurrentMediaType(FirstAudioStream, IntPtr.Zero, wanted));
            }
            finally { Marshal.ReleaseComObject(wanted); }

            Marshal.ThrowExceptionForHR(reader.GetCurrentMediaType(FirstAudioStream, out var actual));
            int channels, rate;
            try
            {
                channels = (int)GetUInt32(actual, ChannelsKey);
                rate = (int)GetUInt32(actual, SampleRateKey);
            }
            finally { Marshal.ReleaseComObject(actual); }

            int maxFrames = rate * maxSeconds, frames = 0;
            var stereo = new float[maxFrames * 2];
            var block = Array.Empty<float>();
            while (frames < maxFrames)
            {
                Marshal.ThrowExceptionForHR(reader.ReadSample(FirstAudioStream, 0, out _, out uint flags, out _, out var sample));
                if (sample is not null)
                {
                    try { frames = Append(sample, channels, stereo, frames, maxFrames, ref block); }
                    finally { Marshal.ReleaseComObject(sample); }
                }
                if ((flags & ReaderError) != 0) throw new InvalidDataException("The file couldn't be read to the end.");
                if ((flags & EndOfStream) != 0) break;
            }
            if (frames == 0) throw new InvalidDataException("The file has no sound in it.");

            Array.Resize(ref stereo, frames * 2);
            Fade(stereo, rate, fadeOut: frames >= maxFrames);
            return new SoundClip(stereo, rate);
        }
        finally
        {
            if (reader is not null) Marshal.ReleaseComObject(reader);
            MFShutdown();
        }
    }

    /// <summary>Appends one decoded sample, folding mono up and surround down to stereo.</summary>
    static int Append(IMFSample sample, int channels, float[] stereo, int frames, int maxFrames, ref float[] block)
    {
        Marshal.ThrowExceptionForHR(sample.ConvertToContiguousBuffer(out var buffer));
        try
        {
            Marshal.ThrowExceptionForHR(buffer.Lock(out var data, out _, out int length));
            int count = length / sizeof(float);
            try
            {
                if (block.Length < count) block = new float[count];
                Marshal.Copy(data, block, 0, count);
            }
            finally { buffer.Unlock(); }

            for (int f = 0; f < count / channels && frames < maxFrames; f++, frames++)
            {
                int i = f * channels;
                float left = block[i], right = channels > 1 ? block[i + 1] : left;
                if (channels >= 6) // 5.1 and up: FL FR FC LFE SL SR...
                {
                    left += 0.7f * (block[i + 2] + block[i + 4]);
                    right += 0.7f * (block[i + 2] + block[i + 5]);
                }
                stereo[frames * 2] = left;
                stereo[frames * 2 + 1] = right;
            }
            return frames;
        }
        finally { Marshal.ReleaseComObject(buffer); }
    }

    static void Fade(float[] stereo, int rate, bool fadeOut)
    {
        int frames = stereo.Length / 2;
        int fadeIn = Math.Min(frames, rate / 200), tail = fadeOut ? Math.Min(frames, rate / 2) : 0;
        for (int f = 0; f < fadeIn; f++) Scale(f, (float)f / fadeIn);
        for (int f = 0; f < tail; f++) Scale(frames - 1 - f, (float)f / tail);

        void Scale(int frame, float gain)
        {
            stereo[frame * 2] *= gain;
            stereo[frame * 2 + 1] *= gain;
        }
    }

    static void SetGuid(IMFMediaType type, Guid key, Guid value) =>
        Marshal.ThrowExceptionForHR(type.SetGUID(ref key, ref value));

    static uint GetUInt32(IMFMediaType type, Guid key)
    {
        Marshal.ThrowExceptionForHR(type.GetUINT32(ref key, out uint value));
        return value;
    }
}
