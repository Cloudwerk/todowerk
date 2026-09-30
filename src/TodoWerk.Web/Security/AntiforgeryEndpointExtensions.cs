using Microsoft.AspNetCore.Antiforgery;

namespace TodoWerk.Web.Security;

internal static class AntiforgeryEndpointExtensions
{
    /// <summary>
    /// Marks an endpoint as requiring an antiforgery token. <c>RequireAntiforgery()</c> only
    /// covers endpoints that bind a form, so mutating JSON/no-body endpoints opt in here. The
    /// marker metadata is what lets a test enumerate every mutating endpoint and prove none
    /// forgot this call — protection that exists only as a convention is protection until the
    /// first new endpoint.
    /// </summary>
    public static TBuilder ValidateAntiforgery<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(AntiforgeryProtectedMetadata.Instance);

        builder.AddEndpointFilter(async (context, next) =>
        {
            var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();

            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.Problem(
                    title: "Invalid antiforgery token",
                    detail: "The request could not be verified. Reload the page and try again.",
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = "Antiforgery.Invalid" });
            }

            return await next(context);
        });

        return builder;
    }

    /// <summary>
    /// Marks a mutating endpoint for which the antiforgery pair is not the defence, with the
    /// reason spelled out. The invariant test enumerates these too, so an exemption is a thing
    /// somebody wrote down rather than a thing somebody forgot — and the reason travels with the
    /// endpoint instead of living in a test's allow-list.
    /// </summary>
    public static TBuilder AntiforgeryNotApplicable<TBuilder>(this TBuilder builder, string reason)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new AntiforgeryNotApplicableMetadata(reason));

        return builder;
    }
}

/// <summary>
/// Present on the one mutating endpoint that accepts no ambient credential, and therefore has
/// nothing for a cross-site request to forge with.
/// </summary>
internal sealed record AntiforgeryNotApplicableMetadata(string Reason);

/// <summary>
/// Present on every endpoint that validates the antiforgery token. Exists so the invariant
/// "every mutating endpoint validates" is something a test can read off the endpoint data
/// source instead of something a reviewer has to remember.
/// </summary>
internal sealed class AntiforgeryProtectedMetadata
{
    public static readonly AntiforgeryProtectedMetadata Instance = new();

    private AntiforgeryProtectedMetadata()
    {
    }
}
