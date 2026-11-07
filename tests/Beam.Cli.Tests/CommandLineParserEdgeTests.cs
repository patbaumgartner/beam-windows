using Beam.Cli;

namespace Beam.Cli.Tests;

/// <summary>
/// Adversarial parser cases: unknown options, missing option values, repeated flags,
/// empty tokens. The parser is the first code every invocation touches.
/// </summary>
public class CommandLineParserEdgeTests
{
    private static CliOptions P(params string[] rest) => CommandLineParser.Parse("status", rest);

    [Fact]
    public void Parse_EmptyRest_Defaults()
    {
        CliOptions o = P();
        Assert.Equal("status", o.Command);
        Assert.Empty(o.Positionals);
        Assert.False(o.Json);
        Assert.False(o.DryRun);
        Assert.False(o.NightLight);
        Assert.False(o.Verify);
        Assert.Null(o.Device);
    }

    [Fact]
    public void Parse_AllFlags_Set()
    {
        CliOptions o = P("--json", "--dry-run", "--nightlight", "--verify");
        Assert.True(o.Json && o.DryRun && o.NightLight && o.Verify);
    }

    [Fact]
    public void Parse_RepeatedFlags_Set()
    {
        CliOptions o = P("--json", "--json");
        Assert.True(o.Json);
    }

    [Fact]
    public void Parse_DeviceConsumesNextToken()
    {
        CliOptions o = P("--device", @"\\.\DISPLAY2", "--json");
        Assert.Equal(@"\\.\DISPLAY2", o.Device);
        Assert.True(o.Json);
    }

    [Fact]
    public void Parse_DeviceAtEnd_Throws()
    {
        Assert.Throws<CliUsageException>(() => P("--device"));
    }

    [Fact]
    public void Parse_DeviceValueLookingLikeFlag_Consumed()
    {
        // "--device --json" consumes --json as the device value (documented quirk).
        CliOptions o = P("--device", "--json");
        Assert.Equal("--json", o.Device);
        Assert.False(o.Json);
    }

    [Theory]
    [InlineData("--Json")]
    [InlineData("--JSON")]
    [InlineData("--json ")]
    [InlineData("-json")]
    [InlineData("--")]
    [InlineData("---json")]
    [InlineData("--night-light")]
    [InlineData("--nightlight=true")]
    [InlineData("--dryrun")]
    public void Parse_NearMissFlags_AreRejectedOrPositional(string token)
    {
        // Every near-miss is either rejected or lands in positionals — never silently
        // treated as a known flag.
        var ex = Record.Exception(() => P(token));
        if (ex is null)
        {
            CliOptions o = P(token);
            Assert.Contains(token, o.Positionals);
        }
        else
        {
            Assert.IsType<CliUsageException>(ex);
        }
    }

    [Fact]
    public void Parse_MixedPositionalsAndFlags_KeepsOrder()
    {
        CliOptions o = CommandLineParser.Parse("dpi", new List<string> { "set", "--json", "150" });
        Assert.Equal(new[] { "set", "150" }, o.Positionals);
        Assert.True(o.Json);
    }

    [Fact]
    public void Parse_EmptyStringToken_IsPositional()
    {
        CliOptions o = P("");
        Assert.Contains("", o.Positionals);
    }
}
