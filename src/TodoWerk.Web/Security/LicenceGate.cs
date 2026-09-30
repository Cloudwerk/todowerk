using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Licensing;
using TodoWerk.Domain.Licensing;
using TodoWerk.Web.Endpoints;
using TodoWerk.Web.Legal;

namespace TodoWerk.Web.Security;

/// <summary>
/// Marks an endpoint that answers a signed-in person whether or not they are licensed. Metadata
/// rather than a list of paths in the middleware: a path list is a second place to remember, and
/// what it would get wrong is which endpoints keep working for somebody who has been shut out.
/// </summary>
internal sealed class AllowUnlicensedMetadata;

/// <summary>
/// Denies a person the whole authenticated API when their Licence has ended or could not be
/// verified — and denies nobody else, because the resolution is per person (ADR-0012).
/// <para>
/// A middleware rather than an authorization policy or a filter on each endpoint, and that is the
/// point: it applies to every endpoint that requires authorization, including one added tomorrow
/// by somebody who has never read this file. Opting out is the deliberate act, and it is spelled
/// <see cref="LicenceGateEndpointExtensions.AllowUnlicensed"/> at the endpoint that means it.
/// </para>
/// <para>
/// What stays open for a denied person, and why: <c>/api/me</c>, because the denied card is drawn
/// inside the application shell and the shell reads it — and because the way out sits in that
/// shell; <c>/auth/sign-out</c>, because a person the product has shut out must still be able to
/// leave, and gating it answers the sign-out form with this very problem document instead of a
/// redirect; <c>/api/me/erasure</c>, because erasure is an obligation and not a feature; and the
/// Teams token exchange, because being unable to sign in and being unlicensed are different things
/// and only one of them has a card. <c>/version</c>, the health check and the legal pages are
/// anonymous and never reach here at all.
/// </para>
/// </summary>
internal sealed class LicenceGateMiddleware(RequestDelegate next)
{
    /// <summary>
    /// The name the ended card reads the operator's contact from. Carried on the problem rather
    /// than fetched from an endpoint of its own: the only screen that wants it is the one nothing
    /// else on that screen can be loaded for.
    /// </summary>
    internal const string ContactExtension = "contact";

    /// <summary>
    /// The name the ended card reads the purchase link from. It travels on the refusal rather than
    /// on <c>/api/licence</c> for the reason the contact does: a denied person is refused that
    /// endpoint too, so the one screen they can see has to be told everything on the way in.
    /// <para>
    /// The portal delivers it on a refusal precisely so this card can carry it — somebody who has
    /// just been told their Trial ended is who wants to buy.
    /// </para>
    /// </summary>
    internal const string PurchaseUrlExtension = "purchaseUrl";

    public async Task InvokeAsync(
        HttpContext context,
        ILicenceResolver resolver,
        IOptions<LegalOptions> legal)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(legal);

        var endpoint = context.GetEndpoint();

        // Nothing to gate: an anonymous endpoint, the SPA's fallback document, or a request that
        // matched no route at all. Authorization has already turned away anybody who should not be
        // here — this middleware only decides what a person who is signed in may reach.
        if (endpoint?.Metadata.GetMetadata<IAuthorizeData>() is null
            || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
            || endpoint.Metadata.GetMetadata<AllowUnlicensedMetadata>() is not null
            || context.User.Identity?.IsAuthenticated is not true)
        {
            await next(context);

            return;
        }

        var user = context.User.GetTenantId() is { Length: > 0 } tenantId
            && context.User.GetObjectId() is { Length: > 0 } objectId
                ? new IndexUser(tenantId, objectId)
                : null;

        if (user is null)
        {
            // A principal with no tenant or object id. Not this middleware's refusal to make: the
            // handler behind it answers with its own "sign in again", which is the right advice
            // for a token that is missing claims and the wrong advice for a lapsed Licence.
            await next(context);

            return;
        }

        var resolution = await resolver.ResolveAsync(user, context.RequestAborted);

        if (resolution.IsLicensed)
        {
            await next(context);

            return;
        }

        var ended = resolution.Outcome is LicenceOutcome.Ended;
        var contact = legal.Value.OperatorContact;

        await LicensingErrors
            .ForDeniedResolution(ended, resolution.Message)
            .ToProblem(ExtensionsFor(ended, contact, resolution.PurchaseUrl))
            .ExecuteAsync(context);
    }

    /// <summary>
    /// What the ended card is told beyond its sentence: where to complain, and where to buy.
    /// <para>
    /// Both are on the ended refusal alone. The other card is met by people who have <em>paid</em>,
    /// during an outage on CloudWerk's side — offering them a way to buy what they already
    /// own is the mistake the two codes exist to keep apart — and in any case an unreachable portal
    /// answered nothing, so there is no address to offer. Each is omitted rather than sent empty:
    /// an unconfigured contact is not a fallback phrase pretending to be an address, and a portal
    /// that sells nothing for TodoWerk is a legitimate state that renders no link.
    /// </para>
    /// </summary>
    private static Dictionary<string, object?>? ExtensionsFor(
        bool ended,
        string? contact,
        Uri? purchaseUrl)
    {
        if (!ended)
        {
            return null;
        }

        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(contact))
        {
            extensions[ContactExtension] = contact;
        }

        if (purchaseUrl is not null)
        {
            // OriginalString, for two reasons that point the same way. ToString() *unescapes* —
            // "/Sol%20utions" comes back as "/Sol utions", which is not an address a browser can
            // follow — and OriginalString is exactly what System.Text.Json writes for a Uri, which
            // is how the same address reaches the banner through `/api/licence`. Anything else and
            // the two surfaces would spell one portal address two ways.
            extensions[PurchaseUrlExtension] = purchaseUrl.OriginalString;
        }

        return extensions.Count == 0 ? null : extensions;
    }
}

internal static class LicenceGateEndpointExtensions
{
    /// <summary>
    /// Puts <see cref="LicenceGateMiddleware"/> in front of every endpoint that requires
    /// authorization. After the rate limiter, so a caller cannot spend the process's licence
    /// resolutions faster than they can spend anything else.
    /// </summary>
    public static IApplicationBuilder UseLicenceGate(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<LicenceGateMiddleware>();
    }

    /// <summary>
    /// Keeps this endpoint working for somebody whose Licence has ended or could not be verified.
    /// Every use is a decision worth a sentence at the call site.
    /// </summary>
    public static TBuilder AllowUnlicensed<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpoint => endpoint.Metadata.Add(new AllowUnlicensedMetadata()));

        return builder;
    }
}
