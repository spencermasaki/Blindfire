using System;
using System.IO;
using System.Media;
using System.Text;

namespace Blindfire.Audio;

// Synthesizes a short click/tick sound in memory (no external asset to ship)
// and plays it asynchronously so it never blocks the click-handling path.
public static class ClickSoundPlayer
{
    private static readonly byte[] ClickWavBytes = GenerateClickWav();

    public static void PlayClick()
    {
        try
        {
            var stream = new MemoryStream(ClickWavBytes);
            var player = new SoundPlayer(stream);
            player.Play();
        }
        catch
        {
            // Audio is best-effort feedback; never let playback issues break the trial flow.
        }
    }

    private static byte[] GenerateClickWav()
    {
        const int sampleRate = 44100;
        const double durationSeconds = 0.045;
        const double frequencyHz = 1400.0;
        const double decayRate = 60.0;

        var sampleCount = (int)(sampleRate * durationSeconds);
        var samples = new short[sampleCount];
        var random = new Random(12345);

        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (double)sampleRate;
            var envelope = Math.Exp(-decayRate * t);
            var tone = Math.Sin(2 * Math.PI * frequencyHz * t);
            var noise = (random.NextDouble() * 2 - 1) * 0.3;
            var sampleValue = envelope * (tone * 0.7 + noise * 0.3);
            samples[i] = (short)(Math.Clamp(sampleValue, -1.0, 1.0) * short.MaxValue);
        }

        return BuildWavFile(samples, sampleRate);
    }

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
