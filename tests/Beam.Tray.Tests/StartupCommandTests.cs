using Beam.Tray;

namespace Beam.Tray.Tests;

public class StartupCommandTests
{
    private const string Exe = @"C:\Program Files\Beam\Beam.exe";

    [Fact]
    public void IsAutoStartValue_MatchesBareQuotedPath()
    {
        Assert.True(StartupCommand.IsAutoStartValue(Exe, @"""" + Exe + @""""));
    }

    [Fact]
    public void IsAutoStartValue_MatchesHiddenForm_InstallerWritesThis()
    {
        // The installer's "Launch at sign-in" task writes "<exe>" --hidden.
        Assert.True(StartupCommand.IsAutoStartValue(Exe, @"""" + Exe + @""" --hidden"));
    }

    [Fact]
    public void IsAutoStartValue_IsCaseInsensitive()
    {
        Assert.True(StartupCommand.IsAutoStartValue(Exe, @"""" + Exe.ToLowerInvariant() + @""""));
        Assert.True(StartupCommand.IsAutoStartValue(Exe, @"""" + Exe + @""" --HIDDEN"));
    }

    [Fact]
    public void IsAutoStartValue_RejectsDifferentExecutable()
    {
        Assert.False(StartupCommand.IsAutoStartValue(Exe, "\"C:\\Other\\app.exe\""));
        Assert.False(StartupCommand.IsAutoStartValue(Exe, null));
    }

    [Fact]
    public void IsAutoStartValue_RejectsNullOrBlank()
    {
        Assert.False(StartupCommand.IsAutoStartValue(Exe, null));
        Assert.False(StartupCommand.IsAutoStartValue(Exe, "  "));
        Assert.False(StartupCommand.IsAutoStartValue("", "\"x\""));
        Assert.False(StartupCommand.IsAutoStartValue("  ", "\"x\""));
    }

    [Fact]
    public void IsAutoStartValue_RejectsArgumentsBeyondHidden()
    {
        // A value with any argument other than --hidden is not our entry (e.g. a
        // stale value pointing to some other tool, or a hand-edited command line).
        Assert.False(StartupCommand.IsAutoStartValue(Exe, @"""" + Exe + @""" --something"));
    }

    [Fact]
    public void ValueFor_QuotesPathAndAddsHiddenByDefault()
    {
        Assert.Equal(@"""" + Exe + @""" --hidden", StartupCommand.ValueFor(Exe));
    }

    [Fact]
    public void ValueFor_CanOmitHidden()
    {
        Assert.Equal(@"""" + Exe + @"""", StartupCommand.ValueFor(Exe, launchHidden: false));
    }

    [Fact]
    public void ValueFor_RoundTripsWithIsAutoStartValue()
    {
        foreach (bool hidden in new[] { true, false })
        {
            string value = StartupCommand.ValueFor(Exe, hidden);
            Assert.True(StartupCommand.IsAutoStartValue(Exe, value), $"value: {value}");
        }
    }
}
