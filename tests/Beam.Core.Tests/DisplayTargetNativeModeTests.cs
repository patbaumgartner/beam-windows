using Beam.Core.Display;

namespace Beam.Core.Tests;

/// <summary>B2: DisplayTarget carries the EDID-native mode as an optional 8th field.</summary>
public class DisplayTargetNativeModeTests
{
    [Fact]
    public void NativeMode_DefaultsToNull()
    {
        var target = new DisplayTarget("Dell", "DEL", 0x1234, "HDMI", @"monitor\dell", false, true);

        Assert.Null(target.NativeMode);
    }

    [Fact]
    public void NativeMode_RoundTrips_AndIdentityIsUnaffected()
    {
        var native = new DisplayMode(1920, 1080, 60);
        var target = new DisplayTarget("Proj", "EPS", 0x0001, "HDMI", @"monitor\proj", false, true, native);

        Assert.Equal(native, target.NativeMode);
        Assert.Equal(@"monitor\proj", target.Identity);
    }
}
