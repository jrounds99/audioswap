using System.Collections.Concurrent;

namespace AudioSwap.Audio;

internal sealed record BuiltInSound(string Id, string Name)
{
    /// <summary>How a profile refers to it in config.json.</summary>
    public string Ref => BuiltInSounds.Prefix + Id;
}

/// <summary>
/// AudioSwap's own sounds, synthesised the first time they're played. Each is about 3.5
/// seconds of continuous, gentle sound, which is enough to wake speakers that only switch
/// on when they hear audio. The brighter part comes near the end, so it's what you hear
/// once they're on.
/// </summary>
internal static class BuiltInSounds
{
    public const string Prefix = "builtin:";
    const int Rate = 48000;
    const double Tau = Math.PI * 2;

    public static IReadOnlyList<BuiltInSound> All { get; } =
    [
        new("glow", "Glow"),
        new("chimes", "Wind chimes"),
        new("marimba", "Marimba"),
        new("breeze", "Breeze"),
    ];

    static readonly ConcurrentDictionary<string, Lazy<SoundClip>> Rendered = new();

    public static BuiltInSound? Find(string sound) =>
        All.FirstOrDefault(s => string.Equals(s.Ref, sound, StringComparison.OrdinalIgnoreCase));

    public static SoundClip Render(BuiltInSound sound) =>
        Rendered.GetOrAdd(sound.Id, id => new Lazy<SoundClip>(() => Synthesize(id))).Value;

    static SoundClip Synthesize(string id)
    {
        var mix = new Mix(3.6);
        switch (id)
        {
            case "glow":
                // A warm Fmaj9 pad that swells straight away, then two soft bells.
                foreach (var (note, pan) in new[] { (53, -0.45), (60, -0.15), (64, 0.1), (67, 0.3), (69, 0.5) })
                    mix.Pad(Note(note), start: 0, length: 3.3, attack: 0.25, release: 1.3, level: 0.1, pan);
                mix.Bell(Note(81), start: 2.05, level: 0.3, pan: -0.2);
                mix.Bell(Note(88), start: 2.3, level: 0.22, pan: 0.25);
                return mix.Finish(reverb: 0.28);

            case "chimes":
                // Pentatonic wind chimes scattered across the stereo field, over a quiet bed.
                var random = new Random(11);
                int[] scale = [84, 86, 88, 91, 93, 96];
                int last = -1;
                for (double t = 0; t < 2.9; t += 0.11 + random.NextDouble() * 0.22)
                {
                    int note;
                    do note = scale[random.Next(scale.Length)]; while (note == last);
                    last = note;
                    mix.Chime(Note(note), t, level: 0.1 + random.NextDouble() * 0.12, pan: random.NextDouble() * 1.4 - 0.7);
                }
                mix.Pad(Note(60), 0, 3.3, 0.4, 1.2, 0.035, -0.2);
                mix.Pad(Note(67), 0, 3.3, 0.4, 1.2, 0.03, 0.2);
                return mix.Finish(reverb: 0.4);

            case "marimba":
                // A lilting pentatonic line that lands on a rolled C major chord.
                (double Time, int Note)[] melody =
                    [(0, 67), (0.3, 72), (0.6, 76), (0.9, 79), (1.2, 76), (1.5, 81), (1.8, 79), (2.1, 76),
                     (2.4, 72), (2.425, 76), (2.45, 79), (2.475, 84)];
                for (int i = 0; i < melody.Length; i++)
                    mix.Marimba(Note(melody[i].Note), melody[i].Time, level: 0.32, pan: (i % 4 - 1.5) * 0.2);
                mix.Pad(Note(48), 0, 3.3, 0.3, 1.0, 0.05, -0.15);
                mix.Pad(Note(55), 0, 3.3, 0.3, 1.0, 0.04, 0.15);
                return mix.Finish(reverb: 0.25);

            default: // breeze
                // A soft wave of air over an A major pad, with one high chime at the crest.
                mix.Air(level: 0.35);
                foreach (var (note, pan) in new[] { (57, -0.3), (64, 0.0), (71, 0.25), (73, 0.4) })
                    mix.Pad(Note(note), 0, 3.3, 0.5, 1.2, 0.05, pan);
                mix.Chime(Note(81), 2.3, level: 0.12, pan: 0.2);
                return mix.Finish(reverb: 0.35);
        }
    }

