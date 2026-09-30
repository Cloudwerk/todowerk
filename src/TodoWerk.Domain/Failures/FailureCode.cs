namespace TodoWerk.Domain.Failures;

/// <summary>
/// Why something TodoWerk does against Microsoft To Do stopped, as a value the client can switch
/// on. Stored next to the sentence a reader sees, never instead of it: the sentence is written
/// where the cause is known (CONTRIBUTING § What a failure is allowed to say), and this is what
/// decides what the UI <em>offers</em> about it.
/// <para>
/// It exists so that the Workbench never decides whether to show a sign-in button by looking for
/// a phrase in that sentence. ADR-0007 makes the reconnect path load-bearing for an ordinary
/// upgrade rather than only for a 90-day expiry, so that guess is not available.
/// </para>
/// <para>
/// Outside any vertical module: the scan queue, the per-list scan state and the Change queue all
/// store one, and a second enum per module would be three vocabularies for one screen.
/// </para>
/// </summary>
public enum FailureCode
{
    /// <summary>Nothing failed. The default, so an untouched row reads as healthy.</summary>
    None = 0,

    /// <summary>
    /// Only a fresh sign-in clears it: the refresh token is gone, consent was withdrawn, or the
    /// cached grant is narrower than what TodoWerk now asks for (ADR-0007). Offer the button.
    /// </summary>
    ReconnectRequired = 1,

    /// <summary>Graph asked for more patience than the caller was willing to spend. Offer nothing; it retries.</summary>
    Throttled = 2,

    /// <summary>Graph did not answer, or answered with something that is not the caller's to fix. Offer a retry.</summary>
    Unavailable = 3,

    /// <summary>
    /// Something else. Named rather than folded into <see cref="Unavailable"/>, because "we do not
    /// know" and "Microsoft To Do is down" send a reader to different places.
    /// </summary>
    Unknown = 4,

    /// <summary>
    /// Graph answered, definitively, that what TodoWerk asked for is not there. Its own member
    /// because both neighbours send a reader somewhere wrong: <see cref="Unavailable"/> says
    /// Microsoft To Do is down and to wait for it, and <see cref="Unknown"/> says nobody worded
    /// this and to go to the logs, which for a clean 404 hold nothing. Offer what
    /// <see cref="Unavailable"/> offers — a retry — because the next pass may find it.
    /// <para>
    /// Never proof that the thing was deleted. The sentence stored beside this says
    /// what Graph actually did; a list Graph still returns from <c>GET /me/todo/lists</c> has not
    /// been deleted, whatever a read of it answers.
    /// </para>
    /// </summary>
    NotFound = 5,
}
