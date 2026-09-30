using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Indexing.GetIndexStatus;

internal sealed class GetIndexStatusQueryHandler(ICurrentUser currentUser, IIndexStatusReader reader)
    : IQueryHandler<GetIndexStatusQuery, IndexStatusDto>
{
    public Task<Result<IndexStatusDto>> HandleAsync(
        GetIndexStatusQuery query,
        CancellationToken cancellationToken)
    {
        var user = IndexUser.From(currentUser);

        return user is null
            ? Task.FromResult(Result.Failure<IndexStatusDto>(IndexingErrors.NotSignedIn))
            : reader.GetStatusAsync(user, cancellationToken);
    }
}
