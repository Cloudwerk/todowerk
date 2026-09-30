namespace TodoWerk.Application.Indexing;

/// <summary>
/// One Microsoft To Do list. Carries the metadata that decides whether a rename or a merge is
/// permitted at all — not only what to put on screen — because the inventory has to flag a list
/// it cannot act on rather than discovering that when the job runs.
/// </summary>
/// <param name="IsShared">
/// The list is shared with other people. A rename is visible to all of them, so this is the
/// blast radius a dry-run preview has to state.
/// </param>
/// <param name="IsOwner">
/// The signed-in user owns the list. Someone else's shared list may not be writable at all.
/// </param>
public sealed record TaskListDto(
    string Id,
    string DisplayName,
    TaskListKind Kind,
    bool IsShared,
    bool IsOwner)
{
    /// <summary>The built-in "Tasks" list.</summary>
    public bool IsDefault => Kind is TaskListKind.Default;

    /// <summary>Created by the service rather than the user, and therefore not renameable.</summary>
    public bool IsSystemList => Kind is not TaskListKind.Normal;
}
