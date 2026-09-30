namespace TodoWerk.Domain.Hashtags;

/// <summary>
/// One Hashtag as it was written in one task title: the literal Spelling, and the folded Key that
/// says which Hashtag it belongs to. The pair travels together because the inventory has to show
/// <c>#Work</c> and <c>#work</c> as one row carrying a casing flag, which it cannot do if the
/// spelling is discarded on the way in.
/// </summary>
/// <param name="Key">The folded identity, per <see cref="HashtagKey"/>.</param>
/// <param name="Spelling">The name exactly as the author typed it, without the leading marker.</param>
public sealed record ExtractedHashtag(string Key, string Spelling);
