using TodoWerk.Domain.Hashtags;

namespace TodoWerk.Domain.Changes;

/// <summary>
/// What makes a target Spelling acceptable: the round trip <see cref="HashtagName"/> defines.
/// <para>
/// The rule itself moved beside the extractor when a second module came to need it — a Marker Rule
/// is about a Hashtag and has to know that its name is one (ADR-0014). This name stays, because
/// "the target of a Change" is what the Changes module calls the thing it validates, and a caller
/// reading <c>ChangeTarget.RoundTrips</c> is asking a question about a Change.
/// </para>
/// </summary>
public static class ChangeTarget
{
    /// <summary>Whether <paramref name="target"/> survives being written and read back.</summary>
    public static bool RoundTrips(string? target) => HashtagName.RoundTrips(target);
}
