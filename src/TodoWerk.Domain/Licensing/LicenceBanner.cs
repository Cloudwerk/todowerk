namespace TodoWerk.Domain.Licensing;

/// <summary>
/// What the Workbench says to one person about their own Licence. Two things worth saying and a
/// silence, and the silence is the common case: under a Tenant Licence or a Personal Licence in
/// term there is nothing to tell anybody, an unreachable portal is a log line rather than a
/// user's problem until the window runs out, and a Self-Host never has a banner at all.
/// </summary>
public enum LicenceBannerState
{
    /// <summary>Nothing to say.</summary>
    None = 0,

    /// <summary>A Trial is running. Dismissible, and the dismissal never leaves the browser.</summary>
    TrialRunning = 1,

    /// <summary>
    /// A Trial ends within the configured window. The same sentence in a warning colour, and it
    /// ignores an earlier dismissal so that it comes back exactly once.
    /// </summary>
    TrialEndingSoon = 2,
}

/// <summary>Which of the three states one Licence is in at one moment.</summary>
public static class LicenceBanner
{
    /// <summary>
    /// Reads the banner state off a Licence. Deliberately not a day count: the banner names the
    /// date and never counts down, so this answers which sentence to show and nothing about how
    /// long is left.
    /// </summary>
    /// <param name="licence">The resolved Licence, or null when there is none to talk about.</param>
    /// <param name="now">The current instant, from the injected clock.</param>
    /// <param name="endingSoon">
    /// How close to its end a Trial has to be for the warning. Configuration, so that the sentence
    /// on screen and the threshold behind it cannot drift apart.
    /// </param>
    public static LicenceBannerState For(Licence? licence, DateTimeOffset now, TimeSpan endingSoon)
    {
        if (licence is not { Kind: LicenceKind.Trial })
        {
            return LicenceBannerState.None;
        }

        // No end date is not "ends now". A Trial the portal named no end for is a Trial that is
        // running, and warning about an ending nobody stated would be a warning about nothing.
        return licence.EndsAt is { } endsAt && endsAt - now <= endingSoon
            ? LicenceBannerState.TrialEndingSoon
            : LicenceBannerState.TrialRunning;
    }
}
