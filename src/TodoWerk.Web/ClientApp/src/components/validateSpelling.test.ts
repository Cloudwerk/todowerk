import { describe, expect, it } from 'vitest';
import { validateSpelling } from './validateSpelling';

/**
 * What the dialog can tell somebody as they type, before the server is asked. Only the things the
 * hint beside the box already promises — the server stays the authority on whether a name is a
 * Hashtag, and an empty box is not scolded: there is nothing to say about it yet.
 */
describe('validateSpelling', () => {
  it.each(['Priority1', 'prio-1', 'prio_1', 'Prüfung', '2026'])('accepts %s', (spelling) => {
    expect(validateSpelling(spelling)).toBeUndefined();
  });

  it('says nothing about an empty box', () => {
    expect(validateSpelling('')).toBeUndefined();
  });

  it('names a space as the problem', () => {
    expect(validateSpelling('prio 1')).toBe('No spaces.');
  });

  it('names a leading # as the problem', () => {
    expect(validateSpelling('#prio1')).toBe('Leave out the #.');
  });

  it('names any other character as the problem', () => {
    expect(validateSpelling('prio.1')).toBe('Only letters, digits, hyphens and underscores.');
  });
});
