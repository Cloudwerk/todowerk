using Microsoft.Extensions.Options;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.Domain.Changes;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Changes.GetChangeQueue;

internal sealed class GetChangeQueueQueryHandler(
    ICurrentUser currentUser,
    IChangeStore changes,
    IOptions<ChangeOptions> options,
    TimeProvider timeProvider)
    : IQueryHandler<GetChangeQueueQuery, ChangeQueueDto>
{
    public async Task<Result<ChangeQueueDto>> HandleAsync(
        GetChangeQueueQuery query,
        CancellationToken cancellationToken)
    {
        var user = IndexUser.From(currentUser);

        if (user is null)
        {
            return Result.Failure<ChangeQueueDto>(ChangeErrors.NotSignedIn);
        }

        var now = timeProvider.GetUtcNow();
        var retention = options.Value.ChangeRetention;

        var records = await changes.ReadQueueAsync(user, cancellationToken);
        var mapped = records.Select(record => ChangeMapper.ToDto(record, now, retention)).ToList();

        // At most one is unfinished, and it is the one with a progress bar and a Cancel button;
        // the rest are history with an Undo beside them.
        var active = mapped.Find(change => change.State is ChangeState.Pending or ChangeState.Running);

        return Result.Success(new ChangeQueueDto(
            active,
            [.. mapped.Where(change => change != active)]));
    }
}
