using NAudio.Wave;

namespace Pitchify.Helper.Tests;

public sealed class LiveAudioBufferTests
{
    private static readonly WaveFormat Format =
        WaveFormat.CreateIeeeFloatWaveFormat(1_000, 1);

    [Fact]
    public void OverflowKeepsNewestFramesInsteadOfStaleAudio()
    {
        var buffer = new LiveAudioBuffer(
            Format,
            TimeSpan.FromMilliseconds(4));
        var first = FloatsToBytes([1, 2, 3, 4]);
        var newest = FloatsToBytes([5, 6]);

        buffer.AddSamples(first, 0, first.Length);
        buffer.AddSamples(newest, 0, newest.Length);

        var result = new byte[buffer.CapacityBytes];
        var read = buffer.Read(result, 0, result.Length);
        var samples = BytesToFloats(result.AsSpan(0, read).ToArray());

        Assert.Equal([3, 4, 5, 6], samples);
        Assert.Equal(2 * sizeof(float), buffer.DiscardedBytes);
    }

    [Fact]
    public void EmptyReadReturnsImmediatelyWithoutInventingSilence()
    {
        var buffer = new LiveAudioBuffer(
            Format,
            TimeSpan.FromMilliseconds(10));

        Assert.Equal(0, buffer.Read(new byte[16], 0, 16));
    }

    private static byte[] FloatsToBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToFloats(byte[] bytes)
    {
        var samples = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        return samples;
    }
}
