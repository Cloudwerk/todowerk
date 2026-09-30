using TodoWerk.Application.Indexing;

namespace TodoWerk.Infrastructure.Persistence;

/// <summary>
/// Column shapes and collations every table holds to, so the configurations across the modules
/// agree. Beside the <see cref="TodoWerkDbContext"/> rather than inside one module's folder: the
/// index and the Change queue store the same kinds of value — a tenant id, a Graph task id, a
/// title, a failure sentence — and two modules sizing them differently is how one table quietly
/// truncates what the other stored whole.
/// </summary>
internal static class StorageConventions
{
    /// <summary>
    /// A tenant id and an Entra ID object id are both guids in string form. Sized for the string
    /// rather than stored as <c>uniqueidentifier</c>, because they arrive as claims and a guid
    /// column would make every write parse a value it never needs to interpret.
    /// </summary>
    internal const int IdentifierLength = 64;

    /// <summary>
    /// Graph ids are opaque strings — long, base64-ish, and not documented as bounded. The bound
    /// itself lives in the Application layer, because the request that names a list is validated
    /// there against the same number this column is sized by.
    /// </summary>
    internal const int GraphIdLength = IndexingLimits.GraphIdLength;

    /// <summary>
    /// Text Graph wrote and TodoWerk stores: a task title or a list's display name. Roomy on
    /// purpose — Microsoft To Do keeps 255 characters of a title
    /// (<see cref="Application.Changes.TodoTaskLimits.TitleLength"/>), so what arrives fits with
    /// room to spare, and a title longer than this column can only be something TodoWerk did not
    /// write and does not understand.
    /// <para>
    /// It is not the bound a Change is held to. Graph does not reject an over-long title — it
    /// answers success and stores a truncated one — so the Change runner enforces
    /// To Do's own limit before writing, and this column is only the last word on what can be
    /// recorded at all.
    /// </para>
    /// </summary>
    internal const int DisplayTextLength = 512;

    /// <summary>What the UI shows when something fails. A stack trace never reaches these columns.</summary>
    internal const int FailureReasonLength = 512;

    /// <summary>An enum stored as its name, which is what every state column here does.</summary>
    internal const int EnumNameLength = 32;

    /// <summary>
    /// Binary collation on every column holding an id Graph issued. Graph writes task
    /// and list ids as case-sensitive base64, and Exchange item ids share long prefixes and differ
    /// at the tail — so two legitimately different tasks routinely differ by one letter's case.
    /// Under the database's default <c>SQL_Latin1_General_CP1_CI_AS</c> the unique index over them
    /// cannot tell them apart and rejects the second as a duplicate key, which wedges the list:
    /// every retry reads the same page and fails identically.
    /// <para>
    /// It also settles what "same id" means. The scan looks its pre-loaded rows up with
    /// <see cref="StringComparer.Ordinal"/>; a case-insensitive column would return a row the
    /// dictionary then misses, and the code would conclude the task is new — the exact split that
    /// turned a wrong comparison into a failed insert.
    /// </para>
    /// </summary>
    internal const string GraphIdCollation = "Latin1_General_100_BIN2";

    /// <summary>
    /// Binary collation on the folded Hashtag key and on the Spelling, per ADR-0005: the fold is
    /// computed in C# and the database compares the bytes it produced. Without this, SQL's default
    /// collation would call <c>#Straße</c> and <c>#Strasse</c> one Hashtag while the application
    /// calls them two, and the disagreement would surface as a duplicate-key violation on German
    /// data. The Spelling carries it for the same reason one level down: a plan asks the database
    /// which tasks are <em>not</em> already spelled the target way, and under a case-insensitive
    /// collation that question would answer "none of them" for every casing clean-up.
    /// </summary>
    internal const string KeyCollation = "Latin1_General_100_BIN2";
}
