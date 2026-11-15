using Beam.Core.Diagnostics;

namespace Beam.Core.Tests;

/// <summary>C4/A4: the sanitizer that keeps machine/user names out of exported diagnostics.</summary>
public class LogSanitizerTests
{
    [Fact]
    public void Sanitize_ReplacesSecrets_CaseInsensitive()
    {
        string text = @"log from C:\Users\patbaum\AppData\Local\Beam on machine PAT-PC";

        string result = LogSanitizer.Sanitize(text, "patbaum", "PAT-PC");

        Assert.Equal(@"log from C:\Users\[redacted]\AppData\Local\Beam on machine [redacted]", result);
        Assert.True(LogSanitizer.IsClean(result, "patbaum", "PAT-PC"));
    }

    [Fact]
    public void Sanitize_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal("", LogSanitizer.Sanitize(null, "x"));
        Assert.Equal("", LogSanitizer.Sanitize("", "x"));
        Assert.Equal("plain", LogSanitizer.Sanitize("plain"));
    }

    [Fact]
    public void Sanitize_IgnoresTinySecrets()
    {
        // Secrets shorter than 3 chars are too risky to blanket-replace ("C", "ab").
        Assert.Equal("C:\\temp", LogSanitizer.Sanitize("C:\\temp", "C", "ab"));
    }

    [Fact]
    public void IsClean_DetectsLeakedSecret()
    {
        Assert.False(LogSanitizer.IsClean("path contains patbaum here", "patbaum"));
        Assert.False(LogSanitizer.IsClean("path contains PATBAUM upper", "patbaum")); // case-insensitive
        Assert.True(LogSanitizer.IsClean("nothing sensitive", "patbaum"));
        Assert.True(LogSanitizer.IsClean(null, "patbaum"));
    }

    [Fact]
    public void IsClean_IgnoresTinySecrets_SameGuardAsSanitize()
    {
        // Secrets shorter than 3 chars are never treated as sensitive ("C", "ab")…
        Assert.True(LogSanitizer.IsClean(@"C:\temp", "C", "ab"));
        // …but a 3-char secret still counts.
        Assert.False(LogSanitizer.IsClean("value ab1 here", "ab1"));
    }

    [Fact]
    public void Sanitize_ReplacesEveryOccurrence()
    {
        string result = LogSanitizer.Sanitize("patbaum/patbaum/patbaum", "patbaum");

        Assert.Equal("[redacted]/[redacted]/[redacted]", result);
    }
}
