# Privacy notice

Last updated: 2026-09-22.

TodoWerk reads the hashtags out of your Microsoft To Do tasks, shows you what you have, and changes
them when you say so. To do that it keeps a copy of some of your task titles, and a small record that
you use it at all. This page says what those are, how long each is kept, what deletes them, and how
to have everything removed without waiting.

It describes the service CloudWerk operates at todowerk.cloudwerk.de, as the software behaves today.

## Who holds your data

CloudWerk GmbH operates TodoWerk and holds the database. Everything below is held by CloudWerk on
your organisation's behalf, as a processor acting on your instructions. The service is offered
through the Microsoft Teams Store at no charge.

## What is stored about you as a person

This is what TodoWerk collects in order to report on its own use, rather than to make a feature
work. It is the smallest thing that could answer the question.

| What | Why |
| --- | --- |
| Your organisation's Microsoft Entra ID tenant id | So the counts below are counts of your organisation, and not of somebody else's |
| Your Entra ID object id | A pseudonymous directory identifier. It is what distinguishes you from a colleague without naming you |
| When you first signed in | So a tenant can see how long TodoWerk has been in use there |
| When you last signed in | So "active recently" can mean something |

That is all of it. No name, no email address, no user principal name, no IP address, no user agent,
no device information, and no history of your visits beyond the first and the last. TodoWerk holds
no record of your working patterns.

The record is written only when a person signs in. A background sync running on a timer does not
count as you using TodoWerk and does not touch it.

## What appears in the server log

A log is not a record kept about you. It is what an operator reads while something is going wrong,
and how long it survives is theirs to decide, not something this notice can promise.

TodoWerk writes almost nothing about a person there. One line mentions your browser, and it is
written in one situation: your browser refused to keep TodoWerk's session while it ran inside
Microsoft Teams, which Safari does by design and which you would have just been shown a card about.
The line records the address of the request that failed, `/api/me`, and your browser's user agent,
the string every website you visit already receives. It exists to answer one open question: which
browsers refuse the session. It does not name you. At the moment it is written TodoWerk does not
know who you are, which is the condition being reported.

No other request writes a user agent, and TodoWerk writes no IP address anywhere. It uses your
address to decide how many requests a minute you may make, and does not record it.

## What is stored as a consequence of using it

This is what the product is made of, not what it reports on.

| What | How long it is kept |
| --- | --- |
| Your task titles, one row per task, for every task list TodoWerk can read | Until you are forgotten (below). Kept current in the background; a task deleted in Microsoft To Do is removed from the index on the next sync |
| The hashtag Occurrences extracted from those titles | The same |
| Per-list sync state: which list, its name, how far the last read got, and why it failed if it did | The same |
| Your changes: which hashtags were renamed or merged into what, and the per-task plan behind each one | 30 days from when the change finished |
| Change journals: for every task a change wrote, the title read immediately before the write and the title written | 30 days. This is what makes a change undoable, and it is the only record of what a task used to be called |
| Your marker rules: for each one, an emoji, the hashtag it is about, and where it sits in your list | Until you are forgotten (below). A rule is something you wrote down, not something TodoWerk observed. Deleting one takes it off your list and keeps a note of the emoji and the hashtag, marked deleted, because the emoji is still at the front of the tasks the rule was applied to and TodoWerk has to go on recognising it there. The note is listed under your markers so you can see it, and it is destroyed once you remove that emoji from the tasks carrying it. Being forgotten destroys it either way |
| Your Microsoft Graph refresh token, encrypted at rest | 90 days after it was last used, or until you sign out or are forgotten |

Task titles are the sensitive part. A title is whatever you wrote in it, and TodoWerk holds a copy
to count hashtags. Journal entries hold a title twice, before and after, which is why they are the
shortest-lived thing here.

TodoWerk never creates, completes or deletes one of your tasks. When it writes one it rewrites only
the hashtag's own text. The spacing, the punctuation and the rest of the sentence are left exactly
as they were.

## What your colleagues can see

Any signed-in person in your organisation can open the Tenant Overview, which shows:

- how many people in the organisation have signed in
- how many of them signed in inside each of three trailing windows
- when the first of them started using TodoWerk
- how many hashtag Occurrences they hold between them, and the average per person

Counts, and nothing else. It never names anybody, never shows a per-person row, never lists which
hashtags exist in the organisation, and never compares one person's hashtags with another's. No
surface anywhere in TodoWerk does.

While fewer than five people are on record, the statistics are not shown at all. People who have
been forgotten do not count toward that five, because the figures describe only the people still
here. In a tenant that small, a total plus one reader's own knowledge is an inference about
identifiable colleagues, and the floor is what stands between an aggregate and that inference. It
is checked on every request, in both directions, so a tenant that had the statistics can lose them
again when enough people are forgotten. That is the floor doing its job.

One consequence of being forgotten is worth knowing. Somebody who is forgotten stays in the count of
people who have ever signed in, and if they later return they are counted anew. Recognising a
returning person would mean keeping exactly the identifier that being forgotten removes.

## Being forgotten

