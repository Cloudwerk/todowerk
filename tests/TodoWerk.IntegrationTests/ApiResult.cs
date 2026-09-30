using System.Net;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// One API answer, whichever way it went: the status, the body if it succeeded, and the problem
/// details' code and detail if it did not.
/// <para>
/// The code is what the tests assert on rather than the sentence. The client switches on the
/// code too; a test suite that kept matching prose would be the same mistake with a longer
/// feedback loop.
/// </para>
/// </summary>
internal sealed record ApiResult<T>(HttpStatusCode StatusCode, T? Body, string? Code, string? Detail);
