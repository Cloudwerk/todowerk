using Microsoft.Identity.Web.TokenCacheProviders;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Erasure;

namespace TodoWerk.Infrastructure.Authentication;

/// <summary>
/// Evicts one person's entry from the durable token cache, so no background work can act as them
/// after they have been forgotten.
/// <para>
/// Registered first among the purges, deliberately: it is the one that stops anything new starting.
/// A scan or a Change already in flight is stopped by the disappearance of the row it claimed, but
/// nothing else prevents the next one from beginning, and a queued job that could still get a token
/// is a job that could still read somebody's tasks back into an index they asked to have destroyed.
/// </para>
/// <para>
/// The account identifier is rebuilt as <c>oid.tid</c>, which equals the <c>uid.utid</c> MSAL keys
/// the entry on for a member of their own tenant — everybody TodoWerk supports today. For a guest
/// the two differ (home tenant versus signed-into tenant), so a guest's entry would survive this
/// eviction: unusable, because <c>GraphGateway</c> reconstructs the same directory-claims principal
/// and misses it too, but present until the 90-day sliding expiry. The same caveat the gateway
/// carries, in the same direction — guests are unsupported until tried against a real tenant — and
/// erasure cannot do better without storing home identifiers, which ADR-0009's data minimisation
/// forbids. Recorded in status.md.
/// </para>
/// </summary>
internal sealed class TokenCachePersonalDataPurge(IMsalTokenCacheProvider tokenCache) : IPersonalDataPurge
{
    public string Describes => "the Graph token cache";

    public async Task<PurgeOutcome> PurgeAsync(IndexUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        await tokenCache.ClearAsync($"{user.UserId}.{user.TenantId}");

        // One entry asked for, which is all there is to ask about: the distributed cache does not
        // report whether a row was there, and an entry that had already lapsed is the same outcome.
        // Nothing to converge on either — a single eviction has no second pass.
        return PurgeOutcome.Complete(1);
    }
}
