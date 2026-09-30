# What a Hashtag is, and why casing does not distinguish one

A Hashtag is a token TodoWerk extracts from a task title; Microsoft To Do has no such
concept server-side, so TodoWerk defines the grammar and treats the To Do clients as a
compatibility constraint on it rather than as its source. Two Hashtags are the same
Hashtag when their names fold to the same key under NFC normalisation and invariant
case folding — so `#Work` and `#work` are one Hashtag with two observed Spellings, not
two Hashtags. That folded key is computed in C# and stored in a binary-collated column,
because SQL Server's default collation and .NET disagree about which strings are equal.

## Microsoft specifies nothing here

There is no first-party specification to adopt. Searching Microsoft Learn — the To Do API
overview, the `todoTask` resource, the PST export field mapping, the Graph Toolkit
`mgt-todo` component — returns no mention of hashtags in any form. `todoTask.title` is an
opaque string that maps to `subject` on export. The hashtag is a client-side rendering
affordance: undocumented, unversioned, free to differ between the Windows, web, mobile and
Outlook clients, and free to change without notice.

That is why the index cannot simply copy client behaviour. Per
[ADR-0003](0003-single-sql-store-own-index.md) TodoWerk maintains its own SQL index, and a
persisted uniqueness key derived from reverse-engineered client rendering would have its
meaning silently changed by a client update. Instead the client relationship is expressed
as two properties the extractor must satisfy, which survive client drift in a way a copied
regex does not:

- **Recognition is conservative.** Everything TodoWerk calls a Hashtag is also highlighted
  by the clients. Otherwise TodoWerk offers to rename text the user does not see as a tag.
- **Writes preserve tagging.** Every title a rename or merge writes still highlights in the
  clients. Otherwise the feature destroys the tags it exists to manage.

## Identity folds case, whatever the clients do

The decisive argument is that this choice does not depend on the empirical question. If the
clients turn out to be case-sensitive when searching, then `#Work` and `#work` failing to
find each other is precisely the defect the user bought TodoWerk to fix — one inventory row
carrying a casing-inconsistency flag is still the honest model. If they are case-insensitive,
the same row is right and the flag is merely cosmetic. The probe result changes the severity
copy on the flag; it does not change the schema. M1 is therefore not blocked on it.

Consequences for the model, which is why `CONTEXT.md` gained three terms alongside this ADR:

- The inventory has one row per Hashtag. What varies between tasks is the **Spelling**, and
  a Hashtag holds the set of Spellings observed across its **Occurrences**.
- Resolving `#Work` versus `#work` is **normalising casing on one Hashtag**, not merging two.
  Merge is reserved for genuinely distinct Hashtags (`#kunde` into `#customer`). These are
  separate operations with separate previews, and conflating them was a real risk: the README
  had listed "casing variants" as a near-duplicate flag, which the case-insensitive model
  makes incoherent.
- The **Canonical Spelling** is the most frequently occurring Spelling, ties broken by most
  recent use, always overridable by the user. Lower-casing everything is not available: in
  `#ProjectAlpha` the casing carries meaning.

## The folded key is computed in C# and compared as bytes

`Key = NFC(ToUpperInvariant(NFC(name)))`, persisted in its own column under
`Latin1_General_100_BIN2`, with the Canonical Spelling stored separately for display. The
unique index is on the key column.

The fold runs upwards rather than downwards, which is a correction to this ADR made when the
extractor was implemented: `CA1308` is an error in this build, and its reasoning holds here.
Upper-casing is the direction that survives round-tripping, and it folds Greek final sigma
(`ς`) onto `σ`, where lower-casing leaves two Hashtags a reader sees as one word. The direction
changes nothing in the table below — .NET applies simple case mapping, so `ß` is left alone
either way — and the key is never displayed, so its case is free.

This looks like over-specification until you measure what the alternatives actually do.
Both tables below were run rather than recalled — .NET 10.0.10 with ICU, and SQL Server 2025
(17.0.4025.3), whose default collation on a fresh instance is `SQL_Latin1_General_CP1_CI_AS`:

| Pair | .NET `Ordinal` | .NET `OrdinalIgnoreCase` | .NET `InvariantCultureIgnoreCase` | SQL `Latin1_General_100_CI_AS` | SQL `..._BIN2` |
| --- | --- | --- | --- | --- | --- |
| `Work` / `work` | differ | **equal** | **equal** | **equal** | differ |
| `Straße` / `Strasse` | differ | differ | differ | **equal** | differ |
| `Prüfung` NFC / NFD | differ | differ | **equal** | **equal** | differ |
| `Prüfung` / `Prufung` | differ | differ | differ | differ (`CI_AI`: **equal**) | differ |

