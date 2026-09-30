# What TodoWerk stores about a person, for how long, and what cancelling does

TodoWerk stores, in order to report on its own use: a tenant id, a pseudonymous Entra ID object id,
and two timestamps — when somebody first signed in and when they last did. Nothing else. No name, no
email, no UPN, no IP address, no user agent, and no history between those two moments. *(Amended in
M4 — see below. The sentence is about the record; one diagnostic log line is not part of it and does
mention a browser.)*

It is written only at interactive sign-in. Twelve months without one and everything TodoWerk holds
about that person is destroyed. A person may ask for the same thing at any time, and cancelling
anonymises their membership record in place rather than deleting it — the tenant and the first moment
stay, the object id and the last moment go — so the count of people who have ever used TodoWerk
survives somebody exercising a right, with nobody identifiable in it.

This is a new kind of collection for this codebase and that is why it is written down. Everything
TodoWerk stored before M3 was stored to make a feature work: task titles because the inventory is
computed from them, journals because undo reads them. Two timestamps per person exist so that a
screen can report a number. Collecting for analytics rather than for function is the point at which
a decision needs recording and a notice stops being optional
([PRIVACY.md](../../PRIVACY.md)).

## Amendment (M4): a log line is not the record, and one of them names a browser

The paragraph above is about what TodoWerk *stores in order to report on its own use*. M4 added
something that is neither: a single warning, written when a browser refuses to keep the session
cookie inside the Microsoft Teams frame, carrying the address that failed and the requesting user
agent ([ADR-0010](0010-teams-tab-session-and-framing.md)).

It is not a membership record and it is not analytics. It is written at a moment when TodoWerk does
not know who the caller is — that is the condition being reported — so there is nothing to correlate
it with, and being forgotten neither removes it nor needs to. It exists because the question the
mitigation leaves open is *which* clients refuse the cookie, and a card on somebody's screen in
another country cannot answer it.

It is recorded because "no user agent" was written without a qualifier, and a reader would fairly
take it to cover everything, not only the record. It covers the record.
[PRIVACY.md](../../PRIVACY.md) now has a section of its own for what reaches a server log, because
that is the document somebody actually reads.

Nothing else in this decision changes. No other request writes a user agent, TodoWerk writes no IP
address of its own — the rate limiter partitions on one and does not record it — and the record
itself is exactly what it was.

## Two moments, not a time series

The obvious shape for "how many people were active recently" is a row per sign-in. It answers every
question the two-timestamp version answers and several it cannot: a trend, a weekly rhythm, whether
adoption is rising.

It also *is* a record of somebody's working patterns. A row per sign-in, retained, says which days
somebody works, when they came back from leave, and how their hours changed after a reorganisation —
about a named-enough individual, in a database their colleagues can see aggregates of. The questions
this milestone actually asks are trailing windows: how many people signed in inside the last thirty
days, ninety days, and year. Two moments answer all three exactly.

So there is no time series, and no chart with time on an axis. A trend needs history this milestone
deliberately does not keep, and the deletion of the option is the decision.

## The identifier, and why cancelling does not remove the row

Two things have to be true at once: the cumulative count of people who have ever signed in must not
fall when somebody asks to be forgotten, and nobody who has asked to be forgotten may remain
identifiable.

Deleting the row breaks the first. Keeping the object id breaks the second — a pseudonymous
identifier is still an identifier, and an Entra ID object id resolves to a person for anybody who can
query the directory.

Anonymising in place satisfies both. What is left is a row saying "somebody in this tenant started
using TodoWerk on this date", which is a fact about the tenant and about nobody in particular. The
statistical purpose is met without retaining an identifier, and that is why the statistic is
anonymous and not merely pseudonymous.

The last moment goes with the object id, deliberately. It is what the activity windows are computed
from, so an erased person counts once toward the total and never as present. Somebody who left is not
"active in the last thirty days".

Nothing records that a cancellation happened. No churn figure, no departure count, no flag. A
statistic showing that somebody left is a statistic about somebody leaving, in a tenant of four
people.

## Twelve months, and why it is the same number as the longest window

Retention is configuration, defaulting to twelve months of dormancy — and the Tenant Overview's
longest activity window is *computed from that same setting* rather than from a matching constant.
Two numbers that happen to agree drift; one number cannot. It means the screen can never claim to
report a year of activity while the sweep keeps eighteen months of it, or the reverse.

Twelve months rather than something shorter because the product is used in bursts: somebody who
cleans up their hashtags each quarter is an ordinary user, and a six-month window would forget them
between passes. Rather than something longer because "TodoWerk holds your task titles indefinitely"
was the state this ADR exists to end.

It is also what an administrator revoking TodoWerk in Entra ID amounts to. Revocation is invisible to
TodoWerk, but nobody in that tenant can sign in afterwards, so everybody goes dormant and the sweep
clears the tenant. There is no organisation-wide cancel action inside TodoWerk and none is wanted:
the mechanism is the sweep and the deliverable is documentation.

## Erasure covers what accumulated, not only what was collected

