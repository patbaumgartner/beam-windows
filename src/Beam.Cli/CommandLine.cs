namespace Beam.Cli;

/// <summary>Exit codes shared by every command (documented in <c>beam help</c>).</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int InvalidArguments = 2;
    public const int NoProjector = 3;
    public const int Unsupported = 4;
}

/// <summary>Thrown for malformed command lines; maps to <see cref="ExitCodes.InvalidArguments"/>.</summary>
public sealed class CliUsageException : Exception
{
    public CliUsageException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A parsed command line. <see cref="Positionals"/> are the arguments after the command
/// that are not flags (for example <c>set 150</c> for <c>beam dpi set 150</c>).
/// </summary>
public sealed record CliOptions(
    string Command,
    IReadOnlyList<string> Positionals,
    bool Json,
    bool DryRun,
    bool NightLight,
    bool Verify,
    string? Device);

/// <summary>Pure parser for Beam's command line. No I/O, no state.</summary>
public static class CommandLineParser
{
    public static CliOptions Parse(string command, IReadOnlyList<string> rest)
    {
        var positionals = new List<string>();
        bool json = false;
        bool dryRun = false;
        bool nightLight = false;
        bool verify = false;
        string? device = null;

        for (int i = 0; i < rest.Count; i++)
        {
            string token = rest[i];
            switch (token)
            {
                case "--json":
                    json = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--nightlight":
                    nightLight = true;
                    break;
                case "--verify":
                    verify = true;
                    break;
                case "--device":
                    if (i + 1 >= rest.Count)
                    {
                        throw new CliUsageException("--device requires a value, e.g. --device \\\\.\\DISPLAY2.");
                    }
                    device = rest[++i];
                    break;
                default:
                    if (token.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new CliUsageException($"Unknown option: {token}");
                    }
                    positionals.Add(token);
                    break;
            }
        }

        return new CliOptions(command, positionals, json, dryRun, nightLight, verify, device);
    }
}
