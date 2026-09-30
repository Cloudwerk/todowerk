# Status

Last updated: 2026-09-30.

TodoWerk reads the hashtags out of your Microsoft To Do tasks, shows you what you actually
have, and changes them when you say so. It is in early development, and M0 to M5 are complete.

- ✅ The core: a hashtag inventory, and renames, merges and markers that are previewed, queued
  and undoable for thirty days.
- ✅ The tenant around it: one approval for everybody, a screen showing an organisation its own
  numbers, and erasure on request.
- ✅ Microsoft Teams: walked against a real tenant on Teams desktop, Teams web and a real
  Android device, with a Change previewed, run and undone against a real mailbox through the tab.
- ⚠️ Two cells of that walk could not be performed: Teams on the web in Safari, and the
  first-ever user of a tenant that has not granted Tenant Consent. Read every sentence about
  those two as a claim about code.
- 📦 There is no release and nothing supported to install. CI publishes a container image to
  GitHub's registry on every push to `main`, but it is a build artefact: undocumented, with no
  version number anybody has promised anything about, until M6 packages self-hosting properly.
  Every image is tagged `sha-<commit>` as well as `latest`, and a running deployment names its
  commit at `/version`.

## What works today

Signing in with a work or school account, having your task lists scanned, and working through
the inventory: how often each hashtag is used, across how many lists, when it was last touched,
which ones look like mistakes. Then fixing them: renaming a hashtag, settling one written
several ways onto a single spelling, or folding several into one. Every change is previewed as
the exact titles it would rewrite, runs in the background, and can be undone as a whole for
thirty days. A hashtag can also be given an emoji, a Marker Rule, and applying or removing
those is a change of the same shape ([ADR-0014](adr/0014-marker-rules-are-a-fourth-change.md)).
An administrator can approve TodoWerk once for the whole organisation, anybody in it can see
how much it is used, and anybody can have everything it holds about them destroyed.

780 .NET tests and 243 client tests stand behind it, and the build treats every compiler and
analyzer warning as an error.

### Index and Workbench

- 🔎 The hashtag index. A background scan reads every task list through Graph's delta endpoint,
  Flagged Emails included, and extracts hashtags by the grammar
  [ADR-0005](adr/0005-what-a-hashtag-is.md) settled: `#Work` and `#work` are one Hashtag with
  two Spellings. It runs off a queue in the database and writes progress per list, so a deploy
  mid-scan resumes instead of starting over.
- 🔄 Delta sync on a schedule, plus a re-scan control. Delta tokens are kept per list; when
  Graph expires one, that list is read in full again with nobody asked to intervene.
  Throttling is waited out on Graph's terms up to a bound. A list that fails, fails alone, and
  says why on screen.
- 📋 The Workbench. The inventory table with usage counts and three issue flags: written more
  than one way, similar to another tag, not edited in a long time. Sorting, filtering and
  paging are the database's work. Index freshness and scan progress sit in the chrome, because
  an index TodoWerk maintains itself is sometimes stale
  ([ADR-0003](adr/0003-single-sql-store-own-index.md)). The change queue sits beside the table
  with the running change, per-task progress, a Cancel, and thirty days of history with Undo.

### Changes

- ✏️ Five operations, two shapes. Normalise casing, rename and merge are read off the shape of
  a Change, a set of source Hashtags and one target Spelling
  ([ADR-0006](adr/0006-what-a-change-is.md)). Apply Markers and Remove Markers are stated
  ([ADR-0014](adr/0014-marker-rules-are-a-fourth-change.md)). Remove Markers is the only
  operation that takes text away, and it can only reach emoji TodoWerk itself put at the front
  of a title.
- 👀 The preview is the contract. The dialog shows the exact old-title/new-title pairs and
  names any list left out because it has never been read end to end. A Change is refused, with
  the reason on screen, for a second change while one is in flight, a target the extractor
  would not read back as one hashtag, a plan past the configurable 1,000-task ceiling, or a
  merge nobody confirmed twice.
- 📝 The write path. One PATCH per task, sequential. `todoTask` carries no ETag and
  `Update todoTask` accepts no `If-Match`, so every task is re-read immediately before it is
  written. A task whose hashtag has gone by then is skipped, which makes the previewed count
  advisory, and the UI says so. Only the hashtag's own text is rewritten. Watched against a
  real mailbox: a title-only PATCH leaves due date, note, importance and checklist steps
  untouched, and a task deleted since the plan is skipped while the rest runs. A rewrite that
  would pass 255 characters is refused, because To Do silently stores a truncated title.
- ↩️ Undo. Every task written gets a journal row holding the title read immediately before the
  write, so undo restores what was really there. It runs as a change in the other direction,
  is offered for a whole change one level deep inside thirty days, and leaves alone any task
  somebody edited afterwards: their edit wins.
