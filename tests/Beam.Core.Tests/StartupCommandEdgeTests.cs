using Beam.Core.Startup;

namespace Beam.Core.Tests;

/// <summary>
/// Adversarial cases for the HKCU Run value builder/matcher: quotes, casing,
/// trailing args, whitespace, null/empty. A mismatch here silently breaks
/// "start at sign-in".
/// </summary>
public class StartupCommandEdgeTests
{
    private const string Exe = @"C:\Program Files\Beam\Beam.exe";

    [Fact]
    public void ValueFor_Bare_Quoted()
    {
        Assert.Equal("\"" + Exe + "\"", StartupCommand.ValueFor(Exe, launchHidden: false));
    }

    [Fact]
    public void ValueFor_Hidden_Quoted()
    {
        Assert.Equal("\"" + Exe + "\" --hidden", StartupCommand.ValueFor(Exe));
    }

    [Fact]
    public void IsAutoStartValue_BareAndHidden_BothMatch()
    {
        Assert.True(StartupCommand.IsAutoStartValue(Exe, $"\"{Exe}\""));
        Assert.True(StartupCommand.IsAutoStartValue(Exe, $"\"{Exe}\" --hidden"));
    }

    [Fact]
    public void IsAutoStartValue_CaseInsensitive()
    {
        Assert.True(StartupCommand.IsAutoStartValue(
            @"c:\program files\beam\beam.exe", "\"C:\\PROGRAM FILES\\BEAM\\BEAM.EXE\""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAutoStartValue_BlankStored_False(string stored)
    {
        Assert.False(StartupCommand.IsAutoStartValue(Exe, stored));
    }

    [Fact]
    public void IsAutoStartValue_BlankExe_False()
    {
        Assert.False(StartupCommand.IsAutoStartValue("", "\"anything\""));
        Assert.False(StartupCommand.IsAutoStartValue("   ", "\"anything\""));
    }

    [Fact]
    public void IsAutoStartValue_NullStored_False()
    {
        Assert.False(StartupCommand.IsAutoStartValue(Exe, null));
    }

    [Theory]
    [InlineData("\"C:\\Other\\Beam.exe\"")]
    [InlineData("C:\\Program Files\\Beam\\Beam.exe")]          // unquoted
    [InlineData("\"C:\\Program Files\\Beam\\Beam.exe\" extra")]
    [InlineData("\"C:\\Program Files\\Beam\\Beam.exe\" --hidden --minimized")] // extra args
    [InlineData("\"Beam.exe\"")]
    public void IsAutoStartValue_NearMisses_False(string stored)
    {
        Assert.False(StartupCommand.IsAutoStartValue(Exe, stored));
    }

    [Fact]
    public void ValueFor_RoundTrips_ThroughMatcher()
    {
        foreach (bool hidden in new[] { true, false })
        {
            string v = StartupCommand.ValueFor(Exe, hidden);
            Assert.True(StartupCommand.IsAutoStartValue(Exe, v), $"roundtrip failed for hidden={hidden}");
        }
    }

    [Fact]
    public void ValueFor_PathWithSpaces_IsQuoted()
    {
        string v = StartupCommand.ValueFor(@"C:\Users\test user\AppData\Local\Programs\Beam\Beam.exe");
        Assert.StartsWith("\"", v);
        Assert.Contains("\" --hidden", v);
    }
}
