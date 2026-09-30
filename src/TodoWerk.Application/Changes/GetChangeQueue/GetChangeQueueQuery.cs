using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Changes.GetChangeQueue;

/// <summary>The running Change and the history beside it — what the Workbench polls.</summary>
public sealed record GetChangeQueueQuery : IQuery<ChangeQueueDto>;