Two divergences fall out, and both are silent data corruption rather than a crash you would
notice in development:

- **`ß` versus `ss`.** SQL's default collation calls them equal; every .NET comparison calls
  them different. The application treats `#Straße` and `#Strasse` as two Hashtags and the
  unique index rejects the second with a duplicate-key violation — on German data, which is
  the data this product was built against.
- **Canonical equivalence.** A `ü` typed on one client may arrive precomposed (U+00FC) and on
  another decomposed (U+0075 U+0308). They are indistinguishable on screen. SQL's default
  collation calls them equal; .NET `Ordinal` calls them different, so the application creates
  two identical-looking rows and the index rejects one of them. `InvariantCulture` happens to
  collapse them, but as a side effect of a comparer that cannot be persisted as a key.

Binary collation on a key that C# alone computes removes SQL's opinion from the question
entirely: the database performs byte equality on a value whose folding rules live in one
place, in testable code. The explicit NFC pass then buys canonical equivalence deliberately
instead of inheriting it from a collation. The outer NFC is belt-and-braces against case
mapping denormalising its input.

The same trap exists one layer up and bit during this work: PowerShell's `-ceq` is
culture-aware, so it reports NFC and NFD as equal. Comparisons that mean "the same bytes"
must say `Ordinal` explicitly, in any language.

## The grammar, and what is still provisional

`scripts/Probe-HashtagGrammar.ps1` seeds a scratch To Do list with the cases below and emits
a checklist to fill in against real clients. Until those answers land, the extractor ships
with these rules, chosen to be conservative — under-recognising leaves a tag unmanaged, while
over-recognising offers to rewrite text that is not a tag:

- A Hashtag starts at a `#` preceded by whitespace or the start of the title, never mid-word.
  This is what keeps `C#` and `foo#bar` from producing tags, and it is the one rule the
  third-party write-ups agree on ("space + `#`").
- The name is one or more Unicode letters, marks, digits, `_` or `-`. Deliberately not
  ASCII-only: an ASCII class truncates `#Prüfung` to `#Pr` and silently corrupts the index of
  any German tenant.
- Anything else terminates the name, so trailing `.` `,` `)` `:` `/` are excluded. A tag and
  the same tag at the end of a sentence must not become two inventory rows.
- A name of zero length is not a Hashtag.
- Titles only. Note bodies are out of scope for v1.

Recorded as genuinely uncertain, with the chosen behaviour above standing until measured:

- Whether the clients highlight non-ASCII letters at all, and whether `_`, `-` and `:` are
  name characters or terminators. Highest-risk unknowns, since they decide row counts.
- Whether a `#` directly after an opening bracket or a quote opens a tag — `(#klammertag)`.
  Found while implementing the extractor, and not a question the probe asks: P09 seeds the case
  but asks only about the closing bracket. The boundary rule above is whitespace-or-start, so
  the extractor recognises nothing here at all, which is the under-recognising side.
- Whether digit-only names (`#2026`) are tags to the clients.
- Whether symbols such as emoji are name characters.
- Whether a `#` at the very start of a title is recognised, given every informal source
  describes the rule as "space + `#`".
- Whether note bodies are tagged in the clients, which would make the title-only scope a
  stated v1 limitation rather than a description of the platform.
- Whether the clients agree with each other. Divergence is the outcome that would most change
  this ADR: it would mean there is no single grammar to be compatible with, and the
  conservative rule becomes the intersection of the clients rather than any one of them.
- Whether Graph itself normalises Unicode on write. The probe script answers this without
  human involvement by comparing sent and returned titles codepoint by codepoint.

## Considered options

- **Case-sensitive identity, casing collisions resolved by merge** — rejected. It makes M1's
  schema depend on an unanswered question about undocumented client behaviour, contradicts
  the model that the inventory is a view of the user's intended vocabulary, and produces two
  rows for what every user reads as one mistake.
- **Lower-case the display name as well as the key** — rejected. `#ProjectAlpha` would become
  `#projectalpha`; discarding casing destroys information the user chose.
- **Let SQL's collation define identity, with a unique index directly on the name** —
  rejected on the measurements above. It also splits the folding rule across C# and a
  collation setting, where a restore onto an instance with a different default collation
  silently changes what the product considers the same Hashtag.
- **Derive the grammar from client behaviour and follow it as it changes** — rejected as the
  primary rule. Kept only as the two compatibility properties, which are testable and do not
  put undocumented behaviour underneath a persisted key.
