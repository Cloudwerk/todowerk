using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.Application.Changes;
using TodoWerk.Application.Changes.CancelChange;
using TodoWerk.Application.Changes.ConfirmChange;
using TodoWerk.Application.Changes.GetChangeQueue;
using TodoWerk.Application.Changes.PreviewChange;
using TodoWerk.Application.Changes.UndoChange;
using TodoWerk.Infrastructure.Authentication;
using TodoWerk.Web.Security;

namespace TodoWerk.Web.Endpoints;

/// <summary>
/// Everything a Change is: previewed, confirmed, watched, stopped, taken back. Every one of these
/// is a POST behind the antiforgery gate except the queue itself — including the preview, which
/// writes nothing but carries a list of Hashtag keys that has no business in a query string.
/// </summary>
internal static class ChangeEndpoints
{
    public static IEndpointRouteBuilder MapChangeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/api/changes/preview",
                async (
                    PreviewChangeCommand command,
                    ICommandHandler<PreviewChangeCommand, ChangePreviewDto> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(command, cancellationToken);

                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            .ValidateAntiforgery()
            .WithName("PreviewChange");

        app.MapPost(
                "/api/changes",
                async (
                    ConfirmChangeCommand command,
                    ICommandHandler<ConfirmChangeCommand, ChangeDto> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(command, cancellationToken);

                    // Accepted, not Created: the Change is queued, and nothing in anybody's
                    // mailbox has changed yet. No Location either — there is no per-Change
                    // resource to point at, because the client watches the whole queue rather
                    // than one row, and a header naming an address that answers 404 is worse
                    // than no header.
                    return result.IsSuccess
                        ? Results.Accepted(value: result.Value)
                        : result.Error.ToProblem();
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            .ValidateAntiforgery()
            .WithName("ConfirmChange");

        app.MapGet(
                "/api/changes",
                async (
                    IQueryHandler<GetChangeQueueQuery, ChangeQueueDto> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(new GetChangeQueueQuery(), cancellationToken);

                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            .WithName("GetChangeQueue");

        app.MapPost(
                "/api/changes/{changeId:guid}/cancel",
                async (
                    Guid changeId,
                    ICommandHandler<CancelChangeCommand, ChangeDto> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(new CancelChangeCommand(changeId), cancellationToken);

                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            .ValidateAntiforgery()
            .WithName("CancelChange");

        app.MapPost(
                "/api/changes/{changeId:guid}/undo",
                async (
                    Guid changeId,
                    ICommandHandler<UndoChangeCommand, ChangeDto> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(new UndoChangeCommand(changeId), cancellationToken);

                    // The answer is the new Change that will put things back, not the old one.
                    return result.IsSuccess
                        ? Results.Accepted(value: result.Value)
                        : result.Error.ToProblem();
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            .ValidateAntiforgery()
            .WithName("UndoChange");

        return app;
    }
}
