/**
 * The name of a Hashtag, as ADR-0005 settled it and `HashtagExtractor` reads it: one or more
 * letters, marks, decimal digits, `_` or `-`. Not an ASCII class — that truncates `#Prüfung` to
 * `#Pr`. One source for both places the client needs it, so a dialog cannot mark a token its own
 * validator refuses.
 */
export const HASHTAG_NAME = String.raw`[\p{L}\p{M}\p{Nd}_-]+`;
