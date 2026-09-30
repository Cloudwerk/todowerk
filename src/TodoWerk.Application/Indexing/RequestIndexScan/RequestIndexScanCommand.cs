using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Indexing.RequestIndexScan;

/// <summary>
/// Asks for the signed-in user's index to be brought up to date.
/// </summary>
/// <param name="TaskListId">One list, or null for every list.</param>
/// <param name="FullRescan">
/// Read everything again rather than only what changed. The button behind this exists because a
/// delta stream can only be trusted as far as Graph's tokens are, and the answer to "the numbers
/// look wrong" has to be something a user can press.
/// </param>
public sealed record RequestIndexScanCommand(string? TaskListId, bool FullRescan)
    : ICommand<IndexScanRequestedDto>;

/// <param name="Queued">
/// False when a scan was already in flight. Not a failure: asking twice is the same as asking
/// once, and the UI says "already running" rather than showing an error.
/// </param>
public sealed record IndexScanRequestedDto(bool Queued);
