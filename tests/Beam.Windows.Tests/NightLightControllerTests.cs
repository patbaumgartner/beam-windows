using Beam.Core.NightLight;
using Beam.Windows.NightLight;

namespace Beam.Windows.Tests;

public class NightLightControllerTests
{
    [Theory]
    [InlineData(true, NightLightTransitionCause.Manual, false)]
    [InlineData(true, NightLightTransitionCause.Schedule, false)]
    [InlineData(false, NightLightTransitionCause.Schedule, false)]
    [InlineData(false, NightLightTransitionCause.Manual, true)]
    public void ShouldRestore_OnlyWhenCurrentStateIsStillBeamDisabled(
        bool active,
        NightLightTransitionCause cause,
        bool expected)
    {
        var current = new NightLightState(active, cause, 1, 1);

        Assert.Equal(expected, NightLightController.ShouldRestore(current));
    }
}
