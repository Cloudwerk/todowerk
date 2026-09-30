# What a Change is, and how one reaches Microsoft To Do

> Widened twice by [ADR-0014](0014-marker-rules-are-a-fourth-change.md). It adds a fourth shape,
> **Apply Markers**, which carries neither source Hashtags nor a target Spelling and is stated
> rather than derived; its later amendment adds a fifth, **Remove Markers**, which is the same write
> in the other direction and is scoped by Marker rather than by Hashtag. Everything below about the
> machinery — the plan, the journal, the queue, exclusivity, cancel, undo and the ceiling — is true
> of all five; the sentences about what a Change *is made of* describe the three this record was
> written for.

A Change carries a set of source Hashtags and one target Spelling, and rewrites every
Occurrence of those Hashtags to that Spelling. Which of the three operations the user
performed is derived from the Change rather than chosen: one source folding to the same key
is **Normalise Casing** (`works` → `Works`), one source folding to a new key is a **Rename**
(`Prio1` → `Priority1`), and two or more sources is a **Merge** (`Work` + `Works` → `Works`).
One mechanism, three names it reports back — so "unify these two tags and call them this" is
a single confirmed operation rather than a grouping step followed by a rename.

The Change is the unit of everything: it is previewed as a whole, queued as a whole, run as a
whole, and undone as a whole.

## Only the Hashtag is rewritten

> True of three Changes rather than of all five. [ADR-0014](0014-marker-rules-are-a-fourth-change.md)
> adds Apply Markers, which writes a block of emoji at the front of a title and no Hashtag at all —
> and says why that is a Change and not something else.

The edit is minimal. Each Occurrence's span in the title is replaced and every other
character — spacing, punctuation, the rest of the sentence — is left exactly as it was.
TodoWerk is a hashtag manager, not a title editor, and the smallest defensible edit is the
one a user can predict.

The consequence is accepted rather than smoothed over: merging `#kunde` into `#customer` on
*"#kunde and #customer"* produces *"#customer and #customer"*, and normalising *"#Work #work
planning"* produces *"#Work #Work planning"*. Collapsing the duplicate would mean deciding
which one survives and what happens to the whitespace around it, and it would turn undo from
a stored string into text surgery. The preview shows the resulting title, so the duplicate is
visible before anybody confirms it.

A target Spelling is accepted when it round-trips: prepend `#`, run it through the extractor,
and require exactly one Hashtag back whose Spelling is the target. That is the only test that
enforces ADR-0005's second compatibility property — every title a write produces still
highlights in the clients — and it inherits the extractor's conservatism, so a target the To
Do clients would accept but the extractor does not is refused. Under-recognising is the safe
direction for the index; here it is merely restrictive, and that trade is deliberate.

## Microsoft To Do offers no optimistic concurrency

`Update todoTask` documents two request headers, `Authorization` and `Content-Type`. There is
no `If-Match`, and `todoTask` carries no ETag to send in one — Planner has both, To Do does
not. There is no compare-and-swap to be had, at any price.

So each task is re-read from Graph immediately before it is written, its Hashtags re-extracted
from the title that comes back, and the rewrite applied to *that* title. If the Hashtag is no
longer in it, the task is skipped: the user asked for a tag to change, and there is no longer
a tag there to change. The Change Journal records the title actually read, never the title the
preview showed, so undo restores what was really there.

A race window between that read and the PATCH remains, and no To Do API can close it. It is
written down here rather than papered over, and it is the reason preview counts are advisory
and the UI says so.

Titles are read from Graph for this purpose and never taken from the index: `IndexedTask.Title`
is truncated to the display column and is kept for showing and comparing, not for writing back.

## One PATCH at a time, through the gateway that already exists

Writes go through a method added to `GraphGateway`, sequentially, one task per request.

`$batch` was the obvious alternative and does not pay. Graph caps a batch at 20 requests; the
Outlook service runs at most four of them in parallel regardless, so the ceiling on
concurrency is four either way. Worse, a throttled item inside a batch comes back as a per-item
`429` inside an overall `200`, which no SDK retries for you — so batching means reimplementing
`Retry-After` handling, the one-shot 401 refresh, the host allowlist and the error mapping that
the gateway already has, for a second transport, in exchange for fewer round trips against a
service that will not run them any faster.

If a live mailbox proves this too slow, the measurement is the thing to bring back to this
decision. Paging and throttling against live Graph are still unobserved (see
[status.md](../status.md)), so the number does not exist yet.

## The plan, the journal, and undo

