using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Onboarding;

namespace TodoWerk.Infrastructure.Onboarding;

/// <summary>
/// Writes the Tenant Member record, hung on the moment Entra ID's ticket has been received and
/// validated.
/// <para>
/// Half of the enforcement of "only when a human signs in", and since M4 only half. There is no
/// flag on the write and no parameter saying whether the caller is interactive: the code path that
/// reaches this one is the OpenID Connect callback, which exists only because a person went through
/// a browser. A scan or a delta sync running on a timer has no route here at all, which is what
/// makes "active in the last thirty days" mean somebody was present rather than that their queue
/// ticked.
/// </para>
/// <para>
/// The other half is <c>TeamsSsoSignIn</c>, which writes the same record through the same port when
/// somebody opens the Teams tab — also a person, also interactive, also with no route from a timer.
/// Two paths now, both of them a human, and the invariant is still enforced by where the writes
/// live rather than by a flag on them.
/// </para>
/// </summary>
internal static class TenantMemberSignInRecorder
{
    /// <summary>
    /// Chains the record onto whatever <c>OnTokenValidated</c> already does, rather than replacing
    /// it: Microsoft.Identity.Web puts its own work on that event, and an assignment here would
    /// silently drop it.
    /// </summary>
    internal static void ChainOnto(OpenIdConnectOptions options)
    {
        var inner = options.Events.OnTokenValidated;

        options.Events.OnTokenValidated = async context =>
        {
            await inner(context);
            await RecordAsync(context);
        };
    }

    private static async Task RecordAsync(TokenValidatedContext context)
    {
        var services = context.HttpContext.RequestServices;
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(TenantMemberSignInRecorder));

        // The same two claims everything else keys a person on, read through the same helpers
        // ICurrentUser uses — so the person the statistics count and the person a scan is queued
        // for cannot drift apart. The tenant is the directory tenant from `tid`; nothing here looks
        // at a UPN, because one tenant holds several verified domains and a guest's UPN names
        // somebody else's organisation entirely.
        var tenantId = context.Principal?.GetTenantId();
        var objectId = context.Principal?.GetObjectId();

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(objectId))
        {
            logger.LogWarning(
                "A sign-in completed without a tenant or object id claim, so no membership record "
                + "was written. The statistics will be short by one person.");

            return;
        }

        try
        {
            await services.GetRequiredService<ITenantMemberStore>()
                .RecordSignInAsync(new IndexUser(tenantId, objectId), context.HttpContext.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Logged, not rethrown. Throwing here fails the sign-in, which would mean refusing
            // somebody access to the product because a statistics row could not be written — the
            // same trade DatabaseReadinessCheck already makes in the other direction, where a
            // database that is down is reported rather than allowed to stop the app from serving.
            //
            // What it costs is one uncounted sign-in and a retention clock that did not move. The
            // second sounds worse than it is: the sweep reads the same table through the same
            // context, so a failure persistent enough to matter is one that stops the sweep too.
            logger.LogError(
                exception,
                "Recording the membership record for a completed sign-in failed. The person is "
                + "signed in; the tenant's statistics and their retention clock are behind by this visit.");
        }
    }
}
