using Beam.Core.Settings;

namespace Beam.Core.Tests;

/// <summary>C1/C2: pause preset durations, labels, and minutes round-trip.</summary>
public class PausePresetTests
{
    [Theory]
    [InlineData(PausePreset.FifteenMinutes, 15)]
    [InlineData(PausePreset.OneHour, 60)]
    public void Duration_ForTimedPresets(PausePreset preset, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), preset.Duration());
        Assert.Equal(minutes, preset.ToMinutes());
    }

    [Fact]
    public void Indefinite_HasNoDuration_AndNullMinutes()
    {
        Assert.Null(PausePreset.Indefinite.Duration());
        Assert.Null(PausePreset.Indefinite.ToMinutes());
    }

    [Theory]
    [InlineData(PausePreset.FifteenMinutes, "Pause for 15 min")]
    [InlineData(PausePreset.OneHour, "Pause for 1 h")]
    [InlineData(PausePreset.Indefinite, "Pause until I resume")]
    public void MenuText_IsHumanReadable(PausePreset preset, string expected)
    {
        Assert.Equal(expected, preset.MenuText());
    }

    [Theory]
    [InlineData(15, PausePreset.FifteenMinutes)]
    [InlineData(60, PausePreset.OneHour)]
    [InlineData(null, PausePreset.Indefinite)]
    [InlineData(42, PausePreset.Indefinite)] // unknown values fall back to indefinite
    public void FromMinutes_RoundTrips(int? minutes, PausePreset expected)
    {
        Assert.Equal(expected, PausePresetExtensions.FromMinutes(minutes));
    }
}
