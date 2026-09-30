import { describe, expect, it } from 'vitest';
import { diffBlocks } from './diffTitles';

/**
 * The block an Apply writes, picked out for the reader the way a Hashtag is picked out for the
 * other three Changes. Two long titles that differ in their first few characters are exactly the
 * difference an eye slides over, and this is the moment the product's trust rests on.
 */
describe('diffBlocks', () => {
  it('marks the block that is added and leaves the rest plain', () => {
    const titles = diffBlocks('Brot kaufen #bread', '🍞 Brot kaufen #bread');

    expect(titles.before).toEqual([{ text: 'Brot kaufen #bread', changed: false }]);
    expect(titles.after).toEqual([
      { text: '🍞 ', changed: true },
      { text: 'Brot kaufen #bread', changed: false },
    ]);
  });

  it('strikes the old block and marks the new one when a block is rewritten', () => {
    const titles = diffBlocks('☕🍞 Frühstück #coffee #bread', '🍞☕ Frühstück #coffee #bread');

    expect(titles.before).toEqual([
      { text: '☕🍞 ', changed: true },
      { text: 'Frühstück #coffee #bread', changed: false },
    ]);
    expect(titles.after).toEqual([
      { text: '🍞☕ ', changed: true },
      { text: 'Frühstück #coffee #bread', changed: false },
    ]);
  });

  /**
   * A shared ending must not swallow half a word: "🥐 Brot" and "🍞 Brot" share "rot" and then
   * "Brot" — the mark stops at the word boundary, not inside the word.
   */
  it('never marks part of a word', () => {
    const titles = diffBlocks('🍞 Brot #bread', '🥐 Brot #bread');

    expect(titles.before).toEqual([
      { text: '🍞 ', changed: true },
      { text: 'Brot #bread', changed: false },
    ]);
    expect(titles.after).toEqual([
      { text: '🥐 ', changed: true },
      { text: 'Brot #bread', changed: false },
    ]);
  });

  it('marks nothing when nothing differs', () => {
    const titles = diffBlocks('🍞 Brot', '🍞 Brot');

    expect(titles.before).toEqual([{ text: '🍞 Brot', changed: false }]);
    expect(titles.after).toEqual([{ text: '🍞 Brot', changed: false }]);
  });
});
