# Marker Rules are a fourth Change, and the one that writes outside a Hashtag

A Marker Rule is a user's standing instruction that a Hashtag carries an emoji at the front of the
title — `#bread` carries 🍞. Bringing titles into line with those rules is a Change of a fourth
shape, **Apply Markers**, that shares everything ADR-0006 built for the other three: the plan table,
the journal, exclusivity against scans, cancel, undo and the ceiling. It is the first Change that
edits text outside a Hashtag span, which reverses a sentence ADR-0006 states plainly, and that is
why this is written down.

## Rules declare; only a Change writes

A rule never writes on its own. Titles change only when the user applies their rules, previewed and
confirmed like any Change, and the scan tick stays a reader. Automatic enforcement after every scan
was the obvious product and was rejected: it would make a timer the first thing in TodoWerk to write
a mailbox without a preview, and the Guide's promise that nothing is written unconfirmed would be
gone for one feature's convenience. If automatic application is ever wanted it is an opt-in on top
of this, not a replacement for it.

Applying only adds. A Marker whose Hashtag has since left the task is kept, not stripped, and
deleting a rule writes nothing. The alternative — reconcile both ways — would turn deleting a rule
into a rewrite of every title it ever touched, and would undo a user's deliberate removal of a
hashtag with a second edit they did not ask for. Stale Markers are a later, explicit "Remove
Markers" run, not a side effect. The one exception is a rule whose Marker the user changed: the next
Apply swaps the retired Marker for the new one inside the block, because that is the rule's own
Marker changing, not a Hashtag going away.

## The block, and what an Apply does to one

All of a task's Markers form one block at position zero, in the order of the user's rules, with one
space after it. An Apply rewrites the whole block on every task it touches: matching Markers in rule
order, then stale Markers already there in their existing order. It never inserts a single Marker
into an existing block, so a one-rule Apply and an all-rules Apply write the same block for the same
task and the order promise holds however the user got there. The leading emoji the extractor sees
is followed by whitespace, so every `#tag` behind a block still indexes, and an emoji is not a name
rune, so a block never becomes a Hashtag.

A Marker is exactly one emoji as a reader sees it — one extended grapheme cluster with the Emoji
property, so a flag, a keycap or a family is one Marker — and is unique across a user's rules. Two
rules sharing 🔴 would make the block stop mapping one Marker to one Hashtag, and a later stale-Marker
count undecidable.

## The Change carries its Markers

The Markers an Apply writes are copied into the Change at confirmation, as the target Spelling
already is. The runner re-reads each task from Graph before writing and recomputes the block from
that title, but from the Change's own Markers, never from the live rules table. A rule edited while
an Apply is queued or running takes effect at the next Apply, and undo and the journal stand without
the rules table.

## Rules follow a Rename, and a Merge asks

A Rename that completes with at least one task written carries the rule to the new name, and undoing
that Rename carries it back under the same condition. A Merge whose sources hold rules asks in the
preview which Marker survives; if the target already has a rule, it wins and the preview says so.
The losing rules are deleted, which writes nothing.

## Where it lives

Rules and the block logic belong to a new **Markers** module, reached from Changes through an
Application-layer abstraction the way the scan scheduler already is. Putting the rule row beside the
occurrences in Indexing was tempting — a code comment there has anticipated a one-row-per-Hashtag
table since M1 — but the index is rebuilt from Graph at will and a rule is not derivable from Graph.
The block grammar sits in the shared Hashtags namespace beside the extractor, where both modules may
read it.

Rules are the first stored user choice in the product and are per-person data: an emoji and a
Hashtag per rule. They join what ADR-0009 lists, the Administrator's Guide repeats it, and erasure
deletes them with everything else.

## Amendment (M7, as built): a deleted rule is kept and marked

"Deleting a rule writes nothing" is what made deletion cheap to reason about, and it is still
true — no title changes. What it does not settle is what happens to the emoji already at the
front of every task the rule was applied to.

Removing the row leaves that emoji recognised by nothing. The block reader stops at the first
grapheme that is not one of the person's Markers, so the next Apply reads the block as ending
before it and writes a **second block in front of the first** — `☕ 🍞☕ Frühstück`, one emoji
twice, stable across every later run. So a deleted rule is kept and marked deleted rather than
removed: listed nowhere, applied nowhere, in no Apply's scope and never swapped, and present
only so the emoji goes on being read as part of the block it is in. (Listed nowhere until the
Remove Markers amendment below, which shows the person the emoji such a row holds, because a row
nobody can see is one nobody can be rid of.) Both uniqueness rules become rules about the rules
that stand, so a deleted rule neither holds its Hashtag nor its emoji against a new one.

The same reasoning covers a Marker a rule stops remembering — the retired one, once an Apply has
swapped it everywhere the plan reached. Tasks that lost the Hashtag before the plan was drawn
were never in it, and still carry the old emoji; a marked row keeps that emoji known too, and is
dropped again the moment the rule remembers it, which is what undoing that Apply does.

Erasure removes the marked rows with everything else, and `PRIVACY.md`, the Administrator's
Guide and [ADR-0009](0009-what-todowerk-stores-about-a-person.md) all say so. Emptying them for
somebody who is still here is Remove Markers' job, which the next amendment builds.

## Amendment (M7): Remove Markers is a fifth Change, and what "stale" means

"Applying only adds" stands. What was missing was the other direction, and until it existed nothing
in the product removed a Marker at all: an emoji whose Hashtag had left the task stayed, a deleted
rule's emoji stayed, and the rows keeping those emoji known were never emptied for somebody who was
still here. **Remove Markers** is that direction.

