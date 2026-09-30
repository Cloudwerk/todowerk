namespace TodoWerk.Web.Diagnostics;

/// <summary>
/// Writes the one line an operator needs to tell Safari's refused cookie apart from an ordinary
/// signed-out visitor ([ADR-0010](../../../docs/adr/0010-teams-tab-session-and-framing.md)).
/// <para>
/// The signature is a pair of responses — an on-behalf-of exchange that succeeded, and a 401 to
/// the request immediately after it — and only the tab sees both halves. A server that watched for
/// the pair itself would be correlating a successful exchange with an anonymous 401 by timestamp,
/// which is guessing: the two arrive on different connections in the case that matters, because
/// the second one carries no cookie at all. So the tab says which request it is, in a header it
/// puts on nothing else, and this reads it.
/// </para>
/// <para>
/// Believed without being trusted, and the trust it is not given has to be paid for here rather
/// than borrowed from elsewhere. The header grants no access and is read only alongside a 401 the
/// caller already had — but an anonymous caller can produce that 401 at will, and TodoWerk's rate
/// limiter does not reach this: the authorization middleware short-circuits an unauthenticated
/// request before <c>UseRateLimiter</c> ever runs, so a 401 costs a caller nothing. Without the
/// ceiling below, the one path that lets an anonymous request decide something is written to the
/// log would also let it decide how much. What a forged line still buys is a false answer to the
/// question this exists to ask — which is why a logged line is evidence from a walk somebody is
/// performing rather than proof about an unknown visitor.
/// </para>
/// </summary>
internal sealed class TeamsBootstrapDiagnostics(
    RequestDelegate next,
    ILogger<TeamsBootstrapDiagnostics> logger)
{
    /// <summary>Comfortably longer than any real one — Safari's is about 130 characters.</summary>
    private const int MaxLoggedUserAgentLength = 256;

    /// <summary>
    /// One a second, averaged over the minute. Far above what the condition produces honestly —
    /// it is one line per tab load, per person, in a tenant whose browser refuses the cookie — and
    /// low enough that the log cannot be filled from outside.
    /// </summary>
    private const int MaxLinesPerWindow = 60;

    private const long WindowMilliseconds = 60_000;

    private readonly Lock _gate = new();

    private long _windowStarted = Environment.TickCount64;

    private int _occurrencesInWindow;

    private int _suppressedInLastWindow;

    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        if (context.Response.StatusCode != StatusCodes.Status401Unauthorized
            || !context.Request.Headers.ContainsKey(TeamsTab.BootstrapHeader))
        {
            return;
        }

        var verdict = Next(out var suppressedBefore);

        // Said first, so a ceiling that hid part of the answer says how much it hid rather than
        // leaving the lines that follow it looking like the whole of what happened.
        if (suppressedBefore > 0)
        {
            logger.LogWarning(
                "{Suppressed} further Teams tab session failures went unlogged after the ceiling of "
                + "{Limit} a minute was reached. This is the first one since, so the minute "
                + "they happened in is the one the ceiling's own warning above was written in, not "
                + "necessarily the minute before this line.",
                suppressedBefore,
                MaxLinesPerWindow);
        }

        switch (verdict)
        {
            case Verdict.Write:
                // The user agent, which nothing else here logs, because the open question this line
                // exists to answer is *which* clients refuse the cookie. Safari on the desktop is
                // the one ADR-0010 predicted; whether Teams mobile's webview behaves the same way is
                // unverified, and a card on somebody else's screen cannot report back.
                logger.LogWarning(
                    "The Teams tab's session did not survive the frame: the on-behalf-of exchange "
                    + "succeeded and {Path} immediately after it answered 401, which means this "
                    + "browser did not keep TodoWerk's session cookie inside Teams (ADR-0010). "
                    + "Not a failed sign-in — the person is being shown the card that offers them "
                    + "the browser. User agent: {UserAgent}.",
                    // The path goes in whole where the user agent is truncated, because the route
                    // table authors one and the caller authors the other: a 401 is only answered to
                    // a request that matched a route, every one of them is a literal or a
                    // {changeId:guid}, and anything else falls through to the SPA's anonymous
                    // document and is answered 200. A test pins that rather than a comment claiming
                    // it, because the day somebody adds an unconstrained route parameter is the day
                    // it stops being true.
                    context.Request.Path.Value,
                    Bounded(context.Request.Headers.UserAgent.ToString()));
                break;

            // Said once per window rather than swallowed. An operator reading these to find out
            // which clients refuse the cookie needs to know the answer in front of them is partial;
            // silence would read as "and no more happened".
            case Verdict.Capped:
                logger.LogWarning(
                    "More than {Limit} Teams tab sessions failed to survive the frame within a "
                    + "minute, so the rest of this minute's are not logged. Either a great "
                    + "many people met the blocked cookie at once, or somebody is sending the "
                    + "bootstrap header by hand — it is asserted by the caller and grants nothing, "
                    + "so this ceiling is what bounds it.",
                    MaxLinesPerWindow);
                break;
        }
    }

    /// <summary>
    /// Whether this occurrence is written, is the one that reports the ceiling, or is dropped —
    /// and, on the first occurrence of a new window, how many the previous window dropped.
    /// <para>
    /// A fixed window over a monotonic clock rather than a <c>RateLimiter</c>: the state is three
    /// integers, the middleware is a singleton so they are shared across every request, and what a
    /// limiter would add is a queue and a lease for something that never waits.
    /// </para>
    /// <para>
    /// One counter for the whole process, not one per caller. That is the ceiling's cost and it is
    /// worth naming: somebody sending the header by hand fast enough can crowd a genuine report out
    /// of the same minute. Partitioning by address would only move the problem — the partitions are
    /// chosen by the caller too — and what bounds it instead is that the ceiling is never silent
    /// about itself: the minute it starts suppressing, it says so, in the same breath as saying
    /// that somebody sending the header by hand is one of the two explanations.
    /// </para>
    /// <para>
    /// The count of what was suppressed is the weaker half, and deliberately so: it is written when
    /// the next occurrence arrives, so a burst that stops leaves it unwritten. Reporting it at the
    /// moment the window closed would mean a timer running for a diagnostic — and the line that
    /// matters, the one saying the answer in front of the reader is partial, has already been
    /// written by then.
    /// </para>
    /// </summary>
    private Verdict Next(out int suppressedInLastWindow)
    {
        var now = Environment.TickCount64;

        lock (_gate)
        {
            if (now - _windowStarted >= WindowMilliseconds)
            {
                // Minus the ceiling notice's own occurrence, which was reported rather than
                // suppressed — the line below says "further" and means it.
                _suppressedInLastWindow = Math.Max(0, _occurrencesInWindow - (MaxLinesPerWindow + 1));
                _windowStarted = now;
                _occurrencesInWindow = 0;
            }

            suppressedInLastWindow = _suppressedInLastWindow;
            _suppressedInLastWindow = 0;

            _occurrencesInWindow++;

            return _occurrencesInWindow switch
            {
                <= MaxLinesPerWindow => Verdict.Write,
                MaxLinesPerWindow + 1 => Verdict.Capped,
                _ => Verdict.Dropped,
            };
        }
    }

    /// <summary>
    /// The caller authors this string, and a header may be kilobytes long where a real user agent
    /// is not. The point of logging it is to read the client's name off the front.
    /// </summary>
    private static string Bounded(string userAgent)
    {
        if (userAgent.Length <= MaxLoggedUserAgentLength)
        {
            return userAgent;
        }

        // Back off one if the cut would land between the halves of a surrogate pair. A real user
        // agent is ASCII and never reaches this, which is precisely why the string that does is
        // one somebody wrote on purpose.
        var length = char.IsHighSurrogate(userAgent[MaxLoggedUserAgentLength - 1])
            ? MaxLoggedUserAgentLength - 1
            : MaxLoggedUserAgentLength;

        return string.Concat(userAgent.AsSpan(0, length), "…");
    }

    private enum Verdict
    {
        Write,
        Capped,
        Dropped,
    }
}
