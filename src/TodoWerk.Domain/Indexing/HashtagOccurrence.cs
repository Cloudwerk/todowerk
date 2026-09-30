using TodoWerk.SharedKernel;
using TodoWerk.Domain.Hashtags;

namespace TodoWerk.Domain.Indexing;

/// <summary>
/// One Spelling of one Hashtag in one task — the unit the inventory counts and the unit a rename
/// will rewrite.
/// <para>
/// The Spelling is stored next to the folded Key rather than being derived from it: the inventory
/// shows <c>#Work</c> and <c>#work</c> as one row that says it is written two ways, which needs
/// both the identity and every Spelling behind it. Nothing here is a value object on the task,
/// because the inventory aggregates across tasks and lists, which an owned collection would make
/// awkward to query.
/// </para>
/// </summary>
public sealed class HashtagOccurrence : Entity<Guid>
{
    private HashtagOccurrence(
        Guid id,
        string tenantId,
        string userId,
        Guid indexedTaskId,
        string key,
        string spelling)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        IndexedTaskId = indexedTaskId;
        Key = key;
        Spelling = spelling;
    }

    public string TenantId { get; private set; }

    public string UserId { get; private set; }

    /// <summary>The task this Occurrence was read from. Deleting the task deletes its Occurrences.</summary>
    public Guid IndexedTaskId { get; private set; }

    /// <summary>
    /// The folded identity from <see cref="HashtagKey"/>. Stored under a binary collation so the
    /// database compares the bytes C# produced rather than applying a collation of its own
    /// (ADR-0005).
    /// </summary>
    public string Key { get; private set; }

    /// <summary>The name as written in this task, without the marker.</summary>
    public string Spelling { get; private set; }

    public static HashtagOccurrence Create(
        string tenantId,
        string userId,
        Guid indexedTaskId,
        ExtractedHashtag hashtag)
    {
        ArgumentNullException.ThrowIfNull(hashtag);

        return new HashtagOccurrence(
            Guid.CreateVersion7(),
            tenantId,
            userId,
            indexedTaskId,
            hashtag.Key,
            hashtag.Spelling);
    }
}