- 🚦 Two queues that never run at once for one person. Changing and indexing exclude each other
  inside each claim, and a confirmed change preempts a running scan at a page boundary. A scan
  that has stood aside once runs to completion the next time. After a change the index catches
  up through Graph, so the table is briefly behind by one scan, and says so.

### Tenant and privacy

- 🏢 Tenant Consent. An administrator approves, from inside TodoWerk, the delegated grant each
  person is already asked for, and nobody in that tenant is prompted at sign-in again. TodoWerk
  gains no new access: no application permission, no directory permission, no app-only
  credential ([ADR-0008](adr/0008-tenant-consent-is-delegated.md)). It cannot see a grant made
  in the Entra portal, so every surface says the grant was or was not made *through TodoWerk*.
  The callback is bound to a state kept in a protected cookie, any error refuses the record
  (Microsoft's real decline carries `admin_consent=True` alongside `error=consent_required`,
  observed live), and a success must name the tenant the flow started for.
- 📊 The Tenant Overview. How many people have signed in, how many inside three trailing
  windows, when the first started, how many Occurrences they hold and the average per person.
  It never names a person, shows a per-person row, or compares Hashtags across people. Any
  signed-in user of the tenant may see it. Below a configurable floor of five identifiable
  people the statistics are absent, with no placeholder. The floor is measured on every
  request, so a tenant can lose the panel again when erasures take it below five. That is
  deliberate. The consent invitation has no floor.
- 🪪 The Tenant Member record: a tenant, an Entra object id, a first sign-in and a last one. No
  name, no UPN, no address, no user agent. Written at interactive sign-in and nowhere else
  ([ADR-0009](adr/0009-what-todowerk-stores-about-a-person.md)). An architecture test pins the
  columns. M4 added one diagnostic log line that names a browser; the ADR's amendment and the
  privacy notice cover it.
- 🗑️ Erasure. "Delete my data" sits beside "Sign out", confirms first, and destroys the index,
  the per-list state, the Changes with their plans and journals, and the token cache entry. The
  membership row is anonymised in place, so the tenant's cumulative count survives. Twelve
  months without a sign-in does the same down the same code path. Each module purges its own
  rows behind a port, so the module-boundary tests hold without a new exception.

### Teams

- 💬 The Teams Tab, loaded by Teams on desktop, web and a real Android device. The Workbench
  renders in personal scope. The tab asks its host for a token, the backend exchanges it
  on-behalf-of for the same Graph scopes every other path uses, and the browser ends up with
  the ordinary `todowerk.session` cookie, with the same eight hours
  ([ADR-0010](adr/0010-teams-tab-session-and-framing.md)). No token reaches the client on any
  path, as [ADR-0002](adr/0002-backend-held-tokens.md) said from M0.
- 🤝 When the exchange fails. In a tenant without Tenant Consent the exchange cannot succeed;
  that is the expected first run. It comes back as its own problem code, and the tab renders an
  explanatory card with a button, never an automatic popup. The button runs the existing
  server-side OpenID Connect flow in a Teams authentication popup: no MSAL.js, no implicit
  flow. Every other failure carries a different code, so a broken client secret cannot loop
  somebody through consent forever.
- 🍪 The blocked-cookie card. Safari refuses unpartitioned third-party cookies, which is what
  the session is inside the Teams frame. The tab detects the signature (the exchange succeeded
  and the next request came back 401), names the cause, offers to open TodoWerk in a browser,
  and does not retry. The server logs the same condition, with the user agent, capped at sixty
  diagnostics per minute with the sixty-first line saying so. That is the only place TodoWerk
  writes a user agent. Mitigated, not fixed, with the trade-off in view.
