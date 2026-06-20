using System;
using System.IO;
using System.Media;
using System.Text;

namespace Blindfire.Audio;

// Synthesizes a whole grab-bag of short, goofy sound effects in memory (no
// external assets to ship) and plays one asynchronously on every click so it
// never blocks the click-handling path.
//
// Every call plays a *different* effect: a shuffle-bag hands out each sound
// once in a random order before any of them repeats, and we avoid replaying
// the same sound twice across the bag boundary - so it always feels fresh.
public static class ClickSoundPlayer
{
    private const int SampleRate = 44100;

    // All effects are rendered to WAV bytes once at startup; playback just
    // wraps the cached bytes in a fresh stream/player.
    private static readonly byte[][] Sounds = BuildAllSounds();
    private static readonly byte[] CalmBeepBytes = BuildWavFile(CalmBeep(), SampleRate);

    private static readonly Random Rng = new();
    private static readonly object Gate = new();
    private static int[] _bag = Array.Empty<int>();
    private static int _bagPosition;
    private static int _lastPlayed = -1;

    // When false, every click plays the same steady beep instead of a random effect.
    public static bool RandomSoundsEnabled { get; set; } = true;

    public static void PlayClick()
    {
        try
        {
            var bytes = RandomSoundsEnabled ? Sounds[NextSoundIndex()] : CalmBeepBytes;
            var stream = new MemoryStream(bytes);
            var player = new SoundPlayer(stream);
            player.Play();
        }
        catch
        {
            // Audio is best-effort feedback; never let playback issues break the trial flow.
        }
    }

    // Steady, quiet sine beep - no pitch sweep, no noise - used when random click sounds are off.
    private static short[] CalmBeep() => Render(0.12, _ => 660.0, Sine, t => Bell(t, 0.12), 0.45);

    // Shuffle-bag: draw each sound once per shuffle so the user hears variety
    // instead of streaks of the same effect. When the bag empties we reshuffle,
    // nudging the new first pick if it would repeat the previous one.
    private static int NextSoundIndex()
    {
        lock (Gate)
        {
            if (_bagPosition >= _bag.Length)
            {
                _bag = FreshBag(Sounds.Length);
                _bagPosition = 0;
                if (_bag.Length > 1 && _bag[0] == _lastPlayed)
                {
                    (_bag[0], _bag[1]) = (_bag[1], _bag[0]);
                }
            }

            var index = _bag[_bagPosition++];
            _lastPlayed = index;
            return index;
        }
    }

    private static int[] FreshBag(int count)
    {
        var order = new int[count];
        for (var i = 0; i < count; i++)
        {
            order[i] = i;
        }

        for (var i = count - 1; i > 0; i--)
        {
            var j = Rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }

    private static byte[][] BuildAllSounds()
    {
        var generators = new Func<short[]>[]
        {
            SpaceLaserPew,
            RisingZap,
            Fart,
            WetRaspberry,
            Boing,
            Coin,
            Pop,
            Blip,
            UfoWobble,
            SadTrombone,
            SlideWhistleUp,
            SlideWhistleDown,
            EightBitJump,
            Explosion,
            ClownHonk,
        };

        var result = new byte[generators.Length][];
        for (var i = 0; i < generators.Length; i++)
        {
            result[i] = BuildWavFile(generators[i](), SampleRate);
        }

        return result;
    }

    // ============================================================
    // Tone-based effects (pitched waveforms with an amplitude envelope)
    // ============================================================

    // Classic arcade "pew" - a square wave swept sharply downward.
    private static short[] SpaceLaserPew() =>
        Render(0.28, t => 1800 - (1500 * (t / 0.28)), Square, t => Math.Exp(-7 * t), 0.6);

    // Rising sci-fi zap - sawtooth swept upward.
    private static short[] RisingZap() =>
        Render(0.26, t => 300 + (2000 * (t / 0.26)), Saw, t => Math.Exp(-6 * t), 0.55);

    // Cartoon spring - a sine that drops in pitch with a little wobble.
    private static short[] Boing() =>
        Render(
            0.40,
            t => 180 + (520 * Math.Exp(-5 * t) * (1 + (0.15 * Math.Sin(2 * Math.PI * 22 * t)))),
            Sine,
            t => Math.Exp(-4 * t),
            0.8);

    // Mario-ish coin: a quick low note that jumps to a sustained higher one.
    private static short[] Coin() =>
        Render(
            0.5,
            t => t < 0.07 ? 988.0 : 1319.0,
            Square,
            t => t < 0.07 ? 1.0 : Math.Exp(-5 * (t - 0.07)),
            0.5);

    // Quick rising "bloop" bubble.
    private static short[] Blip() =>
        Render(0.12, t => 500 + (900 * (t / 0.12)), Sine, t => Math.Min(1, t / 0.01) * Math.Exp(-12 * t), 0.7);

    // Hovering UFO - vibrato sine under a soft bell-shaped fade in/out.
    private static short[] UfoWobble() =>
        Render(0.55, t => 650 + (220 * Math.Sin(2 * Math.PI * 11 * t)), Sine, t => Bell(t, 0.55), 0.6);

    // "Womp womp womp" - four descending sawtooth notes.
    private static short[] SadTrombone()
    {
        double[] notes = { 294, 262, 247, 220 };
        const double noteDuration = 0.18;
        var total = notes.Length * noteDuration;

        return Render(
            total,
            t =>
            {
                var idx = Math.Min(notes.Length - 1, (int)(t / noteDuration));
                var local = t - (idx * noteDuration);
                return (notes[idx] * (1 - (0.03 * (local / noteDuration)))) + (4 * Math.Sin(2 * Math.PI * 6 * t));
            },
            Saw,
            t =>
            {
                var local = t - ((int)(t / noteDuration) * noteDuration);
                return Math.Min(1, local / 0.01) * Math.Exp(-2.0 * local) * 0.9;
            },
            0.6);
    }

    // Slide whistle gliding up (exponential pitch sweep).
    private static short[] SlideWhistleUp() =>
        Render(0.3, t => 400 * Math.Pow(5, t / 0.3), Sine, t => Bell(t, 0.3), 0.6);

    // Slide whistle gliding down.
    private static short[] SlideWhistleDown() =>
        Render(0.3, t => 2000 * Math.Pow(0.2, t / 0.3), Sine, t => Bell(t, 0.3), 0.6);

    // 8-bit "jump" - a square wave with a fast upward pitch bend.
    private static short[] EightBitJump() =>
        Render(0.22, t => 380 + (700 * (t / 0.22)), Square, t => Math.Exp(-4 * t), 0.5);

    // Clown horn - two honks dropping in pitch.
    private static short[] ClownHonk() =>
        Render(
            0.42,
            t => t < 0.2 ? 262.0 : 196.0,
            Square,
            t =>
            {
                var local = t < 0.2 ? t : t - 0.2;
                return Math.Min(1, local / 0.01) * Math.Exp(-3 * local) * 0.9;
            },
            0.5);

    // ============================================================
    // Noise-based effects (rendered with bespoke loops)
    // ============================================================

    // Tiny punchy noise burst.
    private static short[] Pop()
    {
        const double duration = 0.08;
        var n = (int)(SampleRate * duration);
        var samples = new short[n];
        var random = new Random(7);

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var noise = (random.NextDouble() * 2) - 1;
            samples[i] = ToSample(noise * Math.Exp(-45 * t) * 0.85);
        }

        return samples;
    }

