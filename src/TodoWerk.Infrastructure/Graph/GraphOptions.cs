namespace TodoWerk.Infrastructure.Graph;

/// <summary>
/// How TodoWerk behaves towards Microsoft Graph when Graph pushes back. Its own section rather
/// than the indexing one it started in: a Change writes through the same gateway and waits out the
/// same throttling, and a retry budget that lived under <c>Indexing</c> would read as though it
/// did not apply to writes.
/// </summary>
public sealed class GraphOptions
{
    public const string SectionName = "Graph";

    /// <summary>
    /// How many times a throttled Graph request is retried before the caller is failed. Graph's
    /// <c>Retry-After</c> decides the waiting; this decides when to stop waiting.
    /// </summary>
    public int MaxThrottleRetries { get; set; } = 5;

    /// <summary>
    /// Ceiling on one <c>Retry-After</c> wait. Graph can ask for minutes; a caller that obeys
    /// without a limit is a hung scan nobody can see.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(2);
}
