using System.Runtime.InteropServices;
using NAudio.Wave;
using SoundTouch;

namespace Pitchify.Helper;

/// <summary>
/// A recoverable, never-ending SoundTouch provider designed for live capture.
/// Temporary input starvation produces silence and re-primes the processor
/// instead of permanently flushing it as if a file had ended.
/// </summary>
public sealed class LivePitchWaveProvider : IWaveProvider
{
    private const double PrimeBufferMilliseconds = 185;
    private const int InputBufferFrames = 2048;
    private const int HighQualityAntiAliasFilterLength = 64;

    private readonly object _gate = new();
    private readonly ClockDriftResamplingSampleProvider _source;
    private readonly LiveAudioBuffer _liveBuffer;
    private readonly SoundTouchProcessor _processor = new();
    private readonly float[] _inputBuffer;
    private bool _primed;
    private int _semitones;
    private long _underrunCount;

    public LivePitchWaveProvider(
        ISampleProvider source,
        LiveAudioBuffer liveBuffer,
        int outputSampleRate,
        int semitones)
    {
        if (source.WaveFormat.Channels != 2)
        {
            throw new ArgumentException(
                "Pitchify's live pitch processor requires stereo input.",
                nameof(source));
        }

        _liveBuffer = liveBuffer;
        _source = new ClockDriftResamplingSampleProvider(
            source,
            liveBuffer,
            outputSampleRate);
        WaveFormat = _source.WaveFormat;
        _inputBuffer = new float[InputBufferFrames * WaveFormat.Channels];

        _processor.SampleRate = WaveFormat.SampleRate;
        _processor.Channels = WaveFormat.Channels;
        _processor.Tempo = 1.0;
        _processor.Rate = 1.0;
        _processor.SetSetting(SettingId.UseQuickSeek, 0);
        _processor.SetSetting(SettingId.UseAntiAliasFilter, 1);
        _processor.SetSetting(
            SettingId.AntiAliasFilterLength,
            HighQualityAntiAliasFilterLength);

        Semitones = semitones;
    }

    public WaveFormat WaveFormat { get; }

    public int Semitones
    {
        get
        {
            lock (_gate)
            {
                return _semitones;
            }
        }
        set
        {
            if (!PitchValidator.IsValid(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            lock (_gate)
            {
                if (_semitones == value)
                {
                    return;
                }

                _semitones = value;
                _processor.PitchSemiTones = value;
            }
        }
    }

    public long UnderrunCount =>
        Interlocked.Read(ref _underrunCount);

    public int EstimatedLatencyMilliseconds
    {
        get
        {
            lock (_gate)
            {
                var processorLatency = _processor.GetSetting(
                    SettingId.InitialLatency);
                return (int)Math.Ceiling(
                    processorLatency * 1000.0 / WaveFormat.SampleRate
                    + ClockDriftResamplingSampleProvider.TargetBufferMilliseconds);
            }
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        var output = MemoryMarshal.Cast<byte, float>(
            buffer.AsSpan(offset, count));
        output.Clear();

        lock (_gate)
        {
            if (!_primed)
            {
                if (_liveBuffer.BufferedDuration.TotalMilliseconds
                    < PrimeBufferMilliseconds)
                {
                    return count;
                }

                _processor.Clear();
                _source.Reset();
                _primed = true;
            }

            var framesRequested = output.Length / WaveFormat.Channels;
            while (_processor.AvailableSamples < framesRequested)
            {
                var samplesRead = _source.Read(
                    _inputBuffer,
                    0,
                    _inputBuffer.Length);
                if (samplesRead == 0)
                {
                    var availableFrames = Math.Min(
                        framesRequested,
                        _processor.AvailableSamples);
                    if (availableFrames > 0)
                    {
                        _processor.ReceiveSamples(
                            output,
                            availableFrames);
                    }

                    ReprimeAfterUnderrun();
                    return count;
                }

                _processor.PutSamples(
                    _inputBuffer.AsSpan(0, samplesRead),
                    samplesRead / WaveFormat.Channels);
            }

            _processor.ReceiveSamples(output, framesRequested);
            return count;
        }
    }

    private void ReprimeAfterUnderrun()
    {
        Interlocked.Increment(ref _underrunCount);
        _primed = false;
        _processor.Clear();
        _source.Reset();
    }
}
