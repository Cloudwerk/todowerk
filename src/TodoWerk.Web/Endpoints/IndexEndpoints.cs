using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.Application.Indexing;
using TodoWerk.Application.Indexing.GetHashtagInventory;
using TodoWerk.Application.Indexing.GetIndexStatus;
using TodoWerk.Application.Indexing.RequestIndexScan;
using TodoWerk.Infrastructure.Authentication;
using TodoWerk.Web.Security;

namespace TodoWerk.Web.Endpoints;

internal static class IndexEndpoints
{
    public static IEndpointRouteBuilder MapIndexEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/api/index/status",
                async (
                    IQueryHandler<GetIndexStatusQuery, IndexStatusDto> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(new GetIndexStatusQuery(), cancellationToken);

                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            .WithName("GetIndexStatus");

        // POST, and antiforgery-protected: asking for a scan starts minutes of background work
        // against someone's mailbox, so it must not be reachable by a cross-site GET.
        app.MapPost(
                "/api/index/scan",
                async (
                    RequestIndexScanCommand command,
                    ICommandHandler<RequestIndexScanCommand, IndexScanRequestedDto> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(command, cancellationToken);

                    // Accepted, not Ok: the work has been queued, not done. "Already running" is
                    // the same answer — the user asked for the index to be current and it is on
                    // its way there.
                    return result.IsSuccess
                        ? Results.Accepted(value: result.Value)
                        : result.Error.ToProblem();
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            .ValidateAntiforgery()
            .WithName("RequestIndexScan");

        // The inventory itself. Sorting, filtering and paging are query parameters because they
        // are answered in SQL — the client drives the query rather than sorting a copy.
        app.MapGet(
                "/api/hashtags",
                async (
                    IQueryHandler<GetHashtagInventoryQuery, HashtagInventoryPage> handler,
                    CancellationToken cancellationToken,
                    string? search = null,
                    HashtagInventoryFilter filter = HashtagInventoryFilter.None,
                    HashtagInventorySort sort = HashtagInventorySort.TaskCount,
                    bool descending = true,
                    int page = 1,
                    int pageSize = 50) =>
                {
                    var result = await handler.HandleAsync(
                        new GetHashtagInventoryQuery(search, filter, sort, descending, page, pageSize),
                        cancellationToken);

                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            .WithName("GetHashtagInventory");

        return app;
    }
}
