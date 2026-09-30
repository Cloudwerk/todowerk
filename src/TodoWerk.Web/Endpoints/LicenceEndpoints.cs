using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.Application.Licensing;
using TodoWerk.Application.Licensing.GetLicence;
using TodoWerk.Infrastructure.Authentication;

namespace TodoWerk.Web.Endpoints;

internal static class LicenceEndpoints
{
    public static IEndpointRouteBuilder MapLicenceEndpoints(this IEndpointRouteBuilder app)
    {
        // Everything the screens need about the signed-in person's Licence, read once a session.
        // Behind the licence gate like every other read, deliberately: a denied person meets the
        // gate's problem code here rather than a cheerful answer describing the Licence they do
        // not have, and that problem code is what the client draws the card from.
        app.MapGet(
                "/api/licence",
                async (
                    IQueryHandler<GetLicenceQuery, LicenceDto> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.HandleAsync(new GetLicenceQuery(), cancellationToken);

                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
                })
            .RequireAuthorization(AuthorizationPolicies.Api)
            .WithName("GetLicence");

        return app;
    }
}
