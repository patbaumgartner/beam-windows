using Beam.Core.Display;

namespace Beam.Core.Tests;

/// <summary>
/// The persisted projector setting's enum mapping: <see cref="PresentationTopology"/>
/// (Mirror/Extend) ↔ the "Mirror"/"Extend" strings stored in settings.json.
/// </summary>
public class PresentationTopologyTests
{
    [Theory]
    [InlineData(PresentationTopology.Mirror, "Mirror")]
    [InlineData(PresentationTopology.Extend, "Extend")]
    public void ToSettingString_MapsEnum(PresentationTopology topology, string expected) =>
        Assert.Equal(expected, topology.ToSettingString());

    [Theory]
    [InlineData("Extend", PresentationTopology.Extend)]
    [InlineData("extend", PresentationTopology.Extend)]  // case-insensitive
    [InlineData("EXTEND", PresentationTopology.Extend)]
    [InlineData("Mirror", PresentationTopology.Mirror)]
    [InlineData("mirror", PresentationTopology.Mirror)]
    [InlineData(null, PresentationTopology.Mirror)]      // missing key → default
    [InlineData("", PresentationTopology.Mirror)]
    [InlineData("Sideways", PresentationTopology.Mirror)] // unknown → Mirror (default)
    public void FromSettingString_Parses_WithMirrorFallback(string? value, PresentationTopology expected) =>
        Assert.Equal(expected, PresentationTopologyExtensions.FromSettingString(value));

    [Fact]
    public void SettingString_RoundTrips() =>
        Assert.Equal(
            "Extend",
            PresentationTopologyExtensions.FromSettingString(PresentationTopology.Extend.ToSettingString()).ToSettingString());

    [Fact]
    public void MenuText_DistinguishesTheTwo()
    {
        Assert.Contains("Mirror", PresentationTopology.Mirror.MenuText());
        Assert.Contains("Extend", PresentationTopology.Extend.MenuText());
    }
}
