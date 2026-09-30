using TodoWerk.Domain.Hashtags;
using TodoWerk.SharedKernel;

namespace TodoWerk.Domain.Indexing;

/// <summary>
/// One Microsoft To Do task as TodoWerk last saw it. The title is kept because a rename in M2
/// rewrites it, and because a delta page that reports a task as changed has to be compared
/// against something.
/// <para>
/// Tenant and user are on the row rather than implied by the connection: M3 makes the index
/// tenant-wide, and a migration that adds a discriminator to a populated table has no correct
/// value to backfill (CONTRIBUTING § Changing the schema).
/// </para>
/// </summary>
public sealed class IndexedTask : Entity<Guid>
{
    private IndexedTask(
        Guid id,
        string tenantId,
        string userId,
        string taskListId,
        string graphTaskId,
        string title,
        DateTimeOffset lastModifiedAt)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        TaskListId = taskListId;
        GraphTaskId = graphTaskId;
        Title = title;
        LeadingEmoji = MarkerBlock.LeadingEmojiOf(title);
        LastModifiedAt = lastModifiedAt;
    }

    public string TenantId { get; private set; }

    public string UserId { get; private set; }

    /// <summary>Graph's id for the list this task lives in.</summary>
    public string TaskListId { get; private set; }

    /// <summary>Graph's id for the task. Unique per user, and how a delta page is matched to a row.</summary>
    public string GraphTaskId { get; private set; }

    public string Title { get; private set; }

    /// <summary>
    /// The run of emoji this title opens with, as it is written there, or empty. Derived
    /// from <see cref="Title"/> and never set on its own: it is a column that exists to be queried,
    /// not a second fact about the task.
    /// <para>
    /// Stored because deciding whether a task carries somebody's Marker is the block grammar, which
    /// the database cannot run — so answering it used to mean reading the title of every marked
    /// task on every load of the rules. The run is the half of that question that does not depend
    /// on whose rules are being asked about; the other half is a prefix of this, taken against a
    /// person's own Markers when they ask.
    /// </para>
    /// <para>
    /// Which is also why the <em>block</em> is not what is stored: a block is defined against a
    /// rules table, and one rule taking a new emoji would leave every stored block wrong with
    /// nothing to notice it. This changes only when the title does.
    /// </para>
    /// </summary>
    public string LeadingEmoji { get; private set; }

    /// <summary>
    /// Graph's <c>lastModifiedDateTime</c>. The inventory's "last used" is the newest of these
    /// across a Hashtag's Occurrences, so it has to be the task's own timestamp rather than the
    /// moment TodoWerk happened to scan it.
    /// </summary>
    public DateTimeOffset LastModifiedAt { get; private set; }

    public static IndexedTask Create(
        string tenantId,
        string userId,
        string taskListId,
        string graphTaskId,
        string title,
        DateTimeOffset lastModifiedAt) =>
        // Version 7 rather than 4: the value is the clustered key, and a random one would scatter
        // inserts across the whole B-tree on every scan.
        new(Guid.CreateVersion7(), tenantId, userId, taskListId, graphTaskId, title, lastModifiedAt);

    /// <summary>
    /// Applies what a delta page reported. A task can move between lists — the To Do clients
    /// offer it — so the list id is part of what an update carries.
    /// </summary>
    public void Update(string taskListId, string title, DateTimeOffset lastModifiedAt)
    {
        TaskListId = taskListId;
        Title = title;
        LeadingEmoji = MarkerBlock.LeadingEmojiOf(title);
        LastModifiedAt = lastModifiedAt;
    }
}
