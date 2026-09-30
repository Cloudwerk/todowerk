using TodoWerk.Application.Licensing;

namespace TodoWerk.Web.Diagnostics;

/// <summary>
/// What <c>/version</c> answers: the build the compiler stamped, and the one deployment fact beside
/// it that a redeploy of the same image can change — whether the <c>Licensing</c> section is
/// absent or configured.
/// <para>
/// Composed here rather than added to <see cref="BuildVersion"/>, because that record's reason is
/// what the compiler stamped and this field is read from configuration. It belongs on the same
/// endpoint all the same: <c>/version</c> exists so that a configuration difference between two
/// deployments of the same image is visible from outside. The shape is the whole disclosure: no
/// host, no slug, and never the key.
/// </para>
/// </summary>
internal sealed record RunningDeployment(string Version, string? Commit, string Licensing)
{
    /// <summary>
    /// The section is absent: no portal client exists, nobody is asked, every person is licensed.
    /// What a Self-Host is (ADR-0012). The word is <see cref="LicensingOptions.IsAbsent"/>'s, so a
    /// deployment check can compare this JSON verbatim.
    /// </summary>
    internal const string LicensingAbsent = "absent";

    /// <summary>
    /// The section is complete: every signed-in person is resolved against ManagementPortal, and
    /// whoever the portal says holds nothing meets the ended card.
    /// </summary>
    internal const string LicensingPortal = "portal";

    /// <summary>
    /// A string rather than a boolean because the two shapes are not the only ones this record may
    /// ever have to name, and because <c>configured: false</c> reads as a fault to somebody who
    /// does not know the section is meant to be empty. Lower case, unlike the enums the API sends
    /// by member name, because it is not an enum: a deployment check compares this JSON verbatim,
    /// and nothing else consumes the field.
    /// </summary>
    public static RunningDeployment Of(BuildVersion build, LicensingOptions licensing)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(licensing);

        return new RunningDeployment(
            build.Version,
            build.Commit,
            licensing.IsConfigured ? LicensingPortal : LicensingAbsent);
    }
}
