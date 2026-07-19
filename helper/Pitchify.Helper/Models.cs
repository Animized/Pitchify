using System.Text.Json.Serialization;

namespace Pitchify.Helper;

[JsonConverter(typeof(JsonStringEnumConverter<PipelineState>))]
public enum PipelineState
{
    Ready,
    SetupRequired,
    Error,
}

[JsonConverter(typeof(JsonStringEnumConverter<UpdateState>))]
public enum UpdateState
{
    Disabled,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Installing,
    Error,
}

public sealed record UpdateDto(
    string CurrentVersion,
    string? LatestVersion,
    UpdateState State,
    string? Message);

public sealed record DeviceDto(
    string Id,
    string Name,
    bool IsDefault);

public sealed record StatusDto(
    string Version,
    int Semitones,
    PipelineState State,
    DeviceDto? InputDevice,
    DeviceDto? OutputDevice,
    IReadOnlyList<DeviceDto> AvailableOutputs,
    bool FollowsDefaultOutput,
    int? LatencyMs,
    string? Message,
    UpdateDto? Update = null);

public sealed record PitchRequest(int Semitones);

public sealed record OutputRequest(string? DeviceId);

public static class PitchValidator
{
    public const int Minimum = -12;
    public const int Maximum = 12;

    public static bool IsValid(int semitones) =>
        semitones is >= Minimum and <= Maximum;

    public static int Clamp(int semitones) =>
        Math.Clamp(semitones, Minimum, Maximum);
}
