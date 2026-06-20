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

    // Every effect is rendered to raw samples and then normalized to the same
    // peak loudness before being cached as WAV bytes - otherwise some effects
    // (e.g. layered noise ones) come out much louder than simple tones.
    private const double EffectPeakAmplitude = 0.85;
    private const double CalmBeepPeakAmplitude = 0.5;

    // Every effect is rendered and normalized to raw PCM samples once at
    // startup; playback rescales by the current volume and builds a fresh
    // WAV byte array each time, so volume changes apply instantly.
    private static readonly short[][] Sounds = BuildAllSounds();
    private static readonly short[] CalmBeepSamples = ToShortArray(Normalize(CalmBeep(), CalmBeepPeakAmplitude));

    private static readonly Random Rng = new();
    private static readonly object Gate = new();
    private static int[] _bag = Array.Empty<int>();
    private static int _bagPosition;
    private static int _lastPlayed = -1;

    // When false, every click plays the same steady beep instead of a random effect.
    public static bool RandomSoundsEnabled { get; set; }

    // 0.0 (silent) to 1.0 (full, normalized peak). Applied on every play, not baked into the cache.
    public static double Volume { get; set; } = 1.0;

    public static void PlayClick()
    {
        try
        {
            var samples = RandomSoundsEnabled ? Sounds[NextSoundIndex()] : CalmBeepSamples;
            var bytes = BuildWavFile(ApplyVolume(samples, Volume), SampleRate);
            var stream = new MemoryStream(bytes);
            var player = new SoundPlayer(stream);
            player.Play();
        }
        catch
        {
            // Audio is best-effort feedback; never let playback issues break the trial flow.
        }
    }

    private static short[] ApplyVolume(short[] samples, double volume)
    {
        var clampedVolume = Math.Clamp(volume, 0.0, 1.0);
        var result = new short[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            result[i] = (short)(samples[i] * clampedVolume);
        }

        return result;
    }

    // Steady sine beep - no pitch sweep, no noise - used when random click sounds are off.
    private static double[] CalmBeep() => Render(0.12, _ => 660.0, Sine, t => Bell(t, 0.12), 0.45);

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

    private static short[][] BuildAllSounds()
    {
        var generators = new Func<double[]>[]
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
            Arpeggio,
            ModemChirp,
            GlassTing,
            Cowbell,
            DuckQuack,
            CatMeow,
            DogBark,
            Whoosh,
            CameraClick,
            WoodBlock,
            SynthStab,
            Kazoo,
            PowerDown,
            Hiccup,
            Sparkle,
        };

        var result = new short[generators.Length][];
        for (var i = 0; i < generators.Length; i++)
        {
            result[i] = ToShortArray(Normalize(generators[i](), EffectPeakAmplitude));
        }

        return result;
    }

    // ============================================================
    // Tone-based effects (pitched waveforms with an amplitude envelope)
    // ============================================================

    // Classic arcade "pew" - a square wave swept sharply downward.
    private static double[] SpaceLaserPew() =>
        Render(0.28, t => 1800 - (1500 * (t / 0.28)), Square, t => Math.Exp(-7 * t), 0.6);

    // Rising sci-fi zap - sawtooth swept upward.
    private static double[] RisingZap() =>
        Render(0.26, t => 300 + (2000 * (t / 0.26)), Saw, t => Math.Exp(-6 * t), 0.55);

    // Cartoon spring - a sine that drops in pitch with a little wobble.
    private static double[] Boing() =>
        Render(
            0.40,
            t => 180 + (520 * Math.Exp(-5 * t) * (1 + (0.15 * Math.Sin(2 * Math.PI * 22 * t)))),
            Sine,
            t => Math.Exp(-4 * t),
            0.8);

    // Mario-ish coin: a quick low note that jumps to a sustained higher one.
    private static double[] Coin() =>
        Render(
            0.5,
            t => t < 0.07 ? 988.0 : 1319.0,
            Square,
            t => t < 0.07 ? 1.0 : Math.Exp(-5 * (t - 0.07)),
            0.5);

    // Quick rising "bloop" bubble.
    private static double[] Blip() =>
        Render(0.12, t => 500 + (900 * (t / 0.12)), Sine, t => Math.Min(1, t / 0.01) * Math.Exp(-12 * t), 0.7);

    // Hovering UFO - vibrato sine under a soft bell-shaped fade in/out.
    private static double[] UfoWobble() =>
        Render(0.55, t => 650 + (220 * Math.Sin(2 * Math.PI * 11 * t)), Sine, t => Bell(t, 0.55), 0.6);

    // "Womp womp womp" - four descending sawtooth notes.
    private static double[] SadTrombone()
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
    private static double[] SlideWhistleUp() =>
        Render(0.3, t => 400 * Math.Pow(5, t / 0.3), Sine, t => Bell(t, 0.3), 0.6);

    // Slide whistle gliding down.
    private static double[] SlideWhistleDown() =>
        Render(0.3, t => 2000 * Math.Pow(0.2, t / 0.3), Sine, t => Bell(t, 0.3), 0.6);

    // 8-bit "jump" - a square wave with a fast upward pitch bend.
    private static double[] EightBitJump() =>
        Render(0.22, t => 380 + (700 * (t / 0.22)), Square, t => Math.Exp(-4 * t), 0.5);

    // Clown horn - two honks dropping in pitch.
    private static double[] ClownHonk() =>
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

    // Three-note ascending arcade arpeggio.
    private static double[] Arpeggio()
    {
        double[] notes = { 392, 523, 659 };
        const double noteDuration = 0.09;
        var total = notes.Length * noteDuration;

        return Render(
            total,
            t => notes[Math.Min(notes.Length - 1, (int)(t / noteDuration))],
            Square,
            t =>
            {
                var local = t - ((int)(t / noteDuration) * noteDuration);
                return Math.Min(1, local / 0.005) * Math.Exp(-10 * local);
            },
            0.55);
    }

    // Old-modem-style FM chirp - a sine wobbling rapidly around a high center pitch.
    private static double[] ModemChirp() =>
        Render(0.25, t => 1200 + (900 * Math.Sin(2 * Math.PI * 18 * t)), Sine, t => Math.Exp(-5 * t), 0.5);

    // Bright glassy "ting" - a single high sine with a fast decay.
    private static double[] GlassTing() =>
        Render(0.35, _ => 2600.0, Sine, t => Math.Exp(-9 * t), 0.5);

    // Buzzy kazoo drone with a slow vibrato wobble.
    private static double[] Kazoo() =>
        Render(0.3, t => 350 + (10 * Math.Sin(2 * Math.PI * 7 * t)), Buzzy, t => Bell(t, 0.3), 0.5);

    // Descending sine "power down" - the inverse of Blip.
    private static double[] PowerDown() =>
        Render(0.18, t => 1400 - (900 * (t / 0.18)), Sine, t => Math.Min(1, t / 0.01) * Math.Exp(-10 * t), 0.65);

    // Cat meow - a sine that glides up then back down under a bell envelope.
    private static double[] CatMeow() =>
        Render(
            0.4,
            t => t < 0.2 ? 400 + (500 * (t / 0.2)) : 900 - (300 * ((t - 0.2) / 0.2)),
            Sine,
            t => Bell(t, 0.4),
            0.55);

    // ============================================================
    // Noise-based effects (rendered with bespoke loops)
    // ============================================================

    // Tiny punchy noise burst.
    private static double[] Pop()
    {
        const double duration = 0.08;
        var n = (int)(SampleRate * duration);
        var samples = new double[n];
        var random = new Random(7);

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var noise = (random.NextDouble() * 2) - 1;
            samples[i] = noise * Math.Exp(-45 * t);
        }

        return samples;
    }

    // The crowd-pleaser: a low, buzzy sawtooth with vibrato + a tremolo
    // flutter and a dusting of noise.
    private static double[] Fart()
    {
        const double duration = 0.55;
        var n = (int)(SampleRate * duration);
        var samples = new double[n];
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

            samples[i] = ((saw * 0.85) + noise) * tremolo * envelope;
        }

        return samples;
    }

    // Lower, wetter cousin of the fart with a hard amplitude chop ("blblbl").
    private static double[] WetRaspberry()
    {
        const double duration = 0.45;
        var n = (int)(SampleRate * duration);
        var samples = new double[n];
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

            samples[i] = ((saw * 0.8) + noise) * chop * envelope;
        }

        return samples;
    }

    // Muffled boom: low-passed noise crackle over a low sine rumble.
    private static double[] Explosion()
    {
        const double duration = 0.6;
        var n = (int)(SampleRate * duration);
        var samples = new double[n];
        var random = new Random(31);
        var lowPass = 0.0;

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var white = (random.NextDouble() * 2) - 1;
            lowPass += 0.12 * (white - lowPass);

            var rumble = Math.Sin(2 * Math.PI * 55 * t);
            var envelope = Math.Exp(-4.5 * t);

            samples[i] = ((lowPass * 1.6 * 0.7) + (rumble * 0.5)) * envelope;
        }

        return samples;
    }

    // Metallic cowbell clang - two close square-wave overtones.
    private static double[] Cowbell()
    {
        const double duration = 0.3;
        var n = (int)(SampleRate * duration);
        var samples = new double[n];

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var toneA = Square(t * 587.0);
            var toneB = Square(t * 845.0);
            var envelope = Math.Exp(-10 * t);

            samples[i] = ((toneA * 0.5) + (toneB * 0.5)) * envelope;
        }

        return samples;
    }

    // Duck quack - a wobbling low sawtooth with a dusting of noise and a hard chop-off.
    private static double[] DuckQuack()
    {
        const double duration = 0.22;
        var n = (int)(SampleRate * duration);
        var samples = new double[n];
        var random = new Random(41);
        var phase = 0.0;

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var freq = 320 + (60 * Math.Sin(2 * Math.PI * 28 * t));
            phase += freq / SampleRate;

            var saw = Saw(phase);
            var noise = ((random.NextDouble() * 2) - 1) * 0.15;
            var envelope = Math.Min(1, t / 0.01) * Math.Exp(-9 * t);

            samples[i] = ((saw * 0.8) + noise) * envelope;
        }

        return samples;
    }

    // Short dog-bark punch: a low tone thump layered under a noise burst.
    private static double[] DogBark()
    {
        const double duration = 0.18;
        var n = (int)(SampleRate * duration);
        var samples = new double[n];
        var random = new Random(53);

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var noise = ((random.NextDouble() * 2) - 1) * 0.4;
            var tone = Math.Sin(2 * Math.PI * 180 * t);
            var envelope = Math.Min(1, t / 0.005) * Math.Exp(-22 * t);

            samples[i] = ((tone * 0.6) + noise) * envelope;
        }

        return samples;
    }

    // Filtered noise sweep - the low-pass cutoff opens up over the clip's life.
    private static double[] Whoosh()
    {
        const double duration = 0.3;
        var n = (int)(SampleRate * duration);
        var samples = new double[n];
        var random = new Random(61);
        var lowPass = 0.0;

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var white = (random.NextDouble() * 2) - 1;
            var coefficient = 0.05 + (0.35 * (t / duration));
            lowPass += coefficient * (white - lowPass);

            samples[i] = lowPass * Bell(t, duration);
        }

        return samples;
    }

    // Camera-shutter double tick.
    private static double[] CameraClick()
    {
        const double duration = 0.12;
        var n = (int)(SampleRate * duration);
        var samples = new double[n];
        var random = new Random(67);

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var tick1 = Math.Exp(-300 * Math.Abs(t - 0.01));
            var tick2 = Math.Exp(-300 * Math.Abs(t - 0.07));
            var noise = (random.NextDouble() * 2) - 1;

            samples[i] = noise * (tick1 + tick2);
        }

        return samples;
    }

    // Tight wood-block knock - noise click with a low thump underneath.
    private static double[] WoodBlock()
    {
        const double duration = 0.1;
        var n = (int)(SampleRate * duration);
        var samples = new double[n];
        var random = new Random(73);

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var noise = (random.NextDouble() * 2) - 1;
            var thump = Math.Sin(2 * Math.PI * 220 * t);
            var envelope = Math.Exp(-70 * t);

            samples[i] = ((noise * 0.5) + (thump * 0.5)) * envelope;
        }

        return samples;
    }

    // Punchy three-note square-wave synth stab.
    private static double[] SynthStab()
    {
        const double duration = 0.18;
        var n = (int)(SampleRate * duration);
        var samples = new double[n];

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var a = Square(t * 220.0);
            var b = Square(t * 277.0);
            var c = Square(t * 330.0);
            var envelope = Math.Min(1, t / 0.005) * Math.Exp(-14 * t);

            samples[i] = (a + b + c) / 3.0 * envelope;
        }

        return samples;
    }

    // Two short blips stuttered back to back, like a hiccup.
    private static double[] Hiccup()
    {
        var first = Render(0.07, t => 700 + (400 * (t / 0.07)), Sine, t => Math.Exp(-30 * t), 0.6);
        var second = Render(0.09, t => 500 + (600 * (t / 0.09)), Sine, t => Math.Exp(-25 * t), 0.6);
        var gapSamples = (int)(SampleRate * 0.03);

        var combined = new double[first.Length + gapSamples + second.Length];
        Array.Copy(first, 0, combined, 0, first.Length);
        Array.Copy(second, 0, combined, first.Length + gapSamples, second.Length);
        return combined;
    }

    // A few staggered high sine "twinkles" overlapping into one sparkle.
    private static double[] Sparkle()
    {
        const double duration = 0.3;
        var n = (int)(SampleRate * duration);
        var samples = new double[n];
        double[] freqs = { 1800, 2400, 3000 };
        double[] starts = { 0.0, 0.06, 0.12 };

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            double value = 0;
            for (var k = 0; k < freqs.Length; k++)
            {
                var local = t - starts[k];
                if (local < 0)
                {
                    continue;
                }

                value += Math.Sin(2 * Math.PI * freqs[k] * local) * Math.Exp(-25 * local);
            }

            samples[i] = value;
        }

        return samples;
    }

    // ============================================================
    // Synthesis helpers
    // ============================================================

    // Renders a pitched tone: integrates the (possibly time-varying) frequency
    // into a phase so sweeps stay click-free, shaping each sample by waveform,
    // amplitude envelope, and overall gain.
    private static double[] Render(
        double seconds,
        Func<double, double> frequency,
        Func<double, double> waveform,
        Func<double, double> envelope,
        double gain)
    {
        var n = (int)(SampleRate * seconds);
        var samples = new double[n];
        var phase = 0.0;

        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            phase += frequency(t) / SampleRate;
            samples[i] = waveform(phase) * envelope(t) * gain;
        }

        return samples;
    }

    private static double Frac(double x) => x - Math.Floor(x);

    private static double Sine(double phase) => Math.Sin(2 * Math.PI * phase);

    private static double Square(double phase) => Frac(phase) < 0.5 ? 1.0 : -1.0;

    private static double Saw(double phase) => (2 * Frac(phase)) - 1;

    // Nasal kazoo-ish timbre: half square, half sine.
    private static double Buzzy(double phase) => (Square(phase) * 0.5) + (Sine(phase) * 0.5);

    // Smooth fade in and out (half-sine), zero at both ends.
    private static double Bell(double t, double duration) => Math.Sin(Math.PI * Math.Min(1, t / duration));

    // Scales a clip so its loudest sample hits targetPeak, so every effect -
    // simple tones and layered noise alike - ends up at the same volume.
    private static double[] Normalize(double[] samples, double targetPeak)
    {
        var peak = 0.0;
        foreach (var sample in samples)
        {
            var abs = Math.Abs(sample);
            if (abs > peak)
            {
                peak = abs;
            }
        }

        if (peak <= 0)
        {
            return samples;
        }

        var scale = targetPeak / peak;
        var result = new double[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            result[i] = samples[i] * scale;
        }

        return result;
    }

    private static short[] ToShortArray(double[] samples)
    {
        var result = new short[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            result[i] = ToSample(samples[i]);
        }

        return result;
    }

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
