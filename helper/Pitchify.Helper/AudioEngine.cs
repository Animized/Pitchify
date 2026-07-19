using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Pitchify.Helper;

public sealed class AudioEngine : IDisposable
{
    private const int CaptureBufferMilliseconds = 25;
    private const int OutputBufferMilliseconds = 60;
    private const int MaximumBufferedMilliseconds = 350;
    private const int DeviceChangeDebounceMilliseconds = 350;

    private readonly object _gate = new();
    private readonly ConfigStore _configStore;
    private readonly DeviceService _deviceService;
    private readonly FileLogger _logger;
    private readonly Timer _deviceChangeTimer;

    private WasapiCapture? _capture;
    private WasapiOut? _output;
    private LiveAudioBuffer? _captureBuffer;
    private LivePitchWaveProvider? _pitchProvider;
    private MMDevice? _inputDevice;
    private MMDevice? _outputDevice;
    private PipelineState _state = PipelineState.SetupRequired;
    private string? _message = "Audio pipeline has not started.";
    private bool _disposed;
    private bool _restartInProgress;
    private string? _observedDefaultOutputId;
    private IReadOnlyList<DeviceDto> _availableOutputs =
        Array.Empty<DeviceDto>();

    public AudioEngine(
        ConfigStore configStore,
        DeviceService deviceService,
        FileLogger logger)
    {
        _configStore = configStore;
        _deviceService = deviceService;
        _logger = logger;
        _deviceChangeTimer = new Timer(
            _ => MonitorDevices(),
            null,
            Timeout.Infinite,
            Timeout.Infinite);
        _deviceService.DevicesChanged += OnDevicesChanged;
    }

    public void Start()
    {
        Restart();
    }

    public StatusDto GetStatus()
    {
        lock (_gate)
        {
            var config = _configStore.Snapshot();
            return new StatusDto(
                Version: BuildInfo.Version,
                Semitones: config.Semitones,
                State: _state,
                InputDevice: ToDeviceDto(_inputDevice, isDefault: false),
                OutputDevice: ToDeviceDto(
                    _outputDevice,
                    config.OutputDeviceId is null
                        && _outputDevice?.ID == _observedDefaultOutputId),
                AvailableOutputs: _availableOutputs,
                FollowsDefaultOutput: config.OutputDeviceId is null,
                LatencyMs: _state == PipelineState.Ready
                    ? CaptureBufferMilliseconds
                        + OutputBufferMilliseconds
                        + (_pitchProvider?.EstimatedLatencyMilliseconds ?? 0)
                    : null,
                Message: _message);
        }
    }

    public StatusDto SetSemitones(int semitones)
    {
        if (!PitchValidator.IsValid(semitones))
        {
            throw new ArgumentOutOfRangeException(
                nameof(semitones),
                $"Semitones must be between {PitchValidator.Minimum} and {PitchValidator.Maximum}.");
        }

        lock (_gate)
        {
            _configStore.SetSemitones(semitones);
            if (_pitchProvider is not null)
            {
                _pitchProvider.Semitones = semitones;
            }

            _logger.Info($"Pitch changed to {semitones:+#;-#;0} semitones.");
        }

        return GetStatus();
    }

    public StatusDto SetOutputDevice(string? deviceId)
    {
        if (!string.IsNullOrWhiteSpace(deviceId)
            && !_deviceService.IsAvailableSafeOutput(deviceId))
        {
            throw new ArgumentException(
                "The selected output is unavailable or is a virtual cable.",
                nameof(deviceId));
        }

        _configStore.SetOutputDevice(deviceId);
        Restart();
        return GetStatus();
    }

    public StatusDto RestartAndGetStatus()
    {
        Restart();
        return GetStatus();
    }

    public void Restart()
    {
        lock (_gate)
        {
            if (_disposed || _restartInProgress)
            {
                return;
            }

            _restartInProgress = true;
            try
            {
                StopPipelineLocked();
                StartPipelineLocked();
            }
            catch (Exception exception)
            {
                _state = PipelineState.Error;
                _message = $"The audio pipeline could not start: {exception.Message}";
                _logger.Error("Audio pipeline start failed.", exception);
                StopPipelineLocked();
            }
            finally
            {
                _restartInProgress = false;
            }
        }
    }

