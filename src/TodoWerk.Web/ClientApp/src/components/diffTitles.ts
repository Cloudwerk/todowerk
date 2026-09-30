import { HASHTAG_NAME } from './hashtagGrammar';

/** A run of a task title: either a Hashtag the Change touches, or text it leaves alone. */
export interface TitlePart {
  text: string;
  changed: boolean;
}

/**
 * A Hashtag starts at a `#` preceded by whitespace or the start of the title — never mid-word, so
 * `C#` is not a tag (ADR-0005) — and the name after it is the grammar's. Whitespace as
 * `char.IsWhiteSpace` sees it, which is Unicode's White_Space and not JavaScript's `\s`.
 */
const hashtag = new RegExp(`#${HASHTAG_NAME}`, 'gu');
const whitespace = /\p{White_Space}/u;

/**
 * The title before a Change and the title after it, with the Hashtags the Change touches picked
 * out on both sides.
 *
 * Which tokens changed is read off the two titles rather than worked out from spellings: the
 * server has already decided what it rewrites, and it rewrites Hashtags in place, one for one,
 * so the n-th Hashtag before is the n-th Hashtag after and a pair that differs is a pair the
 * Change touched. That is what keeps an occurrence the Change leaves alone — the `#Work` beside
 * the `#work` a casing clean-up settles, or the busiest name a Merge folds the others into — from
 * being shown as removed, and it needs no second opinion on the client about how Hashtags fold.
 *
 * Two titles whose Hashtags do not line up one for one are shown plain: a wrong mark is worse
 * than none, and this is the moment the product's trust rests on.
 */
export function diffTitles(before: string, after: string): { before: TitlePart[]; after: TitlePart[] } {
  const beforeTokens = hashtags(before);
  const afterTokens = hashtags(after);

  if (beforeTokens.length !== afterTokens.length) {
    return { before: plain(before), after: plain(after) };
  }

  const changed = beforeTokens.map((token, index) => token.text !== afterTokens[index].text);

  return {
    before: split(before, beforeTokens, changed),
    after: split(after, afterTokens, changed),
  };
}

/**
 * The title before an Apply Markers and the title after it, with the block the Apply writes picked
 * out. An Apply changes no Hashtag, so {@link diffTitles} would mark nothing and hand the reader
 * two long titles that differ in their first few characters — exactly the difference an eye slides
 * over. What it changes is the front of the title and nothing behind it, so the part the two titles
 * do not share at the front is the part to mark: the old block struck, the new one marked.
 *
 * Read off the two strings rather than with any emoji grammar in the client: the server has already
 * decided what the block is, and it never touches what comes after it.
 */
export function diffBlocks(before: string, after: string): { before: TitlePart[]; after: TitlePart[] } {
  const rest = commonSuffix(before, after);

  return {
    before: leading(before, before.length - rest.length),
    after: leading(after, after.length - rest.length),
  };
}

/**
 * The longest ending both titles share, trimmed so that it begins on a word: a mark never splits a
 * word, and the block's own trailing space is marked with the block rather than left dangling in
 * front of the first word.
 */
function commonSuffix(before: string, after: string): string {
  let length = 0;

  while (
    length < before.length &&
    length < after.length &&
    before[before.length - 1 - length] === after[after.length - 1 - length]
  ) {
    length++;
  }

  let start = after.length - length;

  // A suffix that begins inside a word — "🥐 Brot" and "🍞 Abrot" share "brot" — is moved on to the
  // next word, because marking "🍞 A" would be a wrong mark.
  if (start > 0 && start < after.length && !whitespace.test(after[start - 1]) && !whitespace.test(after[start])) {
    const next = after.slice(start).search(whitespace);

    start = next === -1 ? after.length : start + next;
  }

  while (start < after.length && whitespace.test(after[start])) {
    start++;
  }

  return after.slice(start);
}

/** The first `length` characters marked as changed, and the rest plain. Nothing marked when nothing differs. */
function leading(title: string, length: number): TitlePart[] {
  const changed = title.slice(0, length);
  const rest = title.slice(length);
  const parts: TitlePart[] = [];

  if (changed !== '') parts.push({ text: changed, changed: true });
  if (rest !== '') parts.push({ text: rest, changed: false });

  return parts;
}

interface Token {
  start: number;
  text: string;
}

function hashtags(title: string): Token[] {
  const tokens: Token[] = [];

  for (const match of title.matchAll(hashtag)) {
    const start = match.index;

    if (start === 0 || whitespace.test(title[start - 1])) {
      tokens.push({ start, text: match[0] });
    }
  }

  return tokens;
}

function split(title: string, tokens: Token[], changed: boolean[]): TitlePart[] {
  const parts: TitlePart[] = [];
  let plainFrom = 0;

  tokens.forEach((token, index) => {
    if (!changed[index]) return;

    if (token.start > plainFrom) {
      parts.push({ text: title.slice(plainFrom, token.start), changed: false });
    }

    parts.push({ text: token.text, changed: true });
    plainFrom = token.start + token.text.length;
  });

  if (plainFrom < title.length) {
    parts.push({ text: title.slice(plainFrom), changed: false });
  }

  return parts;
}

function plain(title: string): TitlePart[] {
  return title === '' ? [] : [{ text: title, changed: false }];
}