    // The crowd-pleaser: a low, buzzy sawtooth with vibrato + a tremolo
    // flutter and a dusting of noise.
    private static short[] Fart()
    {
        const double duration = 0.55;
        var n = (int)(SampleRate * duration);
        var samples = new short[n];
        var random = new Random(11);
        var phase = 0.0;

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var freq = 125 - (45 * (t / duration)) + (16 * Math.Sin(2 * Math.PI * 15 * t));
            phase += freq / SampleRate;

            var saw = (2 * Frac(phase)) - 1;
            var tremolo = 0.7 + (0.3 * Math.Sin(2 * Math.PI * 15 * t));
            var envelope = Math.Min(1, t / 0.02) * Math.Exp(-1.8 * t);
            var noise = ((random.NextDouble() * 2) - 1) * 0.12;

            samples[i] = ToSample(((saw * 0.85) + noise) * tremolo * envelope * 0.85);
        }

        return samples;
    }

    // Lower, wetter cousin of the fart with a hard amplitude chop ("blblbl").
    private static short[] WetRaspberry()
    {
        const double duration = 0.45;
        var n = (int)(SampleRate * duration);
        var samples = new short[n];
        var random = new Random(23);
        var phase = 0.0;

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var freq = 80 - (18 * (t / duration)) + (8 * Math.Sin(2 * Math.PI * 8 * t));
            phase += freq / SampleRate;

            var saw = (2 * Frac(phase)) - 1;
            var chop = Frac(t * 32) < 0.5 ? 1.0 : 0.35;
            var envelope = Math.Min(1, t / 0.02) * Math.Exp(-2.2 * t);
            var noise = ((random.NextDouble() * 2) - 1) * 0.18;

            samples[i] = ToSample(((saw * 0.8) + noise) * chop * envelope * 0.85);
        }

        return samples;
    }

    // Muffled boom: low-passed noise crackle over a low sine rumble.
    private static short[] Explosion()
    {
        const double duration = 0.6;
        var n = (int)(SampleRate * duration);
        var samples = new short[n];
        var random = new Random(31);
        var lowPass = 0.0;

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var white = (random.NextDouble() * 2) - 1;
            lowPass += 0.12 * (white - lowPass);

            var rumble = Math.Sin(2 * Math.PI * 55 * t);
            var envelope = Math.Exp(-4.5 * t);

            samples[i] = ToSample(((lowPass * 1.6 * 0.7) + (rumble * 0.5)) * envelope * 0.9);
        }

        return samples;
    }

    // ============================================================
    // Synthesis helpers
    // ============================================================

    // Renders a pitched tone: integrates the (possibly time-varying) frequency
    // into a phase so sweeps stay click-free, shaping each sample by waveform,
    // amplitude envelope, and overall gain.
    private static short[] Render(
        double seconds,
        Func<double, double> frequency,
        Func<double, double> waveform,
        Func<double, double> envelope,
        double gain)
    {
        var n = (int)(SampleRate * seconds);
        var samples = new short[n];
        var phase = 0.0;

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            phase += frequency(t) / SampleRate;
            samples[i] = ToSample(waveform(phase) * envelope(t) * gain);
        }

        return samples;
    }

    private static double Frac(double x) => x - Math.Floor(x);

    private static double Sine(double phase) => Math.Sin(2 * Math.PI * phase);

    private static double Square(double phase) => Frac(phase) < 0.5 ? 1.0 : -1.0;

    private static double Saw(double phase) => (2 * Frac(phase)) - 1;

    // Smooth fade in and out (half-sine), zero at both ends.
    private static double Bell(double t, double duration) => Math.Sin(Math.PI * Math.Min(1, t / duration));

    private static short ToSample(double value) => (short)(Math.Clamp(value, -1.0, 1.0) * short.MaxValue);

    private static byte[] BuildWavFile(short[] samples, int sampleRate)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        const int bitsPerSample = 16;
        const int channels = 1;
        var byteRate = sampleRate * channels * (bitsPerSample / 8);
        var dataSize = samples.Length * (bitsPerSample / 8);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write((short)bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