### Stale is one thing, and the third candidate is unreachable

A Marker in a task's block is **stale** when no standing rule asks for it on that task. A rule asks
for its own Marker on a task carrying its Hashtag. It also asks for the Marker it retired while that
Hashtag is there, because the next Apply swaps that one, and a Remove that took it would clear a
task about to be marked properly. One sentence covers all three things this amendment is
about: an emoji whose Hashtag has gone, one whose rule was deleted, and a retired Marker that
`MarkerPlan` could not attribute to a single rule and therefore left where it was.

The candidate that reads as the safest, "an emoji no rule of theirs has ever mentioned", is the one
this operation is structurally unable to act on, by design. `MarkerBlock.Read`
defines a block only against that person's own Markers, so an emoji nobody made a rule about is not
in a block at all: it is the first character of the rest of the title, deliberately, so that an
Apply cannot pick up somebody's decorative sparkle and start reordering it. A Remove reads the same
block, so it can only ever take text TodoWerk itself put there. That exclusion needs no code.

The remaining inference is real and is accepted: somebody who typed 🍞 on a task with no `#bread`
will be told it is stale. That is not a new claim. An Apply already treats that 🍞 as a block — it
reorders it, and coverage counts it as marked — and `CONTEXT.md` has always defined a Marker as an
emoji a title carries *because a Marker Rule put it there*. The journal records the title as it was
found immediately before each write, so a removal is exactly as recoverable as the addition was,
and the preview shows every pair before anybody agrees to it.

### Scoped by Marker, not by Hashtag

An Apply picks the tasks it covers by Hashtag. A Remove cannot: a stale Marker is stale *because*
the Hashtag has gone, so no key selects those tasks, and a Marker a deleted rule left behind has no
key at all. Its scope is a set of Markers — one, or all of them — carried on the Change's own copy
of the rules rather than in `SourceKeys`, which a Remove leaves empty. Finding the tasks therefore
needs a read the index did not have: every task whose stored title contains any of this person's
Markers, with no Occurrence join, because that join is exactly the filter that hides the population
being measured.

There is no per-task selection. The plan is computed server-side and confirmed whole, as every
Change is — a list of tasks the browser could hand back would be a list of task ids somebody could
edit, and per-task undo was rejected in ADR-0006 as a second product with its own surface.

### A fifth Kind rather than a mode of the fourth

The mechanism is shared entirely — plan table, journal, queue, exclusivity, cancel, undo, the
ceiling — which is the argument that made Apply Markers a Change rather than a new mechanism. It is
not an argument for sharing a *name*. Kind is the axis every reader names a Change by, and
everything that reads `ApplyMarkers` reads it as the promise that applying never removes; a flag
would make that reading wrong in the queue, the history, the dialog and the undo at once. Kinds are
stored as their names in a column that already exists, so a fifth name costs no migration while a
flag would be a new column.

Removing removes and does nothing else: it never adds a Marker the block does not hold, never
reorders what is left, and never swaps a retired Marker. All three are an Apply, and a Remove that
quietly did one would write a title nobody previewed as that. A task whose title is nothing but the
Markers being removed is named in the preview and left alone — Microsoft To Do has no task without
a title, and a Change is not the place to invent one.

### The count ships with it, and the rows are finally emptied

"A count without the action only nags" was the condition, and the action now exists. Each standing
rule carries the number of tasks that no longer hold its Hashtag and still hold its Marker, beside
the coverage figure it already showed; each Marker of a deleted rule is listed on its own with the
same count, which is the only place in the product those emoji are visible at all. Both come from
one walk over the same titles the coverage figure already read — one join lighter than before, and
larger by exactly the stale population it exists to measure.

A completed Remove drops the rows that were keeping its Markers known, under the test
`RecordWrittenMarkersAsync` already uses: every planned task whose block held the Marker was
reached. A cancelled or failed run drops nothing. And because undo is offered for thirty days, the
undo of a Remove writes those rows back for every Marker it restored. Without that, an undone
Remove would leave an emoji in real titles that the block reader no longer recognises, and the next
Apply would write a second block in front of the first: the artefact the kept rows exist to
prevent.

## Considered options

- **A separate run queue for Markers** — rejected. It would have to reproduce exclusivity against
  scans and Changes in a second claim, and a second journal for undo, to avoid widening one
  definition.
- **Automatic enforcement on every scan** — rejected, above.
- **Reconcile both ways** — rejected, above.
- **Strict insert-only into an existing block** — rejected. It cannot keep the order promise once a
  user has applied rules one at a time in the wrong order, and it can never collapse a duplicate.
- **Any short string as a Marker** — rejected. `#red` as a Marker is a Hashtag to the extractor and
  lands in the index; a bracketed word is a title edit in a different product.

## Consequences

- `CONTEXT.md` gains **Marker**, **Marker Rule** and **Apply Markers**, and **Change** is widened to
  two shapes. The Guide describes four Changes — five, after the Remove Markers amendment — and
  promises: rules never write on their own, applying adds and reorders but never removes, a stale
  Marker stays until removed on purpose, and a Rename carries the rule. It promises nothing about
  how To Do sorts or searches a title that starts with an emoji.
- Completed tasks are applied to like any other, as Rename and Merge already do; the index does not
  know completion state and this ADR does not widen it. Recorded in status.md.
- A title the block would push past 255 characters is skipped and named in the preview, the same
  shape as the existing skips. The 1,000-task ceiling is unchanged; a per-rule Apply is the way
  through it.
- Coverage per rule ("n of m tagged tasks carry 🍞") ships now; the stale-Marker count ships with
  Remove Markers, because a count without the action only nags. Both now exist.
