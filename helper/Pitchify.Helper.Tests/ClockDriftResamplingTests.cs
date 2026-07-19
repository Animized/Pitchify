namespace Pitchify.Helper.Tests;

public sealed class ClockDriftResamplingTests
{
    [Fact]
    public void ConsumesFasterWhenTheLiveBufferIsTooFull()
    {
        var ratio =
            ClockDriftResamplingSampleProvider.CalculateTargetRatio(250);

        Assert.InRange(
            ratio,
            1.0,
            1.0 + ClockDriftResamplingSampleProvider.MaximumRateCorrection);
    }

    [Fact]
    public void ConsumesSlowerWhenTheLiveBufferIsTooLow()
    {
        var ratio =
            ClockDriftResamplingSampleProvider.CalculateTargetRatio(5);

        Assert.InRange(
            ratio,
            1.0 - ClockDriftResamplingSampleProvider.MaximumRateCorrection,
            1.0);
    }

    [Theory]
    [InlineData(70)]
    [InlineData(85)]
    [InlineData(100)]
    public void LeavesTheClockUnchangedInsideTheDeadZone(double bufferedMs)
    {
        Assert.Equal(
            1.0,
            ClockDriftResamplingSampleProvider.CalculateTargetRatio(bufferedMs));
    }
}
