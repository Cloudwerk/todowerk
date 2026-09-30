using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.Application.Indexing;
using TodoWerk.Application.Indexing.GetTaskLists;
using TodoWerk.Infrastructure.Authentication;

namespace TodoWerk.Web.Endpoints;

internal static class TaskListEndpoints
{
    public static IEndpointRouteBuilder MapTaskListEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/api/task-lists",
                async (
                    IQueryHandler<GetTaskListsQuery, IReadOnlyList<TaskListDto>> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(new GetTaskListsQuery(), cancellationToken);

                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : result.Error.ToProblem();
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            .WithName("GetTaskLists");

        return app;
    }
}
