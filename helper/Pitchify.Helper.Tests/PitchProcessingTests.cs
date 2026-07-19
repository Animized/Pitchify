using NAudio.Wave;

namespace Pitchify.Helper.Tests;

public sealed class PitchProcessingTests
{
    private const int SampleRate = 48_000;
    private const int Channels = 2;
    private const double DurationSeconds = 3.0;
    private const double OutputDurationSeconds = 2.0;

    [Theory]
    [InlineData(0, 440.0)]
    [InlineData(12, 880.0)]
    [InlineData(-12, 220.0)]
    public void ShiftsSineWaveWhileKeepingDuration(int semitones, double expectedFrequency)
    {
        var input = CreateSineWave(440.0, DurationSeconds);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(
            SampleRate,
            Channels);
        var liveBuffer = new LiveAudioBuffer(
            format,
            TimeSpan.FromSeconds(4));
        liveBuffer.AddSamples(input, 0, input.Length);
        var shifter = new LivePitchWaveProvider(
            liveBuffer.ToSampleProvider(),
            liveBuffer,
            SampleRate,
            semitones);

        var output = ReadDuration(shifter, OutputDurationSeconds);
        var outputSamples = new float[output.Length / sizeof(float)];
        Buffer.BlockCopy(output, 0, outputSamples, 0, output.Length);

        var measured = MeasureFrequency(
            outputSamples,
            SampleRate,
            Channels,
            channel: 0);
        Assert.InRange(measured, expectedFrequency * 0.96, expectedFrequency * 1.04);

        var outputDuration =
            outputSamples.Length / (double)(SampleRate * Channels);
        Assert.Equal(OutputDurationSeconds, outputDuration, precision: 3);
        Assert.Equal(0, shifter.UnderrunCount);
    }

    [Fact]
    public void PreservesIndependentStereoChannels()
    {
        var input = CreateStereoSineWave(
            leftFrequency: 440,
            rightFrequency: 660,
            DurationSeconds);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(
            SampleRate,
            Channels);
        var liveBuffer = new LiveAudioBuffer(
            format,
            TimeSpan.FromSeconds(4));
        liveBuffer.AddSamples(input, 0, input.Length);
        var shifter = new LivePitchWaveProvider(
            liveBuffer.ToSampleProvider(),
            liveBuffer,
            SampleRate,
            semitones: 0);

        var bytes = ReadDuration(shifter, OutputDurationSeconds);
        var samples = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);

        var left = MeasureFrequency(samples, SampleRate, Channels, channel: 0);
        var right = MeasureFrequency(samples, SampleRate, Channels, channel: 1);
        Assert.InRange(left, 425, 455);
        Assert.InRange(right, 640, 680);
    }

    private static byte[] CreateSineWave(double frequency, double durationSeconds)
    {
        return CreateStereoSineWave(frequency, frequency, durationSeconds);
    }

    private static byte[] CreateStereoSineWave(
        double leftFrequency,
        double rightFrequency,
        double durationSeconds)
    {
        var frameCount = (int)(SampleRate * durationSeconds);
        var samples = new float[frameCount * Channels];
        for (var frame = 0; frame < frameCount; frame++)
        {
            samples[frame * Channels] = (float)(0.5 * Math.Sin(
                2 * Math.PI * leftFrequency * frame / SampleRate));
            samples[frame * Channels + 1] = (float)(0.5 * Math.Sin(
                2 * Math.PI * rightFrequency * frame / SampleRate));
        }

        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] ReadDuration(
        IWaveProvider provider,
        double durationSeconds)
    {
        var requestedBytes = (int)(
            SampleRate
            * Channels
            * sizeof(float)
            * durationSeconds);
        var output = new byte[requestedBytes];
        var offset = 0;
        while (offset < output.Length)
        {
            var read = provider.Read(
                output,
                offset,
                Math.Min(4096, output.Length - offset));
            Assert.True(read > 0);
            offset += read;
        }

        return output;
    }

    private static double MeasureFrequency(
        float[] interleavedSamples,
        int sampleRate,
        int channels,
        int channel)
    {
        var left = new List<float>(interleavedSamples.Length / channels);
        for (
            var index = channel;
            index < interleavedSamples.Length;
            index += channels)
        {
            left.Add(interleavedSamples[index]);
        }

        var firstAudible = left.FindIndex(sample => Math.Abs(sample) > 0.05f);
        Assert.True(firstAudible >= 0, "No audible output was produced.");

        var start = Math.Min(firstAudible + sampleRate / 5, left.Count - 2);
        var end = Math.Min(start + sampleRate / 2, left.Count - 1);
        var crossings = 0;
        for (var index = start + 1; index <= end; index++)
        {
            if (left[index - 1] <= 0 && left[index] > 0)
            {
                crossings++;
            }
        }

        var measuredDuration = (end - start) / (double)sampleRate;
        return crossings / measuredDuration;
    }
}
