/**
 * The grammar of a Marker, mirrored from `TodoWerk.Domain.Hashtags.Marker`: exactly one extended
 * grapheme cluster that reads as an emoji. No emoji dataset — `Intl.Segmenter` finds the cluster
 * and Unicode property escapes decide what it is, with flags and keycaps recognised as the
 * sequences they are because neither opens on a pictograph.
 */

import { HASHTAG_NAME } from './hashtagGrammar';

const PICTOGRAPH = /^\p{Extended_Pictographic}$/u;

/** Mirrors `Marker.MaxLength`: what the server would refuse by length before looking. */
const MAX_LENGTH = 32;

/**
 * A cluster the Hashtag grammar would read as a tag: the hash keycap opens on `#` and its two
 * combining marks are name characters, so a block of it would be indexed as a Hashtag the moment
 * it was written. The server refuses it for the same reason (`Marker.TryCreate`).
 */
const OPENS_A_HASHTAG = new RegExp(`^#${HASHTAG_NAME}`, 'u');

const REGIONAL_INDICATOR = /^\p{Regional_Indicator}$/u;

/** A digit, `#` or `*`, an optional variation selector, and the combining enclosing keycap. */
const KEYCAP = /^[0-9#*]️?⃣$/u;

/**
 * What the marker box can say about what has been typed or pasted into it, or `undefined` when it
 * has nothing to say. The server stays the authority on what a Marker is; this exists so that two
 * emoji or a letter are answered by the box rather than by a refusal a round trip later.
 *
 * An empty box gets no message: there is nothing to correct in it yet. Neither does a box in a
 * browser without `Intl.Segmenter`, where this cannot tell one emoji from two — saying nothing and
 * letting the server answer is right, and guessing is not.
 */
export function validateMarker(marker: string): string | undefined {
  if (marker === '' || !canSegment()) return undefined;

  if (isMarker(marker)) return undefined;

  return OPENS_A_HASHTAG.test(marker.normalize('NFC'))
    ? 'That begins with # and would be read as a hashtag.'
    : 'One emoji, and only one.';
}

/**
 * Whether this is exactly one emoji as a reader sees it. False where segmentation is unavailable,
 * which is why {@link validateMarker} asks that first rather than reading a refusal into it.
 */
export function isMarker(candidate: string): boolean {
  if (candidate === '' || candidate.length > MAX_LENGTH || !canSegment()) return false;

  const cluster = onlyGrapheme(candidate.normalize('NFC'));

  if (cluster === undefined) return false;

  if (OPENS_A_HASHTAG.test(cluster)) return false;

  // A block is markers written back to back: a trailing joiner would fuse this one with the next
  // into a cluster no rule matches, and every Apply would write the block again in front of itself.
  if (cluster.endsWith('\u200D')) return false;

  const scalars = [...cluster];

  // A skin tone, a ZWJ family and a tag-sequence flag all open on a pictograph, so one test covers
  // them: the segmenter has already decided where the cluster ends.
  if (PICTOGRAPH.test(scalars[0])) return true;

  // A flag is a pair of regional indicators. One on its own is a letter in a box.
  if (REGIONAL_INDICATOR.test(scalars[0])) {
    return scalars.length === 2 && REGIONAL_INDICATOR.test(scalars[1]);
  }

  return KEYCAP.test(cluster);
}

/**
 * Whether two texts are the same Marker, by the server's rule rather than by string equality:
 * `Marker` normalises to NFC and takes its identity from the text with the presentation selectors
 * removed, so a bare `✉` and a `✉️` that render alike are one Marker and not two
 * (`TodoWerk.Domain.Hashtags.Marker`).
 *
 * It matters wherever the client decides whether something it is showing *is* a Marker the person
 * already has. Compared as raw strings, the marker grid would leave no button pressed for a rule
 * stored in the other presentation.
 */
export function sameMarker(left: string, right: string): boolean {
  return identity(left) === identity(right);
}

/** NFC, with the emoji and text presentation selectors removed. Mirrors `Marker`'s own identity. */
function identity(marker: string): string {
  return marker.normalize('NFC').replaceAll('️', '').replaceAll('︎', '');
}

function canSegment(): boolean {
  return typeof Intl !== 'undefined' && typeof Intl.Segmenter === 'function';
}

/** The one grapheme cluster in `text`, or `undefined` when there is not exactly one. */
function onlyGrapheme(text: string): string | undefined {
  const graphemes = [...new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(text)];

  return graphemes.length === 1 ? graphemes[0].segment : undefined;
}
