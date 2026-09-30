using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Indexing;
using TodoWerk.Application.Indexing.GetTaskLists;
using TodoWerk.SharedKernel;
using Xunit;

namespace TodoWerk.UnitTests.Indexing;

public sealed class GetTaskListsQueryHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenReaderSucceeds_ReturnsTheTaskLists()
    {
        IReadOnlyList<TaskListDto> lists =
        [
            new("list-1", "Tasks", TaskListKind.Default, IsShared: false, IsOwner: true),
            new("list-2", "Groceries", TaskListKind.Normal, IsShared: false, IsOwner: true),
        ];
        var handler = CreateHandler(Result.Success(lists));

        var result = await handler.HandleAsync(new GetTaskListsQuery(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(lists, result.Value);
    }

    [Fact]
    public async Task HandleAsync_WhenReaderFails_PassesTheErrorThrough()
    {
        var error = Error.Unauthorized("Graph.Reconnect", "The Microsoft Graph connection has expired.");
        var handler = CreateHandler(Result.Failure<IReadOnlyList<TaskListDto>>(error));

        var result = await handler.HandleAsync(new GetTaskListsQuery(), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    /// <summary>
    /// The reader is asked for a named user's lists, never for "whoever is signed in" — the same
    /// call the background scan makes. A principal with no object id therefore has no index to
    /// read, and must not reach Graph at all.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenThePrincipalCarriesNoObjectId_DoesNotCallGraph()
    {
        var reader = new StubTaskListsReader(Result.Success<IReadOnlyList<TaskListDto>>([]));
        var handler = new GetTaskListsQueryHandler(new StubCurrentUser(ObjectId: null), reader);

        var result = await handler.HandleAsync(new GetTaskListsQuery(), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(IndexingErrors.NotSignedIn, result.Error);
        Assert.Null(reader.AskedFor);
    }

    [Fact]
    public async Task HandleAsync_AsksForTheSignedInUsersLists()
    {
        var reader = new StubTaskListsReader(Result.Success<IReadOnlyList<TaskListDto>>([]));
        var handler = new GetTaskListsQueryHandler(new StubCurrentUser(), reader);

        await handler.HandleAsync(new GetTaskListsQuery(), TestContext.Current.CancellationToken);

        Assert.Equal(new IndexUser("tenant-id", "user-id"), reader.AskedFor);
    }

    private static GetTaskListsQueryHandler CreateHandler(Result<IReadOnlyList<TaskListDto>> result) =>
        new(new StubCurrentUser(), new StubTaskListsReader(result));

    private sealed class StubTaskListsReader(Result<IReadOnlyList<TaskListDto>> result) : ITaskListsReader
    {
        public IndexUser? AskedFor { get; private set; }

        public Task<Result<IReadOnlyList<TaskListDto>>> GetTaskListsAsync(
            IndexUser user,
            CancellationToken cancellationToken)
        {
            AskedFor = user;

            return Task.FromResult(result);
        }
    }

    private sealed record StubCurrentUser(string? ObjectId = "user-id") : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? TenantId => "tenant-id";

        public string? DisplayName => "Signed In";

        public string? Username => "signed-in@todowerk.test";
    }
}
