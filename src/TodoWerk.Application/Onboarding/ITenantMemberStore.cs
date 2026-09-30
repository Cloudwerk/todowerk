using TodoWerk.Application.Abstractions.Authentication;

namespace TodoWerk.Application.Onboarding;

/// <summary>The Tenant Member table, as everything that writes it sees it.</summary>
public interface ITenantMemberStore
{
    /// <summary>
    /// Records that somebody signed in: a new row with both moments set, or the last moment moved
    /// on one that already exists. Called from the sign-in event and from nowhere else.
    /// </summary>
    Task RecordSignInAsync(IndexUser user, CancellationToken cancellationToken);

    /// <summary>
    /// Forgets who somebody was while keeping the tenant's cumulative count intact. Returns false
    /// when there was no identifiable row to anonymise, which is what makes a retried erasure
    /// harmless rather than an error.
    /// </summary>
    Task<bool> AnonymiseAsync(IndexUser user, CancellationToken cancellationToken);

    /// <summary>
    /// The people who have not signed in for longer than <paramref name="dormancyWindow"/>, oldest
    /// first, at most <paramref name="batchSize"/> of them. Already-anonymised rows are never
    /// returned: they hold no last moment, so there is nobody left in them to forget.
    /// </summary>
    Task<IReadOnlyList<IndexUser>> FindDormantAsync(
        TimeSpan dormancyWindow,
        int batchSize,
        CancellationToken cancellationToken);
}
