namespace TodoWerk.Application.Indexing;

/// <summary>
/// What kind of Microsoft To Do list this is. System lists are created by the service rather
/// than by the user, and Graph will not let them be renamed or deleted — so rename and merge
/// have to recognise them rather than offering an action that is certain to fail.
/// </summary>
public enum TaskListKind
{
    /// <summary>An ordinary user-created list. The only kind rename and merge may touch.</summary>
    Normal,

    /// <summary>The built-in list every account has, shown as "Tasks".</summary>
    Default,

    /// <summary>
    /// The projection of Outlook-flagged mail. Tasks appear here because a message was flagged,
    /// not because anyone created them, so it is not a list in the sense the rest of the app means.
    /// </summary>
    FlaggedEmails,

    /// <summary>
    /// A well-known list Graph named after this code was written. Treated as a system list: a new
    /// well-known name is by definition service-owned, and guessing "ordinary" would offer a
    /// rename that Graph then refuses.
    /// </summary>
    Unknown,
}
