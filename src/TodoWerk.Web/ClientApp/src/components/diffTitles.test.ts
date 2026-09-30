import { describe, expect, it } from 'vitest';
import { diffTitles } from './diffTitles';

/**
 * The confirm dialog's one job is to let somebody see, in each of up to two hundred titles, the
 * token that changes. What changed is read off the two titles the server sent, Hashtag for
 * Hashtag, so the dialog never claims a change the server does not make.
 */
describe('diffTitles', () => {
  it('picks out the Hashtag a rename replaces, on both sides', () => {
    expect(diffTitles('Angebot #Prio1 senden', 'Angebot #Priority1 senden')).toEqual({
      before: [
        { text: 'Angebot ', changed: false },
        { text: '#Prio1', changed: true },
        { text: ' senden', changed: false },
      ],
      after: [
        { text: 'Angebot ', changed: false },
        { text: '#Priority1', changed: true },
        { text: ' senden', changed: false },
      ],
    });
  });

  /**
   * A casing clean-up settles `#work` on `#Work` and leaves the `#Work` beside it alone; the one
   * it leaves alone must not be shown as removed.
   */
  it('leaves an occurrence the Change does not touch plain', () => {
    expect(diffTitles('Do #Work then #work', 'Do #Work then #Work')).toEqual({
      before: [
        { text: 'Do #Work then ', changed: false },
        { text: '#work', changed: true },
      ],
      after: [
        { text: 'Do #Work then ', changed: false },
        { text: '#Work', changed: true },
      ],
    });
  });

  it('marks every source a Merge folds, and not the name it folds them into', () => {
    expect(diffTitles('Angebot #Kunde #klient', 'Angebot #Kunde #Kunde').before).toEqual([
      { text: 'Angebot #Kunde ', changed: false },
      { text: '#klient', changed: true },
    ]);
  });

  it('does not take a # in the middle of a word for a Hashtag', () => {
    expect(diffTitles('Learn C#work #work', 'Learn C#work #Work').before).toEqual([
      { text: 'Learn C#work ', changed: false },
      { text: '#work', changed: true },
    ]);
  });

  it('stops a Hashtag at punctuation', () => {
    expect(diffTitles('Call about #Prio1.', 'Call about #Priority1.').after).toEqual([
      { text: 'Call about ', changed: false },
      { text: '#Priority1', changed: true },
      { text: '.', changed: false },
    ]);
  });

  /** Titles whose Hashtags do not line up cannot be paired, and a wrong mark is worse than none. */
  it('shows both titles plain when their Hashtags do not line up', () => {
    expect(diffTitles('#one #two', '#one')).toEqual({
      before: [{ text: '#one #two', changed: false }],
      after: [{ text: '#one', changed: false }],
    });
  });

  it('shows an unchanged title plain', () => {
    expect(diffTitles('No tags here', 'No tags here')).toEqual({
      before: [{ text: 'No tags here', changed: false }],
      after: [{ text: 'No tags here', changed: false }],
    });
  });
});
