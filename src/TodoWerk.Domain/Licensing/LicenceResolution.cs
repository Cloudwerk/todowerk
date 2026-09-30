namespace TodoWerk.Domain.Licensing;

/// <summary>
/// The answer to "may this person use TodoWerk, and under what". One type for the request path,
/// the background claims and the Licence endpoint, so the three cannot come to different
/// conclusions about the same person.
/// </summary>
/// <param name="Outcome">What asking ended in.</param>
/// <param name="Licence">What was found. Present only when the outcome is licensed.</param>
/// <param name="Message">
/// The portal's own sentence for a refusal, safe to show verbatim. Present only on
/// <see cref="LicenceOutcome.Ended"/>, where an expired Trial and an expired subscription are each
/// worded as what they are — which is more than TodoWerk could work out from a status alone.
/// </param>
/// <param name="PurchaseUrl">
/// Where a person buys TodoWerk, as the portal composed it. On the answer rather than on the
/// <see cref="Licence"/> because that is what it is a fact about: the portal computes it per call
/// from its own address and its own catalog, it is identical for everybody asking about the
/// solution, and it arrives on a refusal — where there is no Licence to hang it on and where it
/// matters most, because somebody who has just been told their Trial ended is exactly who wants to
/// buy.
/// <para>
/// Null on <see cref="LicenceOutcome.Unverified"/> and <see cref="LicenceOutcome.NoAuthority"/>,
/// and for the same reason in both: nobody answered, so there is no address, and TodoWerk composes
/// none of its own.
/// </para>
/// </param>
public sealed record LicenceResolution(
    LicenceOutcome Outcome,
    Licence? Licence = null,
    string? Message = null,
    Uri? PurchaseUrl = null)
{
    /// <summary>
    /// A Self-Host, or anything else with no authority to ask. Licensed, because there is nobody
    /// to refuse and nothing to refuse on behalf of.
    /// </summary>
    public static readonly LicenceResolution NoAuthority = new(LicenceOutcome.NoAuthority);

    /// <summary>
    /// The portal could not be reached and nothing positive is left to serve on. No message of its
    /// own: the portal did not answer, so the words are TodoWerk's, and they live in
    /// <c>LicensingErrors</c> beside the code they travel with.
    /// </summary>
    public static readonly LicenceResolution Unverified = new(LicenceOutcome.Unverified);

    /// <summary>
    /// A person the portal licensed. The purchase URL is asked for rather than defaulted: there is
    /// one caller in the product, and a default is how a field that arrives on every answer comes
    /// to be dropped on one path without anybody noticing.
    /// </summary>
    public static LicenceResolution Allowed(Licence licence, Uri? purchaseUrl) =>
        new(LicenceOutcome.Licensed, licence, PurchaseUrl: purchaseUrl);

    /// <summary>
    /// A confirmed negative, carrying the portal's own sentence and — the point of it arriving on
    /// a refusal at all — somewhere to go about it.
    /// </summary>
    public static LicenceResolution Ended(string? message, Uri? purchaseUrl) =>
        new(LicenceOutcome.Ended, Message: message, PurchaseUrl: purchaseUrl);

    /// <summary>
    /// Whether the product runs for this person. A Self-Host is licensed for the same reason it
    /// shows no banner: there is no authority, so there is nothing to be short of.
    /// </summary>
    public bool IsLicensed => Outcome is LicenceOutcome.Licensed or LicenceOutcome.NoAuthority;
}