A confirmed Change persists its plan: one row per task, carrying the task, the title as
previewed, and a status. That fixes the scope to what the user actually confirmed — a task
that acquires the tag afterwards is not swept in — and gives progress, resume and cancel one
shared mechanism. A Change that loses its lease or meets a deploy resumes at the first pending
row rather than restarting.

Cancel stops the run after the current task. There is no separate pause: a paused Change
holding a lease is a wedged row waiting to be discovered, and "cancel, then queue the rest
again" is the same thing without the trap.

Every task actually written gets a Change Journal row: the task, its list, the full title read
before the write, the title written, and when. Undo is offered for a whole Change, one level
deep, inside 30 days, and runs as a Change in the other direction — queued, exclusive,
sequential, journaled like any other. A task is restored only if its current title still equals
what TodoWerk wrote; anything else means somebody edited it since, and their edit wins.

Per-task undo is not offered. Thirty days is a retention decision, not a technical bound: the
scan queue purges after seven because it is a queue, and the journal is the opposite of a queue.

## Changes and scans never run at the same time for one user

The Changes module owns its own table and its own worker. It does not write index rows — the
architecture tests forbid a module reaching into another, and the boundary is worth more here
than the latency it costs. When a Change finishes it queues a scan of the affected lists, and
Graph stays the single source of truth for the index. The Workbench is therefore briefly behind
after a Change completes, and shows that rather than hiding it.

For one user, a Change and a scan are mutually exclusive, enforced in each claim. Overlapping
them is not corrupting — one writes Graph, the other writes the index — but it shows a
half-renamed inventory to somebody watching, and the numbers move under them.

Exclusivity alone would make "start immediately" false, because a first scan is minutes long.
So a queued Change preempts a running scan at a page boundary: the scan runner already renews
its lease per page and already knows how to hand its row back, and its per-list progress means
it resumes having lost nothing. A scan that has been preempted once runs to completion the next
time it is claimed, so a user queueing Changes in a row cannot starve their own index.

Only one Change per user exists at a time. A second one is refused at confirmation with the
reason, rather than queued to run against a plan the first one is about to invalidate.

## A plan is only as complete as the index behind it

Per ADR-0003, writes never run against a half-scanned list. A plan is computed from Occurrences,
so a list that has never finished a scan contributes no rows and the plan is silently short.

The Change is planned over the completely-scanned lists only, and the preview names the lists
left out and why. Refusing the whole Change would punish a user for one list Graph is
throttling; ignoring the gap is the silent shortfall ADR-0003 wrote that sentence to prevent.

## Considered options

- **Compare-and-swap on an ETag** — not available. `todoTask` has none, and `Update todoTask`
  accepts no `If-Match`. Listed because its absence is the reason for the re-read.
- **Write the title stored in the index** — rejected: it is truncated at 512 characters for
  display and is by definition as old as the last scan.
- **`$batch` for writes** — rejected on the numbers above: no more concurrency, and per-item
  throttling that nothing retries.
- **Merge as a TodoWerk-side grouping that writes nothing** — rejected. It introduces a
  persisted concept Microsoft To Do has no counterpart for, breaks the rule that an inventory
  row is one folded key, and defers the write the user actually asked for.
- **Per-task undo** — rejected for v1: a second product with its own surface, and no demand
  yet that a whole-Change undo does not meet.
- **Chunking a Change past the ceiling automatically** — rejected: it invents Changes nobody
  confirmed and makes undo ambiguous. Past the ceiling the Change is refused, with the count.

## Consequences

- `CONTEXT.md` gains **Rename**, **Change** and **Change Journal**, and **Normalise Casing** is
  sharpened to say that two Hashtags which merely look alike (`Work`, `Works`) are a Merge.
- Preview counts are advisory, because the run re-reads and may skip. The UI must say so rather
  than present them as a promise.
- The inventory is behind by one scan after a Change completes, and shows it.
- Writing a task gives it a new `lastModifiedDateTime`, so the stale flag resets on everything a
  Change touched. Accepted: those are the tags the user just deliberately handled, which makes it
  wrong in the harmless direction. It becomes wrong in the unhelpful direction the day somebody
  renames in bulk without caring, and that is recorded in [status.md](../status.md).
- A Change is bounded by a configurable ceiling, defaulted to 1,000 tasks, because it holds the
  user's exclusivity for its whole run.
- `IIndexScanScheduler` moves to the Application layer's shared abstractions, so the Changes
  module can queue a scan without reaching into `Indexing`.
