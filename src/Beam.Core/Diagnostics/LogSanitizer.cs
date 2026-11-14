namespace Beam.Core.Diagnostics;

/// <summary>
/// C4/A4: strips secrets (machine name, user name, profile paths) from log text before it
/// leaves the machine — e.g. in the diagnostics export bundle copied to the Desktop and
/// attached to bug reports. Pure function: the caller supplies the secrets to remove, so
/// Core never touches <c>Environment</c> and tests stay deterministic.
/// </summary>
public static class LogSanitizer
{
    /// <summary>Placeholder substituted for each secret.</summary>
    public const string Redacted = "[redacted]";

    /// <summary>
    /// Replaces every occurrence of each secret (ordinal, case-insensitive on Windows paths)
    /// with <see cref="Redacted"/>. Secrets shorter than 3 chars are ignored (too risky to
    /// blanket-replace, e.g. "C:"). Also collapses any <c>C:\Users\&lt;secret&gt;</c>-style
    /// profile path when the user name is passed in.
    /// </summary>
    public static string Sanitize(string? text, params string[] secrets)
    {
        if (string.IsNullOrEmpty(text) || secrets.Length == 0)
        {
            return text ?? string.Empty;
        }

        string result = text;
        foreach (string secret in secrets)
        {
            if (string.IsNullOrEmpty(secret) || secret.Length < 3)
            {
                continue;
            }

            result = result.Replace(secret, Redacted, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    /// <summary>
    /// True when the text contains none of the given secrets — used by tests and by the
    /// export bundle to assert the sanitized output is safe to share.
    /// </summary>
    public static bool IsClean(string? text, params string[] secrets)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        foreach (string secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret) && secret.Length >= 3
                && text.Contains(secret, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