The two timestamps are the smallest part of what erasure destroys. The rest is what using the product
leaves behind: the indexed task titles and their Occurrences, the per-list sync state, the Changes
with their plan rows and journal entries — each journal entry holding a title twice — and the entry
in the durable token cache, so nothing can act as that person afterwards.

That gap predates this milestone. The index arrived in M1 and the journals in M2, with no retention
rule and no erasure route between them, so part of this decision is repair rather than addition.

Each module purges its own rows behind a port declared in the shared application layer, and erasure
orchestrates them. Onboarding names no Indexing or Changes type, which is what keeps the module
boundary tests honest without a new documented exception — the two that exist are deliberate and
commented, not a precedent for more.

## Amendment (M7): a Marker Rule is the first thing a person chooses rather than leaves behind

Everything above is observed: titles TodoWerk read, Changes somebody confirmed, a token they were
issued. A **Marker Rule** is not — it is a standing instruction a person wrote down, one per
Hashtag: an emoji, the Hashtag it is about, and where it sits in their list
([ADR-0014](0014-marker-rules-are-a-fourth-change.md)).

It is per-person data on the same terms as the rest. It joins the table this record describes, it is
destroyed by erasure with everything else, and it goes with the twelve-month retention when somebody
is forgotten for being dormant. The Administrator's Guide says the same, and `PRIVACY.md` lists it.

A rule the person deletes is kept and marked rather than removed. Deleting writes nothing to their
tasks, so the emoji is still at the front of every title the rule was applied to, and a block reader
that no longer recognised it would read the block as ending before it — the next Apply would put a
second block in front of the first. The marked row is what keeps the emoji recognisable, and it is
applied nowhere.

Since Remove Markers ([ADR-0014](0014-marker-rules-are-a-fourth-change.md)) it is not hidden
either: the person is shown the emoji it holds, with how many of their tasks still carry it,
because a row nobody can see is one nobody can be rid of. Removing that emoji from those tasks
destroys the row — the reason for keeping it was that the emoji was out there, and it no longer is.
Erasure removes whatever is left, as before.

It carries no new kind of information about anybody: an emoji and a Hashtag name that person already
wrote into their own tasks.

## What anonymisation costs, knowingly

Two consequences follow from keeping the row and removing the identifier, and both are accepted
rather than accidental.

A person who is erased and later returns is a new row. Recognising them would require comparing
their identifier against one the anonymisation removed, so the cumulative count records arrivals
rather than distinct persons: somebody who exercises erasure yearly and keeps coming back counts
once per cycle. The alternative, keeping a hash of the identifier to deduplicate against, is still
a pseudonym, and pseudonymous is not anonymous.

And the suppression floor cannot be measured against the cumulative count, precisely because that
count keeps the forgotten. The people the statistics describe are the ones who still hold
Occurrences and activity — the identifiable rows — so the floor gates on those. In a tenant of six
where five have been forgotten, the cumulative count passes any sensible floor while every figure
on the screen is about one identifiable colleague. Gating on identifiable rows keeps the floor's
promise after erasure has been exercised, and the average per person divides by the same number for
the same reason.

## Considered options

- **A row per sign-in** — rejected above: it is a record of working patterns, and the questions
  being asked are trailing windows that two moments answer exactly.
- **Delete the membership row on cancellation** — rejected: the cumulative count would fall each time
  somebody exercised a right, which makes the statistic a measure of how many people have not
  objected.
- **Keep the object id and mark the row as erased** — rejected: pseudonymous is not anonymous, and a
  row that names somebody who asked to be forgotten is the thing being asked about.
- **Keep the last-signed-in moment on an anonymised row** — rejected: it would count somebody who has
  left among the people active this month, and an erased person counts as not here.
- **Retain forever and offer erasure on request only** — rejected: it makes the exit depend on
  knowing there is one, and leaves an abandoned account's task titles in the database indefinitely.
- **A separate organisation-wide delete for administrators** — rejected as unnecessary and as a
  weapon. Revocation in Entra ID plus the dormancy sweep already clears a tenant, and a button that
  destroys colleagues' data on one person's authority is not something TodoWerk should own.

## Consequences

- The Tenant Member table is the whole of what is stored about a person for statistical purposes, and
  an architecture test pins its columns so a fourth one cannot arrive quietly and make the privacy
  notice wrong.
- A sign-in whose membership write fails is logged and allowed to proceed. Refusing somebody access
  to the product because a statistics row could not be written is the worse failure, and the cost is
  one uncounted visit and a retention clock that did not move — a failure persistent enough to matter
  is one that stops the sweep too, because both read the same table through the same context.
- Erasure is not exclusive with a scan or a Change the way those two are with each other. A scan
  running at the moment somebody erases themselves is stopped by the disappearance of the row it
  claimed, and the purge repeats itself while it keeps finding rows, so at most it costs a second
  pass. Made properly exclusive it would need a third participant in the claim conditions; that is
  recorded in [status.md](../status.md) rather than built.
- PRIVACY.md exists, and every claim in it has to be true of the behaviour that shipped. It ships
  last in this milestone on purpose: a notice promising erasure should not arrive before erasure does.
