using TodoWerk.Domain.Failures;
using TodoWerk.SharedKernel;

namespace TodoWerk.Infrastructure.Graph;

/// <summary>
/// What can go wrong between TodoWerk and Graph, in the words the UI shows. One place, because
/// the same failure has to read the same way whether it happened in a request, in a scan, or in a
/// Change.
/// </summary>
internal static class GraphErrors
{
    /// <summary>
    /// The refresh token expired, the user revoked consent, or the cached grant is narrower than
    /// what TodoWerk now asks for (ADR-0007); the UI asks them to reconnect rather than reporting
    /// a generic failure.
    /// </summary>
    internal static readonly Error ReconnectRequired = Error.Unauthorized(
        "Graph.ReconnectRequired",
        "The connection to Microsoft To Do has expired. Sign in again to reconnect.");

    internal static readonly Error Unavailable = Error.Failure(
        "Graph.Unavailable",
        "Microsoft To Do could not be reached. Try again in a moment.");

    /// <summary>
    /// Graph kept asking for more time than the caller was willing to wait. Not a defect and not
    /// permanent — the list is failed on its own and the next pass tries again.
    /// </summary>
    internal static readonly Error Throttled = Error.Failure(
        "Graph.Throttled",
        "Microsoft To Do is rate-limiting this account. TodoWerk will pick this up again shortly.");

    /// <summary>
    /// Graph expired the delta token. Recoverable without an operator: the list drops its link
    /// and is read in full instead.
    /// </summary>
    internal static readonly Error ResyncRequired = Error.Conflict(
        "Graph.ResyncRequired",
        "Microsoft To Do asked for a full re-scan of this list.");

    /// <summary>
    /// The list or task is gone. On the write path this is ordinary — a Change plans over the
    /// index, and a task can be deleted between the plan and the write — so it is its own error
    /// rather than one more way of saying "unavailable", which would send a reader looking for an
    /// outage that is not happening.
    /// </summary>
    internal static readonly Error NotFound = Error.NotFound(
        "Graph.NotFound",
        "Microsoft To Do no longer has this item.");

    /// <summary>
    /// Graph answered 404 to a read of a list it had returned from <c>GET /me/todo/lists</c> in
    /// the same pass. The gateway cannot produce this — it sees a status code and
    /// nothing else — so <c>IndexScanRunner</c> substitutes it for <see cref="NotFound"/>, whose
    /// sentence would tell the reader a list was deleted that is still there. A list that really
    /// is gone never reaches a read: reconciliation drops it first, because the listing is the
    /// authority on what the user has.
    /// </summary>
    internal static readonly Error ListNotReadable = Error.NotFound(
        "Graph.ListNotReadable",
        "Microsoft To Do returned this list, then could not find it when TodoWerk asked for its "
        + "tasks. TodoWerk will try again.");

    /// <summary>
    /// The full read asked for by <see cref="ResyncRequired"/> came back asking for a resync as
    /// well, so the one remedy there is — dropping the delta link — has already been spent. Here
    /// rather than worded in the runner, because it is still a sentence about something Graph did,
    /// and a sentence written there carried no code the Workbench could switch on.
    /// </summary>
    internal static readonly Error ResyncRequiredAgain = Error.Conflict(
        "Graph.ResyncRequiredAgain",
        "Microsoft To Do keeps asking for a fresh read of this list. TodoWerk will try again.");

    /// <summary>
    /// The stored code behind a failure, for the client to switch on. Keyed on
    /// <see cref="Error.Code"/> rather than on reference equality, so a failure that travelled
    /// through a <see cref="Result"/> across a layer still classifies.
    /// <para>
    /// Total over the errors declared above, and pinned as total by a test. The fall-through is
    /// for an <see cref="Error"/> from somewhere else entirely; a member of this class reaching it
    /// would store a failure Graph had named precisely as "we do not know".
    /// </para>
    /// </summary>
    internal static FailureCode CodeFor(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error == Error.None)
        {
            return FailureCode.None;
        }

        return error.Code switch
        {
            "Graph.ReconnectRequired" => FailureCode.ReconnectRequired,
            "Graph.Throttled" => FailureCode.Throttled,
            "Graph.Unavailable" => FailureCode.Unavailable,
            "Graph.NotFound" or "Graph.ListNotReadable" => FailureCode.NotFound,

            // Graph refusing to serve a read it has just been asked for twice is not the caller's
            // to fix and may well pass, which is what Unavailable means here. The in-band
            // ResyncRequired never reaches this — the runner intercepts it and re-reads — but a
            // Change or the list read can carry one, and it must not fall through.
            "Graph.ResyncRequired" or "Graph.ResyncRequiredAgain" => FailureCode.Unavailable,

            _ => FailureCode.Unknown,
        };
    }
}
