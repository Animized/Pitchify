using System.Security.Cryptography;
using System.Text.Json;

namespace Pitchify.Helper;

public sealed class PitchifyConfig
{
    public string ApiToken { get; set; } = string.Empty;

    public int Semitones { get; set; }

    public string? OutputDeviceId { get; set; }

    public string? UpdateRepository { get; set; }
}

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private PitchifyConfig _config;

    public ConfigStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _config = LoadFromDisk(path);

        if (string.IsNullOrWhiteSpace(_config.ApiToken))
        {
            _config.ApiToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
                .ToLowerInvariant();
            SaveLocked();
        }

        if (!PitchValidator.IsValid(_config.Semitones))
        {
            _config.Semitones = PitchValidator.Clamp(_config.Semitones);
            SaveLocked();
        }
    }

    public PitchifyConfig Snapshot()
    {
        lock (_gate)
        {
            return new PitchifyConfig
            {
                ApiToken = _config.ApiToken,
                Semitones = _config.Semitones,
                OutputDeviceId = _config.OutputDeviceId,
                UpdateRepository = _config.UpdateRepository,
            };
        }
    }

    public void SetSemitones(int semitones)
    {
        if (!PitchValidator.IsValid(semitones))
        {
            throw new ArgumentOutOfRangeException(
                nameof(semitones),
                $"Semitones must be between {PitchValidator.Minimum} and {PitchValidator.Maximum}.");
        }

        lock (_gate)
        {
            _config.Semitones = semitones;
            SaveLocked();
        }
    }

    public void SetOutputDevice(string? deviceId)
    {
        lock (_gate)
        {
            _config.OutputDeviceId =
                string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
            SaveLocked();
        }
    }

    private static PitchifyConfig LoadFromDisk(string path)
    {
        if (!File.Exists(path))
        {
            return new PitchifyConfig();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PitchifyConfig>(json, JsonOptions)
                ?? new PitchifyConfig();
        }
        catch (JsonException)
        {
            var backupPath = $"{path}.invalid-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(path, backupPath, overwrite: true);
            return new PitchifyConfig();
        }
    }

    private void SaveLocked()
    {
        var temporaryPath = $"{_path}.tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(_config, JsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }
}
