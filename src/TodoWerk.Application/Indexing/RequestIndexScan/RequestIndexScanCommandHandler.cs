using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.Domain.Indexing;
using TodoWerk.SharedKernel;
using TodoWerk.Application.Abstractions.Indexing;

namespace TodoWerk.Application.Indexing.RequestIndexScan;

internal sealed class RequestIndexScanCommandHandler(ICurrentUser currentUser, IIndexScanScheduler scheduler)
    : ICommandHandler<RequestIndexScanCommand, IndexScanRequestedDto>
{
    public async Task<Result<IndexScanRequestedDto>> HandleAsync(
        RequestIndexScanCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = IndexUser.From(currentUser);

        if (user is null)
        {
            return Result.Failure<IndexScanRequestedDto>(IndexingErrors.NotSignedIn);
        }

        if (command.TaskListId is { Length: > IndexingLimits.GraphIdLength })
        {
            return Result.Failure<IndexScanRequestedDto>(IndexingErrors.InvalidTaskListId);
        }

        var mode = command.FullRescan ? IndexScanMode.Full : IndexScanMode.Delta;

        var queued = await scheduler.RequestScanAsync(user, mode, command.TaskListId, cancellationToken);

        return Result.Success(new IndexScanRequestedDto(queued));
    }
}
