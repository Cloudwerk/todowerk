namespace TodoWerk.Application.Onboarding;

/// <summary>
/// What the Tenant Overview will and will not say, and how long TodoWerk keeps somebody who has
/// stopped coming back.
/// <para>
/// In the Application layer rather than beside <c>IndexingOptions</c> in Infrastructure, for the
/// reason <c>ChangeOptions</c> is: these are read on both sides — the query that withholds
/// statistics below the floor is here, and the worker that sweeps dormant people is there.
/// </para>
/// </summary>
public sealed class OnboardingOptions
{
    public const string SectionName = "Onboarding";

    /// <summary>
    /// How many identifiable people must be on record before the statistics are rendered at all.
    /// Below it there is no panel, no placeholder and no explanation: a total across three people is
    /// those three people's data wearing a statistics label, and the overview is visible to every
    /// colleague rather than to an administrator alone. In a four-person tenant a total plus the
    /// reader's own knowledge is an inference about three identifiable people, and this floor is
    /// the only thing standing between an aggregate and that inference.
    /// <para>
    /// Identifiable, not cumulative: an anonymised membership row still counts toward the total the
    /// screen shows, but it holds no Occurrences and no activity, so it is not among the people the
    /// figures describe — and a floor it counted toward would release statistics about fewer
    /// identifiable people than it promises.
    /// </para>
    /// <para>
    /// The invitation to grant Tenant Consent is subject to no floor and renders regardless —
    /// otherwise the feature could not be found in the tenants that most need it.
    /// </para>
    /// </summary>
    public int StatisticsFloor { get; set; } = 5;

    /// <summary>
    /// How long somebody may go without an interactive sign-in before TodoWerk forgets them
    /// entirely, down the same path a person takes when they ask for it themselves.
    /// <para>
    /// It is also the longest activity window the Tenant Overview reports, and deliberately the
    /// same number: the retention rule and the product claim cannot then drift apart without
    /// somebody noticing, because the screen is computed from this value rather than from a
    /// second one that happens to match today.
    /// </para>
    /// <para>
    /// This is what an administrator revoking TodoWerk in Entra ID eventually amounts to. Nothing
    /// tells TodoWerk that the revocation happened, but nobody can sign in afterwards, so
    /// everybody goes dormant and the sweep clears the tenant. There is no separate
    /// organisation-wide action and none is wanted.
    /// </para>
    /// </summary>
    public TimeSpan DormancyWindow { get; set; } = TimeSpan.FromDays(365);

    /// <summary>
    /// How often the worker looks for people to forget. Hourly, not every few seconds: the window
    /// it enforces is a year long, and being an hour late to a twelve-month deadline is not a
    /// promise broken.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How many dormant people one pass of the sweep loads and erases in one scope. The bound on a
    /// pass, not on a tick: a tick drains passes back to back while full ones keep coming, up to a
    /// cap the worker owns, so a backlog clears in hours rather than months. What this bounds is
    /// each pass's blast radius — every erasure is a fan of deletes across every module, and the
    /// batch is what keeps one scope's work, and the gap between cancellation checks, small.
    /// </summary>
    public int SweepBatchSize { get; set; } = 50;
}
