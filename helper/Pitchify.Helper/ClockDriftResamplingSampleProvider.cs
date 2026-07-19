using NAudio.Dsp;
using NAudio.Wave;

namespace Pitchify.Helper;

/// <summary>
/// Resamples by a few parts per thousand to keep independent capture and
/// playback device clocks synchronized. The correction is far below a
/// semitone and avoids SoundTouch tempo modulation.
/// </summary>
public sealed class ClockDriftResamplingSampleProvider : ISampleProvider
{
    public const double TargetBufferMilliseconds = 85;
    public const double DeadZoneMilliseconds = 18;
    public const double MaximumRateCorrection = 0.003;

    private readonly ISampleProvider _source;
    private readonly LiveAudioBuffer _liveBuffer;
    private readonly WdlResampler _resampler = new();
    private readonly int _channels;
    private readonly int _sourceSampleRate;
    private readonly int _outputSampleRate;
    private double _currentRatio = 1.0;

    public ClockDriftResamplingSampleProvider(
        ISampleProvider source,
        LiveAudioBuffer liveBuffer,
        int outputSampleRate)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _liveBuffer = liveBuffer ?? throw new ArgumentNullException(nameof(liveBuffer));
        _channels = source.WaveFormat.Channels;
        _sourceSampleRate = source.WaveFormat.SampleRate;
        _outputSampleRate = outputSampleRate;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(
            outputSampleRate,
            _channels);

        _resampler.SetMode(
            interp: false,
            filtercnt: 0,
            sinc: true,
            sinc_size: 64,
            sinc_interpsize: 32);
        _resampler.SetFilterParms();
        _resampler.SetFeedMode(wantInputDriven: false);
        ApplyRates();
    }

    public WaveFormat WaveFormat { get; }

    public double CurrentRatio => _currentRatio;

    public static double CalculateTargetRatio(double bufferedMilliseconds)
    {
        var error = bufferedMilliseconds - TargetBufferMilliseconds;
        if (Math.Abs(error) <= DeadZoneMilliseconds)
        {
            return 1.0;
        }

        var magnitude = Math.Min(
            MaximumRateCorrection,
            (Math.Abs(error) - DeadZoneMilliseconds) * 0.000025);
        return 1.0 + Math.CopySign(magnitude, error);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var desiredRatio = CalculateTargetRatio(
            _liveBuffer.BufferedDuration.TotalMilliseconds);
        _currentRatio += (desiredRatio - _currentRatio) * 0.04;
        ApplyRates();

        var framesRequested = count / _channels;
        var inputNeeded = _resampler.ResamplePrepare(
            framesRequested,
            _channels,
            out var inputBuffer,
            out var inputOffset);

        var availableInputFrames = (int)Math.Floor(
            _liveBuffer.BufferedDuration.TotalSeconds * _sourceSampleRate);
        if (availableInputFrames < inputNeeded)
        {
            return 0;
        }

        var samplesRead = _source.Read(
            inputBuffer,
            inputOffset,
            inputNeeded * _channels);
        var inputFramesRead = samplesRead / _channels;
        if (inputFramesRead != inputNeeded)
        {
            return 0;
        }

        var outputFrames = _resampler.ResampleOut(
            buffer,
            offset,
            inputFramesRead,
            framesRequested,
            _channels);
        return outputFrames * _channels;
    }

    public void Reset()
    {
        _currentRatio = 1.0;
        _resampler.Reset();
        ApplyRates();
    }

    private void ApplyRates()
    {
        _resampler.SetRates(
            _sourceSampleRate * _currentRatio,
            _outputSampleRate);
    }
}
