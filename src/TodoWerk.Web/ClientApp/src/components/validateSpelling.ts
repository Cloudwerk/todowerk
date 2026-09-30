import { HASHTAG_NAME } from './hashtagGrammar';

const name = new RegExp(`^${HASHTAG_NAME}$`, 'u');

/**
 * What the dialog can say about a new spelling as it is typed, or `undefined` when it has nothing
 * to say. Only the three things the hint beside the box already promises: the server stays the
 * authority on whether a name is a Hashtag, and this exists so that a space or a leading `#` is
 * answered by the box rather than by a refusal a debounce later.
 *
 * An empty box gets no message. There is nothing to correct in it yet.
 */
export function validateSpelling(spelling: string): string | undefined {
  if (spelling === '') return undefined;
  if (spelling.startsWith('#')) return 'Leave out the #.';
  if (/\s/u.test(spelling)) return 'No spaces.';
  if (!name.test(spelling)) return 'Only letters, digits, hyphens and underscores.';

  return undefined;
}
