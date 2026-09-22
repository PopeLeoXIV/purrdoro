using System.Buffers.Binary;

namespace Purrdoro.Core.Audio;

/// <summary>
/// Synthesises Purrdoro's completion chime: two soft, bell-like notes (G5 → C6)
/// that fade out in under a second. Generated in code, so the app ships no
/// third-party audio and the sound is entirely original.
/// </summary>
public static class ChimeGenerator
{
    public const int SampleRate = 22050;
    private const double PeakAmplitude = 0.28; // Deliberately quiet.

    /// <summary>Returns a complete 16-bit mono PCM WAV file.</summary>
    public static byte[] CreateCompletionChimeWav()
    {
        var samples = Synthesize();
        return EncodeWav(samples, SampleRate);
    }

    internal static float[] Synthesize()
    {
        const double totalSeconds = 0.95;
        var notes = new (double StartSeconds, double Frequency, double Gain)[]
        {
            (0.00, 783.99, 0.85),  // G5
            (0.16, 1046.50, 1.00), // C6
        };

        var length = (int)(totalSeconds * SampleRate);
        var buffer = new float[length];

        foreach (var (start, frequency, gain) in notes)
        {
            var offset = (int)(start * SampleRate);
            for (var i = offset; i < length; i++)
            {
                var t = (i - offset) / (double)SampleRate;
                var attack = Math.Min(1.0, t / 0.008);       // 8 ms fade-in avoids clicks
                var decay = Math.Exp(-t * 5.5);              // gentle bell decay
                var tone = Math.Sin(2 * Math.PI * frequency * t)
                    + (0.25 * Math.Sin(2 * Math.PI * frequency * 2 * t) * Math.Exp(-t * 9))
                    + (0.08 * Math.Sin(2 * Math.PI * frequency * 3 * t) * Math.Exp(-t * 14));
                buffer[i] += (float)(tone * attack * decay * gain);
            }
        }

        // Normalise to the target peak and add a short fade-out at the very end.
        var peak = buffer.Max(Math.Abs);
        var scale = peak > 0 ? PeakAmplitude / peak : 0;
        var fadeSamples = (int)(0.05 * SampleRate);
        for (var i = 0; i < length; i++)
        {
            var fade = i >= length - fadeSamples ? (length - i) / (double)fadeSamples : 1.0;
            buffer[i] = (float)(buffer[i] * scale * fade);
        }

        return buffer;
    }

    internal static byte[] EncodeWav(float[] samples, int sampleRate)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        const short blockAlign = channels * bitsPerSample / 8;
        var dataLength = samples.Length * blockAlign;
        var wav = new byte[44 + dataLength];
        var span = wav.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataLength);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);           // PCM chunk size
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);            // PCM format
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], bitsPerSample);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataLength);

        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(span[(44 + (i * 2))..], value);
        }

        return wav;
    }
}
