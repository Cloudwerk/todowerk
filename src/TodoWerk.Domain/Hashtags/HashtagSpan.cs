namespace TodoWerk.Domain.Hashtags;

/// <summary>
/// Where one Hashtag sits in a title, marker included. The index throws the position away and
/// keeps the pair; a Change cannot, because ADR-0006 makes the edit "replace this span and touch
/// nothing else" — so the grammar has to hand out positions as well as names, from the one walk
/// that decides what a Hashtag is.
/// </summary>
/// <param name="Start">Index of the <c>#</c> in the title.</param>
/// <param name="Length">Marker plus name, in UTF-16 units.</param>
/// <param name="Key">The folded identity, per <see cref="HashtagKey"/>.</param>
/// <param name="Spelling">The name as written, without the marker.</param>
public readonly record struct HashtagSpan(int Start, int Length, string Key, string Spelling);
