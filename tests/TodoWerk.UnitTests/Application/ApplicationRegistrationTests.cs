using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TodoWerk.Application;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.Application.Indexing;
using TodoWerk.Application.Indexing.GetTaskLists;
using TodoWerk.SharedKernel;
using Xunit;

namespace TodoWerk.UnitTests.Application;

public sealed class ApplicationRegistrationTests
{
    [Fact]
    public void AddApplication_RegistersQueryHandlersBehindTheLoggingDecorator()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ITaskListsReader, StubTaskListsReader>();
        services.AddSingleton<ICurrentUser, StubCurrentUser>();

        services.AddApplication();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetTaskListsQuery, IReadOnlyList<TaskListDto>>>();

        Assert.StartsWith("LoggingQueryHandlerDecorator", handler.GetType().Name, StringComparison.Ordinal);
    }

    private sealed class StubTaskListsReader : ITaskListsReader
    {
        public Task<Result<IReadOnlyList<TaskListDto>>> GetTaskListsAsync(
            IndexUser user,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<TaskListDto>>([]));
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? ObjectId => "user";

        public string? TenantId => "tenant";

        public string? DisplayName => "Signed In";

        public string? Username => "signed-in@todowerk.test";
    }
}