    static double Note(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);

    static double Smooth(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    /// <summary>A stereo buffer that voices are added into, then reverb, levelling and a fade.</summary>
    sealed class Mix(double seconds)
    {
        const double TargetRms = 0.12, MaxPeak = 0.89;

        readonly float[] _left = new float[(int)(seconds * Rate)];
        readonly float[] _right = new float[(int)(seconds * Rate)];

        /// <summary>Equal-power pan: -1 is hard left, 1 hard right.</summary>
        void Add(double start, double length, double pan, Func<double, double> voice)
        {
            int from = (int)(start * Rate), to = Math.Min(_left.Length, from + (int)(length * Rate));
            double angle = (pan + 1) * Math.PI / 4;
            float gainLeft = (float)Math.Cos(angle), gainRight = (float)Math.Sin(angle);
            for (int i = from; i < to; i++)
            {
                float sample = (float)voice((i - from) / (double)Rate);
                _left[i] += sample * gainLeft;
                _right[i] += sample * gainRight;
            }
        }

        /// <summary>Three slightly detuned voices with a little harmonic warmth.</summary>
        public void Pad(double freq, double start, double length, double attack, double release, double level, double pan) =>
            Add(start, length, pan, t =>
            {
                double sum = 0;
                for (int v = 0; v < 3; v++)
                {
                    double f = freq * (1 + (v - 1) * 0.0023), phase = v * 2.1;
                    sum += Math.Sin(Tau * f * t + phase) + 0.25 * Math.Sin(2 * (Tau * f * t + phase)) + 0.08 * Math.Sin(3 * (Tau * f * t + phase));
                }
                return level * Smooth(t / attack) * Smooth((length - t) / release) * sum / 3;
            });

        /// <summary>A rounded glass bell: near-harmonic partials, the upper ones dying away first.</summary>
        public void Bell(double freq, double start, double level, double pan) =>
            Add(start, 3.5, pan, t => level * Smooth(t / 0.004) * (
                Math.Sin(Tau * freq * t) * Math.Exp(-t / 1.6)
                + 0.35 * Math.Sin(Tau * freq * 2 * t) * Math.Exp(-t / 0.9)
                + 0.12 * Math.Sin(Tau * freq * 3 * t) * Math.Exp(-t / 0.5)
                + 0.06 * Math.Sin(Tau * freq * 4.2 * t) * Math.Exp(-t / 0.3)));

        /// <summary>A hanging tube: the free-bar partials (1, 2.76, 5.4) and a slow shimmer.</summary>
        public void Chime(double freq, double start, double level, double pan) =>
            Add(start, 3.5, pan, t => level * Smooth(t / 0.003) * (
                (Math.Sin(Tau * freq * t) + 0.5 * Math.Sin(Tau * freq * 1.002 * t)) / 1.5 * Math.Exp(-t / 1.8)
                + 0.3 * Math.Sin(Tau * freq * 2.76 * t) * Math.Exp(-t / 0.7)
                + 0.1 * Math.Sin(Tau * freq * 5.4 * t) * Math.Exp(-t / 0.25)));

        /// <summary>A wooden bar: the fundamental plus the tuned overtone two octaves up.</summary>
        public void Marimba(double freq, double start, double level, double pan) =>
            Add(start, 2.0, pan, t => level * Smooth(t / 0.002) * (
                Math.Sin(Tau * freq * t) * Math.Exp(-t / 0.5)
                + 0.3 * Math.Sin(Tau * freq * 4 * t) * Math.Exp(-t / 0.08)
                + 0.05 * Math.Sin(Tau * freq * 9.9 * t) * Math.Exp(-t / 0.03)));

        /// <summary>Filtered noise that rises and falls like a wave; each side is its own noise, so it's wide.</summary>
        public void Air(double level)
        {
            var random = new Random(5);
            double length = 3.4;
            int count = Math.Min(_left.Length, (int)(length * Rate));
            double l1 = 0, l2 = 0, r1 = 0, r2 = 0, lowL = 0, lowR = 0;
            for (int i = 0; i < count; i++)
            {
                double t = i / (double)Rate;
                double swell = Math.Pow(Math.Sin(Math.PI * t / length), 2);
                double a = 1 - Math.Exp(-Tau * (350 + 1500 * swell) / Rate);
                double high = 1 - Math.Exp(-Tau * 140.0 / Rate);
                l1 += a * (random.NextDouble() * 2 - 1 - l1); l2 += a * (l1 - l2);
                r1 += a * (random.NextDouble() * 2 - 1 - r1); r2 += a * (r1 - r2);
                lowL += high * (l2 - lowL); lowR += high * (r2 - lowR);
                double env = level * (0.3 + 0.7 * swell) * Smooth(t / 0.15) * Smooth((length - t) / 0.4) * 4;
                _left[i] += (float)((l2 - lowL) * env);
                _right[i] += (float)((r2 - lowR) * env);
            }
        }

        public SoundClip Finish(double reverb)
        {
            Reverb(reverb);

            double sumSquares = 0, peak = 0;
            for (int i = 0; i < _left.Length; i++)
            {
                sumSquares += _left[i] * _left[i] + _right[i] * _right[i];
                peak = Math.Max(peak, Math.Max(Math.Abs(_left[i]), Math.Abs(_right[i])));
            }
            double rms = Math.Sqrt(sumSquares / (_left.Length * 2));
            float gain = (float)Math.Min(TargetRms / rms, MaxPeak / peak);

            int fade = Rate / 5;
            var samples = new float[_left.Length * 2];
            for (int i = 0; i < _left.Length; i++)
            {
                float tail = (float)Smooth((_left.Length - i) / (double)fade);
                samples[i * 2] = _left[i] * gain * tail;
                samples[i * 2 + 1] = _right[i] * gain * tail;
            }
            return new SoundClip(samples, Rate);
        }

        /// <summary>A small Freeverb: eight damped combs into four allpasses per side.</summary>
        void Reverb(double wet)
        {
            int[] combs = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617];
            int[] allpasses = [556, 441, 341, 225];
            var left = new Tank(combs, allpasses, spread: 0);
            var right = new Tank(combs, allpasses, spread: 23);
            float wetGain = (float)(wet * 3);
            for (int i = 0; i < _left.Length; i++)
            {
                float input = (_left[i] + _right[i]) * 0.015f;
                _left[i] += left.Process(input) * wetGain;
                _right[i] += right.Process(input) * wetGain;
            }
        }

        sealed class Tank
        {
            const float Feedback = 0.84f, Damp = 0.25f;
            readonly float[][] _combs, _allpasses;
            readonly int[] _combIndex, _allpassIndex;
            readonly float[] _store;

            public Tank(int[] combs, int[] allpasses, int spread)
            {
                double scale = Rate / 44100.0;
                _combs = combs.Select(n => new float[(int)((n + spread) * scale)]).ToArray();
                _allpasses = allpasses.Select(n => new float[(int)((n + spread) * scale)]).ToArray();
                _combIndex = new int[combs.Length];
                _allpassIndex = new int[allpasses.Length];
                _store = new float[combs.Length];
            }

            public float Process(float input)
            {
                float output = 0;
                for (int c = 0; c < _combs.Length; c++)
                {
                    var buffer = _combs[c];
                    int i = _combIndex[c];
                    float delayed = buffer[i];
                    _store[c] = delayed * (1 - Damp) + _store[c] * Damp;
                    buffer[i] = input + _store[c] * Feedback;
                    _combIndex[c] = i + 1 == buffer.Length ? 0 : i + 1;
                    output += delayed;
                }
                for (int a = 0; a < _allpasses.Length; a++)
                {
                    var buffer = _allpasses[a];
                    int i = _allpassIndex[a];
                    float delayed = buffer[i];
                    buffer[i] = output + delayed * 0.5f;
                    output = delayed - output;
                    _allpassIndex[a] = i + 1 == buffer.Length ? 0 : i + 1;
                }
                return output;
            }
        }
    }
}
