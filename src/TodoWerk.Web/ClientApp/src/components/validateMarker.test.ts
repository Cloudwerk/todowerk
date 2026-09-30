import { describe, expect, it } from 'vitest';
import { isMarker, sameMarker, validateMarker } from './validateMarker';

/**
 * The client's half of the Marker grammar, which has to agree with
 * `TodoWerk.Domain.Hashtags.Marker` exactly — the field's whole purpose is to refuse before the
 * round trip what the server would refuse after it, and to accept everything it would accept.
 */
describe('validateMarker', () => {
  it.each([
    ['a plain emoji', '🍞'],
    ['a skin-toned hand', '👍🏽'],
    ['a family', '👨‍👩‍👧'],
    ['a flag', '🇩🇪'],
    ['a keycap', '1️⃣'],
    ['a star keycap', '*️⃣'],
    ['a tag-sequence flag', '🏴󠁧󠁢󠁥󠁮󠁧󠁿'],
    ['a heart with a variation selector', '❤️'],
  ])('accepts %s as one marker', (_name, marker) => {
    expect(isMarker(marker)).toBe(true);
    expect(validateMarker(marker)).toBeUndefined();
  });

  it.each([
    ['two emoji', '🍞🥐'],
    ['an emoji with a letter stuck to it', '🍞x'],
    ['a letter', 'x'],
    ['a word', 'bread'],
    ['a digit on its own', '1'],
    ['punctuation', '!'],
    ['a space', ' '],
    ['half a flag', '\u{1F1E9}'],
    ['a fragment ending in a joiner', '👩\u200D'],
    ['something longer than a marker can be', '🍞'.repeat(17)],
  ])('refuses %s', (_name, marker) => {
    expect(isMarker(marker)).toBe(false);
    expect(validateMarker(marker)).toBe('One emoji, and only one.');
  });

  /**
   * One emoji by every other measure, and the one the Hashtag grammar would read as a tag — so
   * "one emoji, and only one" would be a lie about it, and the box says what is actually wrong.
   */
  it('refuses the hash keycap, and says why', () => {
    expect(isMarker('#️⃣')).toBe(false);
    expect(validateMarker('#️⃣')).toBe('That begins with # and would be read as a hashtag.');
  });

  /** An empty box has nothing to correct in it yet. */
  it('says nothing about an empty value', () => {
    expect(validateMarker('')).toBeUndefined();
    expect(isMarker('')).toBe(false);
  });
});

/**
 * `Marker` takes its identity from the text with the presentation selectors removed, so two
 * spellings of one emoji are one Marker on the server. Compared as raw strings, the marker grid
 * would show no button pressed for a rule stored in the other presentation.
 */
describe('sameMarker', () => {
  it.each([
    ['an envelope with and without the emoji selector', '✉', '✉️'],
    ['a heart with and without it', '❤', '❤️'],
    ['a marker against itself', '🍞', '🍞'],
  ])('reads %s as one marker', (_name, left, right) => {
    expect(sameMarker(left, right)).toBe(true);
    expect(sameMarker(right, left)).toBe(true);
  });

  it.each([
    ['two different emoji', '🍞', '🥐'],
    ['an emoji and nothing', '🍞', ''],
  ])('keeps %s apart', (_name, left, right) => {
    expect(sameMarker(left, right)).toBe(false);
  });
});
