using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Licensing;

namespace TodoWerk.Infrastructure.Licensing;

/// <summary>
/// Reports the seat, hung on the moment Entra ID's ticket has been received and validated — the
/// same event, and for the same reason, as the Tenant Member record beside it.
/// <para>
/// The rule "only when a human signs in" is enforced by where this lives rather than by a flag on
/// the call: the code path that reaches here is the OpenID Connect callback, which exists only
/// because a person went through a browser. A scan on a timer has no route to it. The other half
/// is <c>TeamsSsoSignIn</c>, which reports through the same port when somebody opens the tab.
/// </para>
/// </summary>
internal static class SeatUsageSignInRecorder
{
    /// <summary>
    /// Chains onto whatever <c>OnTokenValidated</c> already does rather than replacing it:
    /// Microsoft.Identity.Web puts its own work on that event, and an assignment would drop it.
    /// </summary>
    internal static void ChainOnto(OpenIdConnectOptions options)
    {
        var inner = options.Events.OnTokenValidated;

        options.Events.OnTokenValidated = async context =>
        {
            await inner(context);
            await ReportAsync(context);
        };
    }

    private static async Task ReportAsync(TokenValidatedContext context)
    {
        var services = context.HttpContext.RequestServices;

        var tenantId = context.Principal?.GetTenantId();
        var objectId = context.Principal?.GetObjectId();

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(objectId))
        {
            // Said once, quietly. The membership recorder beside this one warns about the same
            // missing claims and says what it costs; a second warning about the same sign-in would
            // only make the log harder to read.
            return;
        }

        try
        {
            await services.GetRequiredService<ISeatUsageReporter>()
                .ReportSignInAsync(new IndexUser(tenantId, objectId), context.HttpContext.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The report is already fire-and-forget inside the reporter, so nothing ordinary
            // reaches here — a missing registration would. Logged rather than rethrown for the
            // reason the whole feature exists to avoid: nobody is refused access to the product
            // over a figure that gates nothing.
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(SeatUsageSignInRecorder))
                .LogError(exception, "Could not start the seat report for a completed sign-in.");
        }
    }
}
