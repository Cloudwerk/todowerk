namespace TodoWerk.Application.Changes;

/// <summary>
/// How Changes are bounded and how quickly they start.
/// <para>
/// In the Application layer rather than beside <c>IndexingOptions</c> in Infrastructure, because
/// unlike those settings these are read on both sides: the handler that refuses a plan past the
/// ceiling is here, and the worker that drains the queue is there. Bound in Infrastructure's
/// composition all the same.
/// </para>
/// </summary>
public sealed class ChangeOptions
{
    public const string SectionName = "Changes";

    /// <summary>
    /// How often the worker looks for confirmed Changes. Shorter than the scan worker's fifteen
    /// seconds on purpose: a confirmed Change is meant to start immediately rather than
    /// eventually, and somebody is watching it.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long a Change may stay <c>Running</c> without a lease renewal before the worker assumes
    /// the process that claimed it is gone. Reclaiming one that is still running would PATCH the
    /// same tasks twice — harmless for the title, not harmless for the journal.
    /// </summary>
    public TimeSpan ChangeTimeout { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// The most tasks one Change may cover. A Change holds the user's exclusivity for its whole
    /// run (ADR-0006), so an unbounded one is an unbounded block on their index. Past this the
    /// Change is refused with the count rather than chunked into Changes nobody confirmed.
    /// </summary>
    public int MaxTasksPerChange { get; set; } = 1000;

    /// <summary>
    /// How many source Hashtags one Merge may fold together. Bounded because the sources are
    /// stored as one text column, and because ten tags folded into one in a single confirmed
    /// operation is already past what anybody previews carefully.
    /// </summary>
    public int MaxSourcesPerChange { get; set; } = 10;

    /// <summary>
    /// How long a finished Change and its journal are kept. Deliberately longer than the scan
    /// queue's seven days: a queue is not an audit trail, and the journal is the only record of a
    /// task's previous title (ADR-0006). Undo is offered inside the same window, so shortening
    /// this shortens undo.
    /// </summary>
    public TimeSpan ChangeRetention { get; set; } = TimeSpan.FromDays(30);
}
