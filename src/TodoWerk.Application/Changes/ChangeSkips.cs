namespace TodoWerk.Application.Changes;

/// <summary>
/// Why one task was passed over, in the words the UI shows. A skip is not a failure: the user
/// asked for something and there was nothing there to do it to.
/// <para>
/// Here rather than in the runner, because two things now say them. The runner reports what it
/// found when it re-read the task, and an Apply Markers' preview reports the one skip it can work
/// out in advance — and a preview that said "too long" one way and an outcome that said it another
/// would read as two different things happening.
/// </para>
/// </summary>
public static class ChangeSkips
{
    public const string TagGone =
        "The hashtag was no longer in this task when TodoWerk came to change it.";

    public const string AlreadyRight =
        "This task already read the way the change asked for.";

    public const string EditedSince =
        "Somebody edited this task after TodoWerk changed it, so their version was left alone.";

    public const string TaskGone =
        "This task no longer exists in Microsoft To Do.";

    /// <summary>
    /// Microsoft To Do keeps 255 characters and silently drops the rest, answering success either
    /// way. Leaving the task alone is the only outcome that keeps undo honest.
    /// </summary>
    public const string TitleTooLongForToDo =
        "The new title would be longer than Microsoft To Do stores, so this task was left alone.";

    public const string TitleTooLongToRecord =
        "The new title would be longer than TodoWerk can record, so this task was left alone.";

    /// <summary>
    /// A task whose title is nothing but the markers being removed. Taking them would leave it with
    /// no title at all, which is not a task Microsoft To Do has — and a Change is not the place to
    /// invent a name for somebody's task, so it is left as it is and named.
    /// </summary>
    public const string TitleWouldBeEmpty =
        "This task's title is only the markers being removed, so it was left alone rather than "
        + "left with no title.";

    /// <summary>
    /// A Remove found nothing of its own left in the block: applied since, removed since, or the
    /// hashtag put back. The counterpart of <see cref="AlreadyRight"/> for the one Change that
    /// takes something away.
    /// </summary>
    public const string MarkerGone =
        "The markers this change was removing were no longer at the front of this task.";
}
