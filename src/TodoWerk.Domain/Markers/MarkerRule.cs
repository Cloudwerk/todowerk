using TodoWerk.Domain.Hashtags;
using TodoWerk.SharedKernel;

namespace TodoWerk.Domain.Markers;

/// <summary>
/// One person's standing instruction that a Hashtag carries a Marker: <c>#bread</c> carries 🍞
/// (ADR-0014). The first stored user choice in the product.
/// <para>
/// A rule declares; it never writes. Titles change only when the user applies their rules as a
/// Change, previewed and undoable like any other — which is why nothing on this class touches a
/// task, and why deleting one writes nothing.
/// </para>
/// <para>
/// Not beside the Occurrences in the index, although a comment there has anticipated a
/// one-row-per-Hashtag table since M1: the index is rebuilt from Graph at will, and a rule is not
/// derivable from Graph.
/// </para>
/// </summary>
public sealed class MarkerRule : Entity<Guid>
{
    private MarkerRule(
        Guid id,
        string tenantId,
        string userId,
        string key,
        string spelling,
        Marker marker,
        int position,
        DateTimeOffset createdAt)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        Key = key;
        Spelling = spelling;
        Marker = marker;
        Position = position;
        CreatedAt = createdAt;
    }

    public string TenantId { get; private set; }

    public string UserId { get; private set; }

    /// <summary>
    /// The folded Hashtag key this rule is about, per <see cref="HashtagKey"/>. Matched by identity
    /// rather than by Spelling, so <c>#Work</c> and <c>#work</c> are one rule — the same claim the
    /// inventory makes about one row.
    /// </summary>
    public string Key { get; private set; }

    /// <summary>
    /// The Spelling the rule was written against. Kept so the rules view can name the Hashtag when
    /// it has no Occurrences left to draw a Canonical Spelling from — the rule outlives the tag.
    /// </summary>
    public string Spelling { get; private set; }

    /// <summary>Unique across this person's rules, so a block maps one Marker to one Hashtag.</summary>
    public Marker Marker { get; private set; }

    /// <summary>
    /// The Marker this rule used to carry, waiting for the next Apply to swap it out of the blocks
    /// it is sitting in. Settled by what that Apply, or its undo, leaves in the titles.
    /// <para>
    /// Not part of the Marker uniqueness: a Marker one rule has retired is free for another rule to
    /// take, and refusing it would let a mistake block an emoji until an Apply nobody wanted to run.
    /// </para>
    /// </summary>
    public Marker? RetiredMarker { get; private set; }

    /// <summary>
    /// Where this rule sits in the person's ordered list, which is the order of the block. Sparse
    /// values are fine: nothing reads the number, only the order it produces.
    /// </summary>
    public int Position { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// When the person deleted this rule, or null while it stands. A deleted rule is kept rather
    /// than removed, because its Marker is still at the front of every title it was applied to:
    /// deleting writes nothing (ADR-0014), so those titles do not change, and a block reader that
    /// no longer recognised the Marker would read the block as ending before it — and the next
    /// Apply would put a second block in front of the first. The row stays so the Marker stays
    /// known; nothing else about it is read again until a Remove Markers cleans up after it.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>A rule the person has deleted. Listed nowhere, applied nowhere, recognised everywhere.</summary>
    public bool IsDeleted => DeletedAt is not null;

    public static MarkerRule Create(
        string tenantId,
        string userId,
        string key,
        string spelling,
        Marker marker,
        int position,
        DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), tenantId, userId, key, spelling, marker, position, createdAt);

    /// <summary>
    /// Takes a new Marker, remembering the one blocks are currently carrying so the next Apply can
    /// swap it.
    /// <para>
    /// The remembered Marker is the one that was last <em>written</em>, not simply the previous
    /// value: changing 🍞 to 🥐 and then to ☕ before any Apply leaves 🍞 in every block, so 🍞 is
    /// what has to be swapped and 🥐 — which no title ever carried — is forgotten. And changing
    /// back to what is already out there cancels the swap altogether.
    /// </para>
    /// </summary>
    public void ChangeMarker(Marker marker)
    {
        if (marker == Marker)
        {
            return;
        }

        RetiredMarker ??= Marker;
        Marker = marker;

        if (RetiredMarker == Marker)
        {
            RetiredMarker = null;
        }
    }

    /// <summary>
    /// Records what an Apply — or the undo of one — has just left in the blocks of every task this
    /// rule covers. The retired Marker means "what the titles carry that is not this rule's
    /// Marker", so it is answered from the titles' side: nothing left to retire when they now carry
    /// this rule's Marker, and the Marker they do carry otherwise.
    /// <para>
    /// The otherwise is not theoretical. A rule edited while its Apply was queued has a Marker the
    /// run never wrote, and an undo puts back a Marker the rule had already forgotten; in both
    /// cases clearing would leave an emoji in every title that no rule remembers and no later Apply
    /// can reach.
    /// </para>
    /// </summary>
    public void Wrote(Marker written) => RetiredMarker = written == Marker ? null : written;

    public void MoveTo(int position) => Position = position;

    /// <summary>Retires the rule without touching a title (ADR-0014). Idempotent.</summary>
    public void Delete(DateTimeOffset at) => DeletedAt ??= at;

    /// <summary>
    /// Follows a Rename to the Hashtag's new name, keeping the Marker and the position (ADR-0014).
    /// The Marker does not change, so nothing is retired and no title needs rewriting.
    /// </summary>
    public void CarryTo(string key, string spelling)
    {
        Key = key;
        Spelling = spelling;
    }
}
