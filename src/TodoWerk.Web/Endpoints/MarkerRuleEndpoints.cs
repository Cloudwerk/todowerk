using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.Application.Markers;
using TodoWerk.Application.Markers.CreateMarkerRule;
using TodoWerk.Application.Markers.DeleteMarkerRule;
using TodoWerk.Application.Markers.GetMarkerRules;
using TodoWerk.Application.Markers.UpdateMarkerRule;
using TodoWerk.Infrastructure.Authentication;
using TodoWerk.Web.Security;

namespace TodoWerk.Web.Endpoints;

/// <summary>
/// A person's Marker Rules: read, added, changed, reordered, deleted. None of these writes a task
/// title — applying rules is a Change, and goes through <see cref="ChangeEndpoints"/> like the
/// other three (ADR-0014).
/// </summary>
internal static class MarkerRuleEndpoints
{
    public static IEndpointRouteBuilder MapMarkerRuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/api/marker-rules",
                async (
                    IQueryHandler<GetMarkerRulesQuery, MarkerRuleListDto> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(new GetMarkerRulesQuery(), cancellationToken);

                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            .WithName("GetMarkerRules");

        app.MapPost(
                "/api/marker-rules",
                async (
                    CreateMarkerRuleCommand command,
                    ICommandHandler<CreateMarkerRuleCommand, MarkerRuleDto> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(command, cancellationToken);

                    // Created, with the rule itself: unlike a Change, this is a resource and it
                    // exists the moment the call returns. Nothing in anybody's mailbox moved.
                    return result.IsSuccess
                        ? Results.Created($"/api/marker-rules/{result.Value.Id}", result.Value)
                        : result.Error.ToProblem();
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            .ValidateAntiforgery()
            .WithName("CreateMarkerRule");

        app.MapPatch(
                "/api/marker-rules/{ruleId:guid}",
                async (
                    Guid ruleId,
                    UpdateMarkerRuleRequest request,
                    ICommandHandler<UpdateMarkerRuleCommand, MarkerRuleDto> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(
                        new UpdateMarkerRuleCommand(ruleId, request.Marker, request.Move),
                        cancellationToken);

                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            .ValidateAntiforgery()
            .WithName("UpdateMarkerRule");

        app.MapDelete(
                "/api/marker-rules/{ruleId:guid}",
                async (
                    Guid ruleId,
                    ICommandHandler<DeleteMarkerRuleCommand> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(new DeleteMarkerRuleCommand(ruleId), cancellationToken);

                    return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            .ValidateAntiforgery()
            .WithName("DeleteMarkerRule");

        return app;
    }
}

/// <summary>
/// The body of a PATCH. Separate from the command because the rule's id belongs in the route, and
/// a body that could name a different one would be a way to edit a rule the URL did not.
/// </summary>
internal sealed record UpdateMarkerRuleRequest(string? Marker, MarkerRuleMove? Move);
