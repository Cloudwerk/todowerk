namespace TodoWerk.Domain.Indexing;

/// <summary>Where a queued scan has got to.</summary>
public enum IndexScanState
{
    /// <summary>Queued. A second request for the same user finds this one and does not add another.</summary>
    Pending = 0,

    /// <summary>Claimed by a worker. Survives a restart as this state, which the worker reclaims.</summary>
    Running = 1,

    Completed = 2,

    /// <summary>The scan could not run at all. Lists that failed individually are recorded per list.</summary>
    Failed = 3,
}