    private void StartPipelineLocked()
    {
        var config = _configStore.Snapshot();
        _availableOutputs = _deviceService.GetAvailableOutputs();
        _observedDefaultOutputId = _deviceService.GetDefaultSafeOutputId();
        _inputDevice = _deviceService.FindCableCapture();
        if (_inputDevice is null)
        {
            _state = PipelineState.SetupRequired;
            _message =
                "VB-CABLE was not detected. Install it, reboot Windows, and restart the audio pipeline.";
            return;
        }

        _outputDevice = _deviceService.ResolveOutput(config.OutputDeviceId);
        if (_outputDevice is null)
        {
            _state = PipelineState.SetupRequired;
            _message = "No physical audio output is available.";
            return;
        }

        _capture = new WasapiCapture(
            _inputDevice,
            useEventSync: true,
            audioBufferMillisecondsLength: CaptureBufferMilliseconds);

        var captureFormat = _capture.WaveFormat;
        _captureBuffer = new LiveAudioBuffer(
            captureFormat,
            TimeSpan.FromMilliseconds(MaximumBufferedMilliseconds));

        ISampleProvider sampleProvider = _captureBuffer.ToSampleProvider();
        if (sampleProvider.WaveFormat.Channels == 1)
        {
            sampleProvider = new MonoToStereoSampleProvider(sampleProvider);
        }
        else if (sampleProvider.WaveFormat.Channels != 2)
        {
            throw new NotSupportedException(
                $"VB-CABLE must expose mono or stereo audio, but reported {sampleProvider.WaveFormat.Channels} channels.");
        }

        var outputSampleRate = _outputDevice.AudioClient.MixFormat.SampleRate;
        _pitchProvider = new LivePitchWaveProvider(
            sampleProvider,
            _captureBuffer,
            outputSampleRate,
            config.Semitones);

        _output = new WasapiOut(
            _outputDevice,
            AudioClientShareMode.Shared,
            useEventSync: true,
            latency: OutputBufferMilliseconds);
        _output.PlaybackStopped += OnPlaybackStopped;
        _output.Init(_pitchProvider);

        _capture.DataAvailable += OnCaptureDataAvailable;
        _capture.RecordingStopped += OnCaptureStopped;

        _output.Play();
        _capture.StartRecording();

        _state = PipelineState.Ready;
        _message = null;
        _logger.Info(
            $"Audio pipeline started: '{_inputDevice.FriendlyName}' -> '{_outputDevice.FriendlyName}', pitch {config.Semitones:+#;-#;0} st.");
    }

    private void OnCaptureDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        try
        {
            _captureBuffer?.AddSamples(
                eventArgs.Buffer,
                0,
                eventArgs.BytesRecorded);
        }
        catch (Exception exception)
        {
            _logger.Error("Failed to enqueue captured audio.", exception);
        }
    }

    private void OnCaptureStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (eventArgs.Exception is not null)
        {
            HandlePipelineFault("VB-CABLE capture stopped unexpectedly.", eventArgs.Exception);
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (eventArgs.Exception is not null)
        {
            HandlePipelineFault("Audio playback stopped unexpectedly.", eventArgs.Exception);
        }
    }

    private void HandlePipelineFault(string message, Exception exception)
    {
        lock (_gate)
        {
            if (_disposed || _restartInProgress)
            {
                return;
            }

            _state = PipelineState.Error;
            _message = $"{message} {exception.Message}";
            _logger.Error(message, exception);
        }
    }

    private void MonitorDevices()
    {
        try
        {
            var config = _configStore.Snapshot();
            var availableOutputs = _deviceService.GetAvailableOutputs();
            var currentDefault = _deviceService.GetDefaultSafeOutputId();
            using var currentInput = _deviceService.FindCableCapture();
            string? observedInputId;

            lock (_gate)
            {
                _availableOutputs = availableOutputs;
                observedInputId = _inputDevice?.ID;
            }

            if (currentInput?.ID != observedInputId)
            {
                _logger.Info("VB-CABLE endpoint changed; rebuilding the pipeline.");
                Restart();
                return;
            }

            if (config.OutputDeviceId is not null)
            {
                if (!availableOutputs.Any(
                        device => device.Id == config.OutputDeviceId))
                {
                    _logger.Info("Selected output disappeared; falling back to the Windows default.");
                    _configStore.SetOutputDevice(null);
                    Restart();
                }

                return;
            }

            if (currentDefault != _observedDefaultOutputId)
            {
                _logger.Info("Windows default audio output changed; rebuilding the pipeline.");
                Restart();
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Device monitor failed.", exception);
        }
    }

    private void OnDevicesChanged()
    {
        if (_disposed)
        {
            return;
        }

        _deviceChangeTimer.Change(
            DeviceChangeDebounceMilliseconds,
            Timeout.Infinite);
    }

    private void StopPipelineLocked()
    {
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            try
            {
                _output.Stop();
            }
            catch
            {
                // The endpoint may already have disappeared.
            }

            _output.Dispose();
            _output = null;
        }

        if (_capture is not null)
        {
            _capture.DataAvailable -= OnCaptureDataAvailable;
            _capture.RecordingStopped -= OnCaptureStopped;
            try
            {
                _capture.StopRecording();
            }
            catch
            {
                // The endpoint may already have disappeared.
            }

            _capture.Dispose();
            _capture = null;
        }

        _pitchProvider = null;
        _captureBuffer = null;

        _inputDevice?.Dispose();
        _inputDevice = null;
        _outputDevice?.Dispose();
        _outputDevice = null;
    }

    private static DeviceDto? ToDeviceDto(MMDevice? device, bool isDefault)
    {
        return device is null
            ? null
            : new DeviceDto(device.ID, device.FriendlyName, isDefault);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _deviceService.DevicesChanged -= OnDevicesChanged;
            _deviceChangeTimer.Dispose();
            StopPipelineLocked();
        }
    }
}
