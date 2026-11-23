using System.Text.Json;

namespace Beam.Cli.Tests;

public class CliValidationTests
{
    [Theory]
    [InlineData("apply", "garbage", "--json")]
    [InlineData("apply", "--dry-run", "--nightlight", "--json")]
    [InlineData("restore", "--dry-run", "--json")]
    [InlineData("nightlight", "on", "extra", "--json")]
    [InlineData("dpi", "read", "extra", "--json")]
    [InlineData("dpi", "set", "150", "garbage", "--json")]
    [InlineData("dpi", "set", "101", "--json")]
    [InlineData("status", "--device", @"\\.\DISPLAY1", "--json")]
    [InlineData("status", "--dry-run", "--json")]
    [InlineData("help", "--verify", "--json")]
    [InlineData("dpi", "read", "--device", "--json")]
    public void MalformedCommands_ReturnInvalidArgumentsJson_WithoutLockOrMutations(params string[] args)
    {
        var h = new CliHarness(CliTopologies.Mirrored());
        int locks = 0;
        h.LockFactory = () =>
        {
            locks++;
            return NoopLock.Instance;
        };

        int code = h.Run(args);

        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Equal(0, locks);
        Assert.Equal(0, h.Mode.ApplyCalls);
        Assert.Equal(0, h.NightLight.SetActiveCalls);
        Assert.Empty(h.Dpi.RequestedPercents);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("error").GetString()));
    }

    [Fact]
    public void UnexpectedFailure_WithJson_ReturnsJsonFailure()
    {
        var h = new CliHarness(CliTopologies.Extended());
        h.Topology.ThrowOnQuery = new InvalidOperationException("display query failed");

        int code = h.Run("status", "--json");

        Assert.Equal(ExitCodes.Failure, code);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("display query failed", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void MissingDevice_WithJson_ReturnsJsonFailure()
    {
        var h = new CliHarness(CliTopologies.Extended());

        int code = h.Run("dpi", "read", "--device", @"\\.\DISPLAY9", "--json");

        Assert.Equal(ExitCodes.Failure, code);
        using JsonDocument doc = JsonDocument.Parse(h.Output);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("DISPLAY9", doc.RootElement.GetProperty("error").GetString());
    }
}
