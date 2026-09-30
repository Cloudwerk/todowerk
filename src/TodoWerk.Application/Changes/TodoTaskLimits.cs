namespace TodoWerk.Application.Changes;

/// <summary>
/// What Microsoft To Do will actually store, as distinct from what TodoWerk can hold.
/// </summary>
public static class TodoTaskLimits
{
    /// <summary>
    /// The longest title Microsoft To Do keeps. A longer title PATCHed through Graph is answered
    /// with success, and the task comes back 255 characters long — the first 252 of what was sent,
    /// followed by three full stops. Graph neither rejects the write nor reports that it
    /// shortened anything.
    /// <para>
    /// So this is a bound TodoWerk enforces rather than one it can let Graph enforce. A rewrite
    /// past it would leave a Hashtag cut in half in somebody's task, and a journal row holding a
    /// title that never existed in the mailbox — which undo compares against, fails to match, and
    /// skips. The write would be silent, wrong, and permanent.
    /// </para>
    /// </summary>
    public const int TitleLength = 255;
}