- 🎨 Themes. Light, dark and high contrast map onto Fluent's Teams ramps, arrive in the tab's
  URL so the first paint is right, and re-theme without a reload. The browser Workbench follows
  the operating system through `prefers-color-scheme`: tokens only, no toggle, no stored
  preference ([ADR-0004](adr/0004-fluent-ui-v9.md)'s amendment).
- 🪟 Four surfaces that would otherwise navigate to a Microsoft sign-in page, which refuses to be
  framed. Sign-out is absent in the tab. Erasure, starting Tenant Consent and reconnecting
  after a refresh token expires all run through a popup. One "Open TodoWerk in your browser"
  control serves the absent sign-out, the Safari card and the manifest's `websiteUrl`.
- 📦 The App Package. One source manifest with placeholders; a PowerShell 7 script fills in the
  id, the host, the client id and the Application ID URI and produces the zip. The two
  CloudWerk packages carry different manifest ids on purpose, and the repository holds
  placeholders only ([ADR-0011](adr/0011-two-app-packages-and-a-template.md)). Publishing is
  manual: the Teams admin center, or the Store once M6 lists it. The Graph publish script
  beside it has no unattended mode, because publishing to an app catalog is delegated-only.

### Licensing

- 🔑 Licensing, in the Hosted Service and nowhere else. For the signed-in person TodoWerk asks
  an external authority, ManagementPortal, what they hold: a Tenant Licence, a Personal
  Licence, or a Trial whose term the licensing service sets
  ([ADR-0012](adr/0012-three-kinds-of-licence-resolved-per-person.md)). The answer is cached
  per person; a confirmed negative closes the product for that person only. An unreachable
  portal changes nothing for twenty-four hours, then shows a card saying the Licence could not
  be verified, never that it ended. A denied person's queued work stays queued, and a running
  Change is never cut off mid-write. Erasure, the legal pages and `/version` survive a denial.
  A Self-Host leaves the `Licensing` section unset and is never refused; a half-filled section
  fails startup.
- 🏷️ One Licence endpoint, three readers. The Licence Banner speaks only about the reader's own
  Trial and names the date. The Tenant Overview's Licence panel gives the kind and the end date
  and no seat figure. The Tenant Consent invitation shows under a Tenant Licence and during a
  Trial, never under a Personal Licence. In the browser the banner and the ended card carry the
  purchase link when the licensing service delivers one; inside the Teams Tab both carry the
  sentence and no link, on every device.
- 💺 Seat usage, reported on an interactive sign-in and at most once per person per day: the
  licence id and a keyed hash of the object id. It is a figure, never a gate. A background scan
  reports nothing, and a Self-Host reports nothing to nobody. A declined report and a seat
  counted above the limit are warnings that stop nobody.

### Pages and platform

- ⚖️ Terms and privacy notice at `/legal/terms` and `/legal/privacy`, served anonymously. They
  are `TERMS.md` and `PRIVACY.md` from the repository root, compiled in and rendered, so the
  page cannot drift from the file. The Operator is configuration (`Legal:Operator`,
  `Legal:OperatorContact`, `Legal:GoverningLaw`). They are a starting point and have not been
  read by anybody qualified.
- 📖 `/about` and `/administrators`
  ([ADR-0013](adr/0013-the-handbook-is-split-by-kinship.md)). The first says what TodoWerk is
  and who operates this installation. The second is for the administrator deciding whether to
  allow it, and reads the floor, the undo window and the dormancy window from the running
  configuration; an integration test checks that with three non-default values. Both answer
  anonymously and set no cookie, because the App Package names them as `developer.websiteUrl`
  and `publisherDocsUrl`. The header links to the Guide when `Handbook:GuideUrl` names one.
- 🧱 A .NET 10 solution in four layers over a shared kernel, with vertical modules per layer.
  Architecture tests fail the build when a layer or module reaches where it shouldn't, and when
  a checked module holds no types at all.
- 🔐 Sign-in through the authorization-code flow against a confidential client. Tokens and the
  token cache stay on the server; the browser holds a session cookie. One consent prompt asks
  for `Tasks.ReadWrite` and nothing narrower ([ADR-0007](adr/0007-one-consent-grant.md)). The
  suite drives the challenge, the authorization request and the callback through the real
  handler, and proves that a callback without the correlation cookie, or with a state never
  issued, produces no session. A test also pins that ASP.NET Core mints a nonce on this flow
  but compares nothing, so nobody assumes it is load-bearing.
- 💾 A durable token cache: MSAL's cache in the SQL database, encrypted through Data Protection.
  A deploy signs nobody out and background jobs can hold a refresh token. Entries slide out 90
  days after last use; sign-out evicts the account's entry. An integration test proves the
  round trip.
- ⚛️ A React and Fluent UI v9 SPA on the CloudWerk brand theme, with a typed fetch wrapper that
  turns every error into RFC 7807 problem details. The build asserts the browser bundle
  contains no TeamsJS and the consent popup's landing document no framework.
- 🤖 CI on every pull request: build, unit, architecture and integration tests, and the client
  suite. The fake Graph truncates a title at 255 characters as the real service does.
- 🏁 `GET /version` answers `{"version", "commit", "licensing"}` anonymously, the third field
  `absent` or `portal`, so an operator can tell which build and which configuration is running.
- ✍️ A Contributor License Agreement check that works. The branch it records signatures on had
  never been created, so the first human pull request failed a gate that could not have passed.

### Review passes

Every milestone went through a review pass before it was committed, and everything found is
fixed. The finds worth knowing:

| Milestone | Headline find |
|---|---|
| M4 | The tab did not work in the case it was built for. MSAL files an on-behalf-of result in a different cache partition from an authorization-code one, so a consented tenant would have signed in and then failed every Graph call. The suite asserted on the session, not on what the session could do; two tests now do. Seven others, including two "Sign in again" buttons that would have navigated the frame, and framing relaxed on every path under `/teams`. |
| M3 | Any signed-in member could have recorded a Tenant Consent grant nobody made, permanently hiding the invitation. The flow returns no signed response, so the screen now keeps an "approve again" route. Of seven others, two would have left data behind after erasure. A second adversarial pass found the statistics floor measured against the cumulative count, which keeps the forgotten; it now counts only rows that still name somebody. |
| M2 | Eight finds. Two would have wedged somebody: a change runner that left its row claimed for an hour on shutdown, and a preemption check that threw away a list's final page. A ninth turned up while writing this page: both write endpoints returned a `Location` header naming a route that does not exist. |
| M1 | Three reviewers by lens, then two adversarial passes over the fixes. An open redirect in the sign-in return URL, column headers that were never clickable, and an over-long hashtag that could wedge a scan permanently. |

See the changelog for the rest. Applying migrations is an explicit step in every environment;
see [CONTRIBUTING § Development setup](../CONTRIBUTING.md#development-setup).

## What does not work yet

No supported way to run this that is not from source. Nothing in TodoWerk manages another
person's Hashtags, counts Hashtags across people, or lets one person change a colleague's
tasks. That was the old app-only Org Mode, and
[ADR-0008](adr/0008-tenant-consent-is-delegated.md) records why it will not be built. Outlook
and the Microsoft 365 app are hosts since 2026-09-09: a manifest at schema 1.13 or later
offers a personal tab there, so the framing policy and the Entra registration name them and
the walk covers them ([ADR-0010](adr/0010-teams-tab-session-and-framing.md), amended;
[runbooks/teams-tab-walk.md](runbooks/teams-tab-walk.md)).

Two decisions about the Teams tab look like mistakes in the code and are not. All three cookies
are `SameSite=None; Secure` and unpartitioned, and framing is relaxed on the tab's own document
path while every other document keeps `frame-ancestors 'none'` and `X-Frame-Options: DENY`.
The reasons are in [ADR-0010](adr/0010-teams-tab-session-and-framing.md). The first means the
antiforgery double-submit pair is now the defence, not defence in depth.

Two gaps that used to head this list were closed on 2026-08-25 by walking them against a real
tenant, and each walk found a defect no fake could have shown:

- ✅ A tenant has approved TodoWerk for real. The approval is visible in Entra ID as a delegated
  admin-consent grant, and a second user then signed in with no prompt. The find: the real
  decline carries `admin_consent=True` alongside `error=consent_required` and no `tenant`
  parameter, so Cancel was recorded as an approval. Fixed the same day and pinned by tests
  carrying the captured shapes.
- ✅ A write has reached a real mailbox. The find: To Do keeps 255 characters and silently
  stores a truncated title, which cut a Hashtag in half and left a journal row undo could never
  match. Fixed by refusing the write. Throttling stayed unobserved; a seven-task mailbox
  rate-limits nothing.

Three gaps are open and worth naming, because the code looks more finished than it is. A fourth
item is a contradiction this page carried and has now settled.

### ⚠️ Two cells of the Teams tab's walk could not be performed

The walk itself is done (checklist in
[docs/runbooks/teams-tab-walk.md](runbooks/teams-tab-walk.md)). All three themes were
exercised, including a switch with the tab open. The package was uploaded to a real tenant
catalog, and the steps in [teams-app-registration.md](runbooks/teams-app-registration.md) were
performed against a real registration. Fourteen cells passed. One defect was reported,
reproduced and dismissed the same day: the window that looked like an unprompted popup was
Teams' own post-Add confirmation dialog.

What was not walked:

- Teams on the web in Safari. Not verified on macOS or iOS. ADR-0010's account of the refused
  cookie, and the card built for it, are predictions that have never run in the browser they
  exist for.
- A tenant that has not granted Tenant Consent, walked by its first-ever user. No second tenant
  was available. This is the first run for every new tenant, and none of it has been seen
  working.

Everything else on this page about the tab is an observation. Those two are not.

### ⚠️ Live-tenant verification is incomplete

A round trip against a real tenant has been run and produced four defects, all fixed: ids
Microsoft Graph issues in mixed case that the database compared without regard to case, a write
failure reported as a read failure, a connection string the driver could not parse that still
let the application start, and the Flagged Emails read described below. None of them was
reachable from the fake-Graph suite. What the exercise never produced is the other half of its
checklist: paging and throttling against live Graph went unobserved, because the tenant
rate-limited nothing, and the delta endpoint has only ever run against a fake. The scan handles
all three the way the documentation says to; nobody has watched it happen. Finishing the job
needs a mailbox that misbehaves, not a ticket.

### ⚠️ The hashtag grammar is settled by argument, not by observation

ADR-0005 fixes what a Hashtag is, but several of its rules are recorded as uncertain: whether
the To Do clients highlight non-ASCII letters, digit-only tags, or a `#` after a bracket.
`scripts/Probe-HashtagGrammar.ps1` seeds the cases and emits a checklist. Until somebody runs it
against real clients, the extractor is deliberately conservative and may under-recognise.

### ✅ Flagged Emails was the id-collation defect

This page said for a week that the list could not be read, while the Teams tab's walk had
observed the opposite on the same account:

```text
Flagged Emails — 700 tasks, synced 20 minutes ago
720 tasks indexed
```

The answer was size, not kind. Nearly all of the account's tasks are in that list, which makes
it the only one likely to hold a pair of Exchange ids differing in one letter's case. Under the
database's default collation the unique index rejected the second of two different tasks as a
duplicate key, so the list never finished indexing and reported itself as unreadable, at some
six hundred indexed tasks. `20260811064210_GraphIdBinaryCollation` re-collated the id columns
at 06:42 on 2026-08-11; the report that the list could not be read was written that evening,
after the repair. Two tests in `IndexScanTests` now put the list through a first pass and a
delta pass, and the first fails with a duplicate key the moment the old collation is put back.

### Everything else known to be outstanding

Smaller than the above, and written down so that none of it has to be rediscovered:

| Gap | Where it stands |
|---|---|
| Every existing user meets the reconnect path on upgrade | Sign-in now asks for `Tasks.ReadWrite` ([ADR-0007](adr/0007-one-consent-grant.md)), and a cache entry issued under `Tasks.Read` cannot silently widen: the gateway maps MSAL's `MsalUiRequiredException` to reconnect-required and background syncs stop until the user signs in again. In practice that is only whoever signed in before the scope widened. |
| The module boundary is enforced within a layer, not across layers | `ModuleBoundaryTests` does not check `Infrastructure.Changes` against `Domain.Indexing`, and two places rely on that: each queue's claim names the other's entity so that mutual exclusion is one conditional `UPDATE`. Deliberate and commented at both call sites, but nothing stops a third use that is not. |
| The confirm dialog draws 200 pairs, not 1,000 | It renders the first two hundred and counts the rest ("…and 43 more"). The plan is unaffected: the ceiling is still a thousand tasks and all of them are changed. |
| Two Pending rows for one user can still race into running together | Two workers claiming simultaneously can both win, because read-committed does not serialise the `EXISTS` against the other's uncommitted update. The consequence is the cosmetic one ADR-0006 accepts, a half-renamed inventory moving under somebody watching, and it needs both timers to fire inside the same few milliseconds. A lock hint was not worth adding for that. |
| A crash between journalling a write and making it can leave a count one too high | The journal row is saved before the PATCH, deliberately. Crash in that window and the journal claims a write that may not have happened; undo finds no match and leaves the task alone. The other order would lose the record of a write that did happen. The count is still wrong. |
| A rewritten title longer than Microsoft To Do stores is skipped, not written | To Do keeps 255 characters and silently stores the first 252 plus three full stops, answering success either way (observed live). A Change that would produce a longer title skips that task with a reason. Seen firing, live and in tests. The 512-character columns remain the limit for a title TodoWerk did not write. |
| Any write to a task restarts its staleness clock | Staleness reads `lastModifiedDateTime` against `Indexing:StaleAfter`, and Graph renews it on any write, including a Change and the user completing the task. [ADR-0006](adr/0006-what-a-change-is.md) accepted that as wrong in the harmless direction. It becomes unhelpful the day somebody renames in bulk: the whole inventory then reads as current until the window passes. |
| Applying markers does not skip completed tasks | Nor do Rename and Merge. The index does not know completion state, and [ADR-0014](adr/0014-marker-rules-are-a-fourth-change.md) declined to widen it. Somebody who marks a hashtag used for years will find the emoji on tasks finished last spring. |
| A rule is carried by a Rename only when the Rename wrote something | "At least one task written" is the condition ([ADR-0014](adr/0014-marker-rules-are-a-fourth-change.md)), asked of the plan rows at completion, so a resumed run counts every pass. A Rename that wrote a single task moves the rule, which may surprise somebody who cancelled after the first task. |
| Whether a marker is stale is answered from two sources, which agree by construction, not by check | The stale count reads hashtags off Occurrences; a Remove's preview reads them off the stored title through the same grammar. Nothing asserts they match. They could diverge only if the extractor changed between the scan and the plan. Recorded, not fixed. |
| A row kept only to keep a marker known is emptied by a Remove that reached every task carrying it, and not otherwise | A task passed over, or a list never read end to end, leaves the row in place, and the person is offered the removal again. What never settles is a row whose emoji no task carries, a rule deleted before it was applied: it stays until erasure, at a cost of one row per abandoned emoji and nothing at read time. Deleting it would trust the index on the one thing it is not authoritative about. |
| A rule remembers one retired marker, and two edits with a partial run between them can orphan one | Change 🍞 to 🥐, apply, change to ☕, undo the Apply: restored titles carry 🍞, any skipped for being edited since carry 🥐, and the rule can be told only one. The other stays until a Remove Markers takes it. ADR-0014 settled on one retired Marker deliberately. |
| The marker figures are as fresh as the last scan, and one of them reads low until it lands | The index stores the run of emoji each title opens with, and `GET /api/marker-rules` walks those runs in C#: two queries however many rules there are, over a filtered index. A title edited in To Do since the last scan is counted as the scan left it. A migration that adds the column backfills by clearing every delta link; until the next sync lands, the coverage and stale figures read nought. |
| The ceiling for an Apply or a Remove is measured against tasks that could need a write, not against tasks that do | A block cannot be excluded in SQL without the grammar going into the database. So somebody with more than a thousand tasks carrying marked hashtags is refused an all-rules Apply even on the second run, and a Remove likewise. One rule or one emoji at a time is the way through ([ADR-0014](adr/0014-marker-rules-are-a-fourth-change.md)), but the refusal will read as arbitrary. |
| The change history is capped at 50 | `ChangeStore` hands the Workbench the 50 most recent Changes. Somebody who makes a great many would find the oldest undoable in principle and invisible in practice. |
| No test watches the application refuse to start | Under `WebApplicationFactory`, startup validation races the host's disposal, so a test that boots the app and waits for the throw is flaky (green about four runs in five). Tests now ask the startup validator directly, and `TestConventionTests` fails the build on the next test that asserts a boot throws. Not watched by anything: the host actually stopping, and a `Program`-level `PostConfigure` that quietly repaired a bad setting. Reshaping `Program` to suit a test was weighed and declined. |
| Only some of the client is tested | The suite covers the state the client holds before the server confirms it. The grid has no test of its own, and neither has the Teams tab's shell; the decision *which* card it shows, made in `session.ts`, is tested. That is the intended shape ([CONTRIBUTING § Testing](../CONTRIBUTING.md#testing)), but a rendering regression in those parts would reach a person before it reached a test. |
| Nothing but branch protection stops a commit skipping CI | The first live-tenant fixes, all of M2 and all of M3 went straight to `main`, and validation first ran on them in the first pull request after M0, which passed on Linux in Release against a SQL Server container. Until a branch protection rule makes the pull request compulsory, the gate is a habit. |
| Startup checks the connection string, not the database | An unparseable connection string fails the boot. A well-formed one pointing at a database that is down does not: `DatabaseReadinessCheck` logs it and the app serves. Deliberate. |
| The first scan after upgrading past the id-collation migration re-reads everything | `GraphIdBinaryCollation` clears every delta link on purpose, because rows a case collision swallowed were never written. Expect one full pass per list, and freshness to read as unknown until it finishes. |
| Erasure is not exclusive with a scan or a Change | A scan running during erasure stops when the row it claimed disappears, and the purge repeats while it keeps finding rows. What is left is a window of milliseconds in which a page could land after the last pass. |
| The statistics begin the day the migration is applied | Nothing recorded a sign-in before M3. On a tenant already using TodoWerk, the first sign-in date is the upgrade date and every count starts from zero. Deliberate: a backfill would have to invent the moments. |
| A sign-in whose membership write fails is not counted | Logged as an error and allowed to proceed. It costs one uncounted visit and a retention clock that did not move, and nothing but the log says so. |
| TodoWerk cannot see consent granted or revoked outside itself | Reading the grants from Graph costs a directory permission, which [ADR-0008](adr/0008-tenant-consent-is-delegated.md) declined. An administrator who approved in the Entra portal keeps seeing the invitation, and a revocation is handled by everybody going dormant. |
| A recorded consent grant is TodoWerk's own note, not proof | The admin-consent flow returns no signed response, so a signed-in member can start the flow, reach the callback, and record a grant nobody made. Not closable without the permission ADR-0008 declined. The record only hides the invitation, and the screen keeps an "approve again" route. |
| A guest's token-cache entry survives their erasure | The purge evicts by directory identifiers (`oid.tid`); MSAL keys by home ones (`uid.utid`), and for a guest the two differ. What survives is unusable and lapses within the 90-day sliding expiry. Guests are unsupported until tried against a real tenant, and the fix means storing home identifiers, which [ADR-0009](adr/0009-what-todowerk-stores-about-a-person.md) forbids. |
| A person who is erased and later returns is counted twice | Inherent to anonymisation ([ADR-0009](adr/0009-what-todowerk-stores-about-a-person.md)). The cumulative count records arrivals, not distinct persons. The floor and the average are unaffected. |
| The dormancy sweep is its own worker, not a job on an existing one | The existing workers live inside Indexing and Changes and cannot reach Onboarding's eraser without breaking the module boundary, so the sweep has its own worker on an hourly tick. Recorded because the specification said otherwise. |
| An erasure that could not finish shows the reader problem-JSON | The control is a form POST, because success is a redirect to the identity provider's end-session endpoint. Failure is therefore a problem-details document rendered as text. The `detail` is written for a person, but it is a JSON page after a sensitive click. The fix is the SPA driving erasure itself. |
| The Teams tab's bootstrap is reachable by no test | `app.initialize()`, `getAuthToken()` and the on-behalf-of round trip run in one module that imports TeamsJS. The decision it makes given three responses is tested; the handshake is not. The walk exercised it on Teams desktop, web and mobile and it worked on all three, so the gap is in the suite: a regression will be found by a person, not by CI. |
| Teams on the web in Safari is expected not to hold a session, and the mitigation has never run | Safari blocks unpartitioned third-party cookies, which is what `todowerk.session` is inside the Teams frame ([ADR-0010](adr/0010-teams-tab-session-and-framing.md)). Closing it would mean the bearer-token design ADR-0010 declined. Teams on a real Android device held the session and no card fired. Safari has **not been verified on macOS or iOS**, so every sentence about its behaviour, and the card built for it, is a prediction. A hosted browser session would settle it in one sitting. The log line is diagnostic evidence, not proof: the header is asserted by the client, grants nothing, and is capped at sixty diagnostics a minute. |
| The first run of a tenant that has not granted Tenant Consent has never been seen | The card, its button and the popup are the expected first run of every new tenant, and no second tenant was available. The decision logic is tested (`session.test.ts` pins five outcomes) and the popup path is the one erasure and reconnect use, both walked. A free Microsoft 365 developer tenant would close it. The browser's half has been walked, and found the sign-in callback answering HTTP 500 on a decline; that is fixed and `SignInFailureTests` pins each failure. |
| The `glass` theme is untestable here | Teams reports it on Apple Vision Pro. It falls back to the light Teams theme and a test pins the fallback, but that is a claim about code. |
| A Teams sign-in's tokens live in a different partition of the cache from a browser sign-in's | A shape worth knowing before somebody simplifies it away. The tab's tokens are filed under a session key TodoWerk chooses (`TeamsSsoDefaults.SessionKeyFor`), and the Graph gateway looks in the account partition first and that one second. The cost is one thrown-and-caught exception on the first Graph call of every Teams request. Removing either half breaks one of the two sign-ins. |
| `validDomains` names TodoWerk's host and not the identity provider | The original plan listed both. Microsoft's schema says not to list identity providers, and the Teams Store validation guidelines disallow `*.microsoftonline.com` outright. Recorded because the plan said otherwise. |
| The Guide, the Handbook's half for the person using the product, is not part of this repository | `/about` and `/administrators` are built and named by the manifest; the Guide is published separately ([ADR-0013](adr/0013-the-handbook-is-split-by-kinship.md)). With `Handbook:GuideUrl` unset there is no Guide entry in the help menu. The screenshot seeder (`scripts/Seed-HandbookScreenshots.ps1`) is here; the pictures are not. M7 adds to what the Guide must say: five Changes, and five promises about Marker Rules. Rules never write on their own, applying adds and reorders but never removes, a stale marker stays until you remove it, removing takes only emoji TodoWerk put there, and a rename carries the rule. |
| Licensing has been walked against a local licensing service only | The resolver, portal client and seat reporter have been walked over the network against a licensing service built from source and run locally, not a deployed one. |
| A Licence answer is cached in memory, so a restart costs one resolution per person | Deliberate: a table of who is licensed would be the second Licence authority [ADR-0001](adr/0001-single-entitlement-authority.md) exists to prevent. The cost is one portal call per active person after every deploy, and one extra usage report, which the portal treats as a refreshed last-seen timestamp. |
| TodoWerk cannot tell a declined sign-in from one that needed an administrator | Both come back as `access_denied`, and the AADSTS code depends on the tenant's consent policy. The card names both readings and offers a route for each. The log carries the code Entra ID sent, so real occurrences can sharpen this later. |
| An approval started from the refused-sign-in card is reported and never recorded | That flow starts with nobody signed in, so its state binds to no tenant, and the `tenant` parameter Microsoft returns is forgeable. Recording it would let anybody switch off any organisation's invitation, which is worse than what [ADR-0008](adr/0008-tenant-consent-is-delegated.md) accepts. The grant is recorded when somebody approves from inside the product. |
| A denied person's queued scan is claimed and handed back on every tick | The gate is asked after the claim, so a denied person with queued work costs a claim and a release every fifteen seconds (three for a Change). Bounded by the number of denied people with pending work. Excluding them in the `WHERE` would put the licence answer in SQL, which must not happen. |
| A Change running while its owner's Licence lapses is watched as a row, not as a write in flight | `WorkerLicensingTests` pins that a denied person's queued work survives a tick untouched while a colleague's runs, and that a claimed Change keeps its lease. No test arranges a runner PATCHing while the portal turns its owner away. "Never cut off mid-write" is a fact about there being no second check. |
| One person's memoised near-duplicate set outlives their erasure on other instances | The inventory memoises Hashtag names in process for a minute; erasure evicts it on the instance that ran it. On one instance, which is every deployment today, that is all of it. Scaling out is the same `DisableL1Cache` conversation as the token cache's L1. |

## Roadmap

Work is cut into vertical slices. Each milestone leaves something you can demonstrate.

| Milestone | Outcome | State |
|---|---|---|
| M0 — Walking skeleton | Solution scaffold, sign-in, one real Graph read, CI | ✅ Complete |
| M1 — Index & inventory | Hashtag index with delta sync; inventory table with issue flags | ✅ Complete, closed 2026-08-11. Live paging and throttling are still unwatched |
| M2 — Rename & merge | Job queue, dry-run preview, journaled undo | ✅ Complete, closed 2026-08-12. Write path verified against a real mailbox 2026-08-25, which found and fixed a silent title truncation |
| M3 — Tenant Consent & Tenant Overview | Delegated admin consent, tenant statistics, retention and erasure | ✅ Complete, closed 2026-08-12. Verified against a real tenant 2026-08-25, which found and fixed a decline recorded as an approval |
| M4 — Teams tab | Teams SSO, app package | ✅ Complete, built 2026-08-26, closed 2026-09-01. Fourteen walk cells passed and the one defect reported was dismissed the same day: the first walk in this project to find nothing real. The App Package passes the Teams Store validation tool with zero errors and zero warnings. ⚠️ Two cells not walked: Safari, and the first user of an un-consented tenant |
| M5 — Licensing & polish | Licence resolution over the First-Party Path, Licence Banner, Tenant Overview Licence panel, browser dark mode | ✅ Complete, shaped 2026-09-02 ([ADR-0012](adr/0012-three-kinds-of-licence-resolved-per-person.md)), built 2026-09-03. Walked against a local licensing service: six cells observed, one finding, fixed. The notification email is dropped (it needed an address on disk that ADR-0009 forbids); the tips card is deferred with no plan attached |
| M6 — Self-Host & listing | Compose packaging, setup docs, marketplace listing | 🗓️ Planned. The application's half of the Handbook landed 2026-09-03 ([ADR-0013](adr/0013-the-handbook-is-split-by-kinship.md)); the Guide and the Microsoft Store listing are prepared outside this repository |

Self-hosting becomes practical at M6. Until then the supported way to run TodoWerk is from
source, as described in the [README](../README.md). Running the CI container on your own server
is possible and written down in
[runbooks/deploying-a-self-host.md](runbooks/deploying-a-self-host.md), but nothing about it is
a promise yet.

## Decisions already settled

Fourteen architecture decisions are recorded and are not up for casual revision. ADR-0002 gained
an amendment in M4: the Teams tab put a bearer token on exactly one endpoint.

- [ADR-0001](adr/0001-single-entitlement-authority.md): a Licence is resolved per person from
  one external authority; TodoWerk holds no Licence record, and a Self-Host is never refused.
- [ADR-0002](adr/0002-backend-held-tokens.md): every Graph token stays on the server; the
  browser gets a session cookie.
- [ADR-0003](adr/0003-single-sql-store-own-index.md): one SQL database holds the index, the
  sync state and the job queue.
- [ADR-0004](adr/0004-fluent-ui-v9.md): Fluent UI v9 with the CloudWerk brand theme.
- [ADR-0005](adr/0005-what-a-hashtag-is.md): casing does not distinguish one Hashtag from
  another; the folded key is computed in C# and compared as bytes.
- [ADR-0006](adr/0006-what-a-change-is.md): a Change is a set of source Hashtags and one target
  Spelling; each task is re-read before it is written, and a journal makes the Change undoable.
- [ADR-0007](adr/0007-one-consent-grant.md): one consent prompt; sign-in asks for
  `Tasks.ReadWrite` and nothing narrower.
- [ADR-0008](adr/0008-tenant-consent-is-delegated.md): Tenant Consent is admin consent to that
  same delegated grant; no application permission, no directory permission, no app-only
  credential.
- [ADR-0009](adr/0009-what-todowerk-stores-about-a-person.md): a tenant id, a pseudonymous
  object id and two timestamps; twelve months of dormancy; anonymisation, not deletion.
- [ADR-0010](adr/0010-teams-tab-session-and-framing.md): the Teams Tab keeps the cookie session,
  so all three cookies are `SameSite=None; Secure` and unpartitioned; framing is relaxed on the
  tab's own document path and nowhere else.
- [ADR-0011](adr/0011-two-app-packages-and-a-template.md): App Packages are built per deployment
  from one template; no deployment's values are committed.
- [ADR-0012](adr/0012-three-kinds-of-licence-resolved-per-person.md): a Tenant Licence, a
  Personal Licence and a Trial with no trial clock in TodoWerk; resolved and denied per person.
- [ADR-0013](adr/0013-the-handbook-is-split-by-kinship.md): the About Page and the
  Administrator's Guide live with the application; the Guide is published separately.
- [ADR-0014](adr/0014-marker-rules-are-a-fourth-change.md): Marker Rules are applied as a
  fourth Change and removed as a fifth, previewed, queued and undoable like the other three.

New decisions are written up as they are made, and pull requests that contradict one should
expect to argue with it.
