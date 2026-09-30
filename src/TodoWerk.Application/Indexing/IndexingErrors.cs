using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Indexing;

/// <summary>Failures the indexing module reports to the caller, in the words the UI shows.</summary>
public static class IndexingErrors
{
    /// <summary>
    /// The endpoint authorised the request but the principal carries no tenant or object id, so
    /// there is no index to speak of. A misconfigured token rather than an anonymous caller —
    /// which the authorization policy has already turned away.
    /// </summary>
    public static readonly Error NotSignedIn = Error.Unauthorized(
        "Indexing.NotSignedIn",
        "Sign in again to continue.");

    /// <summary>
    /// The request named a list with an identifier no Graph list can have. Rejected as input
    /// rather than sent onward, where it would only fail the insert it does not fit into.
    /// </summary>
    public static readonly Error InvalidTaskListId = Error.Validation(
        "Indexing.InvalidTaskListId",
        "That task list identifier is not valid.");
}
