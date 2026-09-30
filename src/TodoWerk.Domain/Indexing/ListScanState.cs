namespace TodoWerk.Domain.Indexing;

/// <summary>
/// What the index is currently doing about one list. Separate from whether the list has ever been
/// scanned to completion, which <see cref="TaskListIndexState.LastCompletedScanAt"/> records —
/// a list being delta-synced right now is still fully indexed from the last pass.
/// </summary>
public enum ListScanState
{
    /// <summary>Discovered, never read. The inventory must not imply its tasks are missing tags.</summary>
    NeverScanned = 0,

    /// <summary>A pass is running. Per ADR-0003 no write may target this list until it finishes.</summary>
    Scanning = 1,

    /// <summary>The last pass finished. The list is as fresh as its last successful sync.</summary>
    Indexed = 2,

    /// <summary>
    /// The last pass gave up on this list. Every other list in the same scan carried on — a list
    /// that fails, fails alone.
    /// </summary>
    Failed = 3,
}