**On your own initiative, at any time.** "Delete my data" sits beside "Sign out" in TodoWerk, and
beside "Open in browser" in the Microsoft Teams tab. The tab offers no sign-out, because signing out
of TodoWerk while you are still signed in to Teams is a state the next visit would undo. It asks you
to confirm, because it cannot be undone. It destroys:

- your indexed task titles and their hashtag Occurrences
- your per-list sync state
- your changes, their plans and their journals, so nothing can be undone afterwards
- your stored Graph refresh token, so nothing can act as you afterwards

and it anonymises the record described in *What is stored about you as a person*: your object id and
your last sign-in are removed, while the tenant id and the first sign-in date remain. What is left is
a row saying that somebody in your organisation started using TodoWerk on a date. It names nobody and
cannot be resolved back to you.

That is deliberate, and it is the one thing erasure does not delete outright. It keeps the count of
people who have ever used TodoWerk from falling every time somebody exercises this right, which would
turn the statistic into a measure of how many people have not objected. Nothing records that somebody
left, and no screen shows a departure or churn figure.

Your Microsoft To Do tasks themselves are not touched.

**Automatically, after {{DormancyWindowDays}} days.** {{DormancyWindowDays}} days without a sign-in
and everything above is destroyed, by the same mechanism and with the same result. There is nothing
to ask for and nobody to ask. {{DormancyWindowDays}} days is also the longest window on the Tenant
Overview, and both come from the same setting, so the retention rule and what the screen claims
cannot drift apart.

**If your organisation revokes TodoWerk.** An administrator removing TodoWerk's permission in the
Microsoft Entra admin center is invisible to TodoWerk, but nobody can sign in afterwards. Everybody in
that organisation goes dormant and the twelve-month sweep clears the tenant. There is no second
action to take inside TodoWerk, and TodoWerk offers no button that destroys colleagues' data on one
person's authority.

## What TodoWerk asks Microsoft for

One delegated permission, `Tasks.ReadWrite`, requested once when you sign in: read your task titles
to build the index, and write them back when you confirm a change. There is no read-only mode.

An administrator can approve that same permission for a whole organisation, so nobody else is
prompted at sign-in. That changes who is asked and nothing else. TodoWerk takes no application
permission and no directory permission, and holds no credential that could reach a mailbox on its
own. It can only reach people who have signed in, because reaching somebody requires a token issued
to them.

TodoWerk sends nothing about you to anybody except Microsoft Graph, on your behalf, to read and
write your own tasks, and CloudWerk's own licensing portal, described next. There is no analytics
service, no error-reporting service, no advertising, and no third-party script in the browser.

## What TodoWerk sends to CloudWerk's licensing portal

Two things, in the table below. TodoWerk is connected to CloudWerk's ManagementPortal, which holds
the record of every licence. On your first sign-in the portal issues you a personal licence at no
charge, valid for one year from that day, and keeps a record of it, keyed on the two identifiers in
the table, for as long as the licence exists. From then on TodoWerk asks the portal whether the
person in front of it is licensed, one person at a time, because a licence can belong to one person
rather than to a whole organisation.

| What is sent | To whom | Why |
| --- | --- | --- |
| Your organisation's Entra ID tenant id and your Entra ID object id | CloudWerk's ManagementPortal | To answer whether you are licensed, and under which kind of licence |
| A keyed hash of your object id, and the licence it belongs to | CloudWerk's ManagementPortal | So the portal can count how many people are using the product, and see that you are still here |

Both identifiers are the pseudonymous directory identifiers described at the top of this notice.
Neither is a name, an address or a user principal name, and neither is written to TodoWerk's
database as a result of being sent. The answer is held in memory, trusted for a few minutes at a
time, dropped once it is too old to act on, and gone when the process restarts. Asking to be
forgotten removes it immediately, along with the note that your seat was reported today.

The usage figure is deliberately the weaker of the two. What goes out is `HMAC-SHA256` of your object
id under a secret belonging to your licence, never the object id itself. That lets the portal count
distinct people without being able to name one, and the same person under two different licences
cannot be recognised as the same person. It is reported at most once per person per day, on a sign-in
a human performed. A background sync reports nothing, the same rule the record at the top of this
notice follows.

TodoWerk holds no licence, no licence key and no clock of its own. What it holds is the portal's most
recent answer about you, for a few minutes at a time.

## Where things are held

Every Microsoft Graph token stays on the server; the browser holds a session cookie and nothing else.
Refresh tokens are encrypted at rest. Everything else lives in one SQL Server database, held by
CloudWerk.

That holds in Microsoft Teams too. The tab is the same application in a frame. It signs you in with
the identity Teams already knows, the exchange for a Microsoft Graph token happens on the server, and
your browser ends up holding the same session cookie and nothing more. Teams is told nothing about
your hashtags, and nothing about you is stored because you opened the tab that would not have been
stored had you opened TodoWerk in a browser.

## Asking about this

About your data, or about this notice: <privacy@cloudwerk.com>. Security vulnerabilities go
through the [security policy](SECURITY.md) rather than the issue tracker or this address.
