using Microsoft.Extensions.Options;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.Domain.Licensing;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Licensing.GetLicence;

/// <summary>
/// Reads the resolver's answer and turns it into the one shape the client reads once a session.
/// <para>
/// It triggers no portal call of its own beyond what any authenticated request already does: the
/// resolver answers from its per-person cache, and this request went through the same filter every
/// other one did. That holds however the filter's own call went — an answer it has just fetched is
/// inside its recheck interval, an attempt that has just failed is inside its retry floor, and
/// both count from the instant that attempt *ended* rather than began, so nothing the round trip
/// took can have used them up before this line runs.
/// </para>
/// <para>
/// Asked again rather than handed down from the filter deliberately. What that would save is one
/// dictionary read; what it would cost is a second path between the filter and this endpoint that
/// can disagree with the first — and, if it went through <c>HttpContext</c>, a dependency this
/// layer is not allowed to have.
/// </para>
/// <para>
/// A denied person never arrives here at all — the filter answers first, with the problem code the
/// client renders as a card.
/// </para>
/// </summary>
internal sealed class GetLicenceQueryHandler(
    ICurrentUser currentUser,
    ILicenceResolver resolver,
    TimeProvider timeProvider,
    IOptions<LicensingOptions> options)
    : IQueryHandler<GetLicenceQuery, LicenceDto>
{
    public async Task<Result<LicenceDto>> HandleAsync(
        GetLicenceQuery query,
        CancellationToken cancellationToken)
    {
        var user = IndexUser.From(currentUser);

        if (user is null)
        {
            return Result.Failure<LicenceDto>(LicensingErrors.NotSignedIn);
        }

        var resolution = await resolver.ResolveAsync(user, cancellationToken);

        // Belt and braces behind the filter. Reaching this with a denial means the filter was not
        // applied to this endpoint, and answering with a cheerful "no Licence, no banner" would
        // look exactly like a Self-Host to the client — which is the one wrong answer available.
        if (!resolution.IsLicensed)
        {
            return Result.Failure<LicenceDto>(LicensingErrors.ForDeniedResolution(
                resolution.Outcome is LicenceOutcome.Ended,
                resolution.Message));
        }

        var licence = resolution.Licence;

        return Result.Success(new LicenceDto(
            licence?.Kind,
            licence?.EndsAt,
            LicenceBanner.For(licence, timeProvider.GetUtcNow(), options.Value.TrialEndingSoon),
            // A Self-Host has no Licence and its consent invitation is unchanged, so the flag is
            // true there. Reading it off `licence?.MayOfferTenantConsent ?? true` would say the
            // same thing in fewer characters and hide which of the two cases it was answering.
            licence is null || licence.MayOfferTenantConsent,
            // Off the resolution rather than off the Licence: the portal computes one address for
            // everybody asking about TodoWerk, and it arrives on a refusal too — which is where a
            // Licence does not. A Self-Host reaches here with neither and shows no link.
            resolution.PurchaseUrl));
    }
}
