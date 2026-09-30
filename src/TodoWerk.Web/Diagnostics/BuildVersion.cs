using System.Reflection;

namespace TodoWerk.Web.Diagnostics;

/// <summary>
/// Which build is answering. CI tags every image it publishes with the commit it was built from
/// (<c>sha-&lt;commit&gt;</c>, see <c>.github/workflows/pr-validation.yml</c>), so the commit is the
/// one fact that identifies a running container — and without it "redeploy" and "the same build as
/// yesterday" are indistinguishable from outside.
/// <para>
/// Read from the assembly rather than from configuration: a version somebody has to remember to set
/// is a version that is eventually wrong. The compiler already stamps this one — .NET appends the
/// source revision to <see cref="AssemblyInformationalVersionAttribute"/>, giving the
/// <c>1.0.0+&lt;commit&gt;</c> form — so nothing in the build has to know this type exists.
/// </para>
/// </summary>
internal sealed record BuildVersion(string Version, string? Commit)
{
    /// <summary>
    /// What a build reports when the compiler stamped no version at all. Not an exception: a
    /// deployment that cannot say which build it is should still answer the question, because the
    /// answer "this build does not know" is itself the thing an operator needs to be told.
    /// </summary>
    public const string Unknown = "unknown";

    public static BuildVersion OfRunningApplication() => Parse(
        typeof(BuildVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion);

    /// <summary>
    /// Splits the informational version at the <c>+</c> that separates the semantic version from the
    /// source revision. A build made outside a git working tree has no revision to append, so the
    /// commit is genuinely absent rather than empty — the difference matters to whoever is trying to
    /// work out whether the deployment is traceable at all.
    /// </summary>
    internal static BuildVersion Parse(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return new BuildVersion(Unknown, null);
        }

        var separator = informationalVersion.IndexOf('+', StringComparison.Ordinal);

        if (separator < 0)
        {
            return new BuildVersion(informationalVersion, null);
        }

        var commit = informationalVersion[(separator + 1)..];

        return new BuildVersion(
            informationalVersion[..separator],
            commit.Length == 0 ? null : commit);
    }
}
