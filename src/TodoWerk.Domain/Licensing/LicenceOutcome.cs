namespace TodoWerk.Domain.Licensing;

/// <summary>
/// What asking about one person ended in. Four answers, and the two that deny are deliberately
/// separate: a customer who has paid meets <see cref="Unverified"/> during a portal outage, and
/// telling them their access had ended would be a lie they would reasonably act on.
/// </summary>
public enum LicenceOutcome
{
    /// <summary>
    /// Nobody was asked, because nobody is configured to ask. A Self-Host: no client, no outbound
    /// call, no banner and never a refusal.
    /// </summary>
    NoAuthority = 0,

    /// <summary>The portal answered, and the answer was yes.</summary>
    Licensed = 1,

    /// <summary>
    /// The portal answered, and the answer was no — expired, suspended, or nothing held at all.
    /// A confirmed negative, which is the only thing that produces the ended card.
    /// </summary>
    Ended = 2,

    /// <summary>
    /// The portal could not be reached and there is no positive answer left to serve on: either
    /// none younger than the fail-open window, or none at all, for somebody arriving for the first
    /// time during an outage. Retried on the next request, and it clears itself.
    /// </summary>
    Unverified = 3,
}
