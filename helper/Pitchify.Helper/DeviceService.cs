using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Pitchify.Helper;

public sealed class DeviceService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly DeviceNotificationClient _notificationClient;

    public DeviceService()
    {
        _notificationClient = new DeviceNotificationClient(
            () => DevicesChanged?.Invoke());
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    public event Action? DevicesChanged;

    public static bool IsVirtualCableName(string name)
    {
        var normalized = name.ToLowerInvariant();
        return normalized.Contains("vb-audio")
            || normalized.Contains("cable input")
            || normalized.Contains("cable output")
            || normalized.Contains("virtual cable");
    }

    public static bool IsCableCaptureName(string name)
    {
        var normalized = name.ToLowerInvariant();
        return normalized.Contains("cable output")
            && (normalized.Contains("vb-audio") || normalized.Contains("virtual cable"));
    }

    public MMDevice? FindCableCapture()
    {
        foreach (var device in _enumerator.EnumerateAudioEndPoints(
                     DataFlow.Capture,
                     DeviceState.Active))
        {
            if (IsCableCaptureName(device.FriendlyName))
            {
                return device;
            }

            device.Dispose();
        }

        return null;
    }

    public MMDevice? ResolveOutput(string? preferredDeviceId)
    {
        if (!string.IsNullOrWhiteSpace(preferredDeviceId))
        {
            try
            {
                var preferred = _enumerator.GetDevice(preferredDeviceId);
                if (preferred.State == DeviceState.Active
                    && !IsVirtualCableName(preferred.FriendlyName))
                {
                    return preferred;
                }

                preferred.Dispose();
            }
            catch
            {
                // The persisted device was unplugged or removed.
            }
        }

        try
        {
            var defaultDevice = _enumerator.GetDefaultAudioEndpoint(
                DataFlow.Render,
                Role.Multimedia);
            if (!IsVirtualCableName(defaultDevice.FriendlyName))
            {
                return defaultDevice;
            }

            defaultDevice.Dispose();
        }
        catch
        {
            // Fall through to the first safe active output.
        }

        foreach (var device in _enumerator.EnumerateAudioEndPoints(
                     DataFlow.Render,
                     DeviceState.Active))
        {
            if (!IsVirtualCableName(device.FriendlyName))
            {
                return device;
            }

            device.Dispose();
        }

        return null;
    }

    public IReadOnlyList<DeviceDto> GetAvailableOutputs()
    {
        string? defaultId = null;
        try
        {
            using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(
                DataFlow.Render,
                Role.Multimedia);
            defaultId = defaultDevice.ID;
        }
        catch
        {
            // No default endpoint is currently available.
        }

        var outputs = new List<DeviceDto>();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(
                     DataFlow.Render,
                     DeviceState.Active))
        {
            using (device)
            {
                if (!IsVirtualCableName(device.FriendlyName))
                {
                    outputs.Add(new DeviceDto(
                        device.ID,
                        device.FriendlyName,
                        device.ID == defaultId));
                }
            }
        }

        return outputs
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string? GetDefaultSafeOutputId()
    {
        using var device = ResolveOutput(null);
        return device?.ID;
    }

    public bool IsAvailableSafeOutput(string deviceId)
    {
        return GetAvailableOutputs().Any(device => device.Id == deviceId);
    }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
        _enumerator.Dispose();
    }

    private sealed class DeviceNotificationClient : IMMNotificationClient
    {
        private readonly Action _notify;

        public DeviceNotificationClient(Action notify)
        {
            _notify = notify;
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) =>
            _notify();

        public void OnDeviceAdded(string pwstrDeviceId) => _notify();

        public void OnDeviceRemoved(string deviceId) => _notify();

        public void OnDefaultDeviceChanged(
            DataFlow flow,
            Role role,
            string defaultDeviceId) =>
            _notify();

        public void OnPropertyValueChanged(
            string pwstrDeviceId,
            PropertyKey key)
        {
            // Property updates are noisy and do not change routing.
        }
    }
}
