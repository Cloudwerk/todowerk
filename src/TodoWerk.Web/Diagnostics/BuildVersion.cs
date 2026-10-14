using System.Globalization;
using System.Reflection;

namespace TodoWerk.Web.Diagnostics;

/// <summary>
/// Which build is answering. CI tags every image it publishes with the commit it was built from
/// (<c>sha-&lt;commit&gt;</c>, see <c>.github/workflows/pr-validation.yml</c>), so the commit is the
/// one fact that identifies a running container — and without it "redeploy" and "the same build as
/// yesterday" are indistinguishable from outside.
/// <para>
/// Read from the assembly rather than from configuration: a version somebody has to remember to set
/// is a version that is eventually wrong. The build already stamps this one: MinVer sets the version
/// from the nearest release tag (Directory.Build.props), and .NET appends the source revision to
/// <see cref="AssemblyInformationalVersionAttribute"/>, giving the <c>1.0.3+&lt;commit&gt;</c> form —
/// so nothing in the build has to know this type exists.
/// </para>
/// <para>
/// The commit's date comes from the same place by a different route: <c>TodoWerk.Web.csproj</c>
/// asks git for it at build time and stamps it as assembly metadata. The commit's date and not the
/// clock's, so two builds of one commit are the same build and a rebuilt image does not look newer
/// than the code it carries.
/// </para>
/// </summary>
internal sealed record BuildVersion(string Version, string? Commit, DateTimeOffset? CommitDate = null)
{
    /// <summary>The assembly metadata key <c>TodoWerk.Web.csproj</c> stamps the commit's date under.</summary>
    internal const string CommitDateKey = "CommitDate";

    /// <summary>
    /// What a build reports when the compiler stamped no version at all. Not an exception: a
    /// deployment that cannot say which build it is should still answer the question, because the
    /// answer "this build does not know" is itself the thing an operator needs to be told.
    /// </summary>
    public const string Unknown = "unknown";

    public static BuildVersion OfRunningApplication()
    {
        var assembly = typeof(BuildVersion).Assembly;
        var stamped = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == CommitDateKey)
            ?.Value;

        return Parse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) with
        {
            CommitDate = ParseCommitDate(stamped),
        };
    }

    /// <summary>
    /// Reads the ISO 8601 date git printed. Anything else is no date rather than a failure: the
    /// build that stamped it is the same build now running, and a page that cannot name the date
    /// should still name the commit.
    /// </summary>
    internal static DateTimeOffset? ParseCommitDate(string? stamped) =>
        DateTimeOffset.TryParse(stamped, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date
            : null;

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
