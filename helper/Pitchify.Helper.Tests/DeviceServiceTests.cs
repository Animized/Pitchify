namespace Pitchify.Helper.Tests;

public sealed class DeviceServiceTests
{
    [Theory]
    [InlineData("CABLE Output (VB-Audio Virtual Cable)", true)]
    [InlineData("CABLE Input (VB-Audio Virtual Cable)", false)]
    [InlineData("Speakers (Realtek Audio)", false)]
    public void DetectsCableCaptureByName(string name, bool expected)
    {
        Assert.Equal(expected, DeviceService.IsCableCaptureName(name));
    }

    [Theory]
    [InlineData("CABLE Output (VB-Audio Virtual Cable)", true)]
    [InlineData("CABLE Input (VB-Audio Virtual Cable)", true)]
    [InlineData("Headphones (Bluetooth)", false)]
    public void RejectsVirtualCableAsPhysicalOutput(string name, bool expected)
    {
        Assert.Equal(expected, DeviceService.IsVirtualCableName(name));
    }
}

