using System.Text;

namespace TodoWerk.Web.Diagnostics;

/// <summary>
/// Values written to the log that somebody outside this application authored: an <c>error</c> code
/// on a redirect, an <c>error_description</c>, a tenant named in a callback.
/// <para>
/// Two hazards, and both are the caller's to choose: a newline turns one log line into two, so a
/// crafted description can write a line that looks like this application's own, and an
/// unbounded string can push everything around it out of a bounded log. So control characters
/// become a single visible mark and the value is cut to a length a real code never reaches.
/// </para>
/// <para>
/// Encoding, not validation. Nothing here decides anything — these values are observability over
/// untrusted input, never proof and never a trigger for behaviour.
/// </para>
/// </summary>
internal static class LogSafe
{
    /// <summary>
    /// Long enough for a whole AADSTS sentence, which is what makes these lines worth reading, and
    /// far short of what an attacker would need to bury the lines around it.
    /// </summary>
    internal const int MaxLength = 400;

    /// <summary>Stands in for whatever was removed, so a doctored value looks doctored.</summary>
    private const char Replacement = '·';

    /// <summary>
    /// One value, safe to put in a log line. Null and empty come back as null, which the logger
    /// renders as <c>(null)</c> — a distinction worth keeping, because "the parameter was absent"
    /// and "the parameter was empty" are different answers to "what did Microsoft send?".
    /// </summary>
    internal static string? Scalar(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var bounded = Bounded(value);
        var safe = new StringBuilder(bounded.Length);

        foreach (var character in bounded)
        {
            // Every control character, not only CR and LF: a lone CR splits a line in most
            // readers, and the rest have no meaning in a code or a message either.
            safe.Append(char.IsControl(character) ? Replacement : character);
        }

        return safe.ToString();
    }

    private static string Bounded(string value)
    {
        if (value.Length <= MaxLength)
        {
            return value;
        }

        // Back off one if the cut would land between the halves of a surrogate pair — the same
        // care the Teams bootstrap diagnostic takes with a user agent, and for the same reason:
        // the string long enough to reach this is one somebody wrote on purpose.
        var length = char.IsHighSurrogate(value[MaxLength - 1]) ? MaxLength - 1 : MaxLength;

        return string.Concat(value.AsSpan(0, length), "…");
    }
}
