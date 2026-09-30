using Microsoft.Extensions.Logging;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Erasure;

namespace TodoWerk.Application.Onboarding;

/// <summary>
/// Destroys everything TodoWerk holds about one person, and leaves the tenant's cumulative count
/// standing.
/// <para>
/// One implementation, two callers: the person asking for it themselves, and the sweep that forgets
/// somebody who has stopped coming back. Deliberately not two — "twelve months of dormancy has the
/// same effect as asking" is a claim that only stays true while there is one path.
/// </para>
/// <para>
/// Not a handler, so the registration scan does not find it; registered beside them the way
/// <c>ChangePlanner</c> is.
/// </para>
/// </summary>
public sealed class PersonalDataEraser(
    IEnumerable<IPersonalDataPurge> purges,
    ITenantMemberStore members,
    ILogger<PersonalDataEraser> logger)
{
    /// <summary>
    /// Runs every module's purge, then anonymises the Tenant Member row.
    /// <para>
    /// That order matters in one direction only. Anonymising first would leave a person's index and
    /// journals behind with nothing left pointing at them — no route to reach them, and nothing to
    /// notice they are there. Anonymising last means a crash part-way leaves the row identifiable, so
    /// the same erasure can simply be attempted again, and every purge is idempotent for exactly
    /// that reason.
    /// </para>
    /// <para>
    /// Which is also why a purge that stops while it is still finding rows is refused rather than
    /// waved through: the identifier is the only thing that can find the residue, so removing it on
    /// a half-finished purge would make what is left permanently unreachable while telling the person
    /// they had been forgotten.
    /// </para>
    /// </summary>
    /// <returns>False when something was left behind, in which case nothing was anonymised.</returns>
    public async Task<bool> EraseAsync(IndexUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var unfinished = new List<string>();

        foreach (var purge in purges)
        {
            var outcome = await purge.PurgeAsync(user, cancellationToken);

            if (!outcome.NothingLeft)
            {
                unfinished.Add(purge.Describes);
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Erasure removed {Count} row(s) from {What} for one person in tenant {TenantId}.",
                    outcome.Removed,
                    purge.Describes,
                    user.TenantId);
            }
        }

        if (unfinished.Count > 0)
        {
            // Left identifiable on purpose, so this can be attempted again — by the person, or by
            // the sweep on its next tick, which finds them exactly because the row still names them.
            logger.LogError(
                "Erasure for one person in tenant {TenantId} did not finish: {What} still held rows "
                + "after its last pass. Their membership record is left as it is so the erasure can "
                + "be attempted again.",
                user.TenantId,
                string.Join(", ", unfinished));

            return false;
        }

        var anonymised = await members.AnonymiseAsync(user, cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            // The object id is deliberately absent from every message here. It is the thing being
            // destroyed, and writing it into a log would move it rather than remove it.
            logger.LogInformation(
                "Erasure complete for one person in tenant {TenantId}; their membership record was {Outcome}.",
                user.TenantId,
                anonymised ? "anonymised" : "already gone");
        }

        return true;
    }
}
