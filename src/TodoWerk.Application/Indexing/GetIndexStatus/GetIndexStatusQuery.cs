using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Indexing.GetIndexStatus;

/// <summary>How current the signed-in user's index is, and what it is doing.</summary>
public sealed record GetIndexStatusQuery : IQuery<IndexStatusDto>;
