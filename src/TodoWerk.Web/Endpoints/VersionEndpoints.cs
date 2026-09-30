using Microsoft.Extensions.Options;
using TodoWerk.Application.Licensing;
using TodoWerk.Web.Diagnostics;

namespace TodoWerk.Web.Endpoints;

internal static class VersionEndpoints
{
    /// <summary>
    /// Which build is running, and in which shape, to anybody who asks. <c>/health</c> already says
    /// whether the deployment is well; this says <em>what</em> is deployed, which nothing else in
    /// the application could answer because an image tag can be moved, and whether its
    /// <c>Licensing</c> section is absent or configured, which a redeploy of the very same image
    /// can change without anybody noticing.
    /// <para>
    /// Anonymous, for the same reason the legal pages are: everyone who needs this arrives without
    /// a session — an operator checking that a redeploy actually took, a smoke test after a
    /// release, somebody reporting a bug against a deployment they do not administer. Nothing is
    /// given away by answering. TodoWerk is AGPL-3.0 and its source is public, so naming the
    /// commit tells a stranger only which public revision they are talking to, which is precisely
    /// what a bug report needs to contain. Every signed-in person can tell the two licensing
    /// shapes apart from the Licence panel; what is named is the shape alone, never the host, the
    /// slug or the key.
    /// </para>
    /// </summary>
    public static IEndpointRouteBuilder MapVersionEndpoints(this IEndpointRouteBuilder app)
    {
        // Read once at startup rather than per request: neither the assembly nor the bound options
        // can change under a running process, and reflection on every call would be a cost paid
        // for nothing. This runs while the pipeline is being configured, before the host starts
        // and before ValidateOnStart's own pass, and reading Value runs the same validators: a
        // half-filled section refuses here, with the message AddTodoWerkLicensing wrote for it,
        // rather than moments later — never on a request.
        var deployment = RunningDeployment.Of(
            BuildVersion.OfRunningApplication(),
            app.ServiceProvider.GetRequiredService<IOptions<LicensingOptions>>().Value);

        app.MapGet("/version", () => TypedResults.Ok(deployment))
            .AllowAnonymous()
            .WithName("Version");

        return app;
    }
}
