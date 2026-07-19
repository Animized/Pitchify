namespace Pitchify.Helper.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"pitchify-tests-{Guid.NewGuid():N}");

    [Fact]
    public void CreatesTokenAndPersistsPitchAndOutput()
    {
        var path = Path.Combine(_directory, "config.json");
        var store = new ConfigStore(path);

        Assert.NotEmpty(store.Snapshot().ApiToken);
        Assert.Equal(0, store.Snapshot().Semitones);

        store.SetSemitones(7);
        store.SetOutputDevice("device-id");

        var reloaded = new ConfigStore(path).Snapshot();
        Assert.Equal(7, reloaded.Semitones);
        Assert.Equal("device-id", reloaded.OutputDeviceId);
        Assert.Equal(store.Snapshot().ApiToken, reloaded.ApiToken);
    }

    [Theory]
    [InlineData(-13)]
    [InlineData(13)]
    public void RejectsPitchOutsideSupportedRange(int semitones)
    {
        var store = new ConfigStore(Path.Combine(_directory, "config.json"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.SetSemitones(semitones));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

