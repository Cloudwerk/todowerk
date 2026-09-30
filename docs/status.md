# Status

Last updated: 2026-09-30.

TodoWerk 1.0.2 is the first release. As of M2 it does both halves of the thing it is for:
it reads the hashtags out of your Microsoft To Do tasks and shows you what you actually
have, and it changes them when you say so. M3 adds the tenant around that: one approval
for everybody, a screen showing an organisation its own numbers, and the first route out.
M4 is complete: it puts the whole thing inside Microsoft Teams, walked against a real tenant on
Teams on the desktop, Teams on the web, and Teams on a real Android device, with a Change
previewed, run and undone against a real mailbox through the tab. Two cells of that walk could
not be performed and are recorded below as gaps: Teams on the web in Safari, and the first-ever
user of a tenant that has not granted Tenant Consent. Read every sentence about those two as a
claim about code.
The first release is two things: CloudWerk's Hosted Service, listed in the Microsoft Teams
Store, and this repository, now public. Self-hosting is available as is: it works and is
documented, but CloudWerk makes no support commitment for it. CI publishes a container image
to GitHub's registry on every push to `main`. Images carry no version tag yet: each one is
tagged `sha-<commit>` as well as `latest`, and a running deployment names the commit it was
built from at `/version`, so pin a deployment to a `sha-` tag.

## What works today

Signing in with a work or school account, having your task lists scanned, working through
the hashtag inventory — how often each one is used, across how many lists, when it was last
touched, which ones look like mistakes — and then fixing them: renaming a hashtag, settling
one written several ways onto a single spelling, or folding several into one. Every change
is previewed as the exact titles it would rewrite, runs in the background against Microsoft
To Do, and can be undone as a whole for thirty days. A hashtag can also be given an emoji —
a Marker Rule — and applying those rules is a fourth change of exactly the same shape,
previewed, queued and undoable like the other three, with a fifth that takes stale markers back
off again ([ADR-0014](adr/0014-marker-rules-are-a-fourth-change.md)).
An administrator can approve TodoWerk once for the whole organisation, anybody in it can see
how much it is used, and anybody can have everything it holds about them destroyed.

Standing behind it:

- The hashtag index. A background scan reads every task list through Graph's delta
  endpoint, Flagged Emails included: a walk against a real tenant indexed some seven hundred
  tasks from it (see below). It extracts the hashtags from task titles, following the grammar
  [ADR-0005](adr/0005-what-a-hashtag-is.md) settled: `#Work` and `#work` are one
  Hashtag with two Spellings, not two rows. The first pass over a busy account takes
  minutes, so it runs off a queue in the database rather than inside a request, and
  it writes its progress per list as it goes: a deploy mid-scan resumes rather than
  starting over.
- Delta sync on a schedule, plus a re-scan control. Graph's delta tokens are kept per
  list and applied incrementally; when Graph expires one, that list is read in full
  again with nobody asked to intervene. Throttling is waited out on Graph's terms up
  to a bound. A list that fails, fails alone, and says why on screen.
- The Workbench: the inventory table, with usage counts and three issue flags —
  written more than one way, similar to another tag, not edited in a long time. Sorting,
  filtering and paging are the database's work, not the browser's. Index freshness and
  scan progress sit in the chrome, because an index TodoWerk maintains itself is
  sometimes stale ([ADR-0003](adr/0003-single-sql-store-own-index.md)) and hiding that
  would let somebody act on numbers they have no reason to trust. Rows are multi-selectable,
  because a merge takes several sources; the change queue sits alongside the table with the
  running change, its per-task progress, a Cancel, and thirty days of history with Undo.
- Changes, which is the half M2 added and M7 widened. A Change is one of two shapes: a set of
  source Hashtags and one target Spelling, or a set of Markers and the tasks to write them into or
  take them out of. Which of the first three operations it performs — normalise casing, rename,
  merge — is read off its shape rather than chosen ([ADR-0006](adr/0006-what-a-change-is.md)); the
  last two, Apply Markers and Remove Markers, are stated rather than derived, because they carry no
  target for anything to be read from ([ADR-0014](adr/0014-marker-rules-are-a-fourth-change.md)).
  Remove Markers is the only operation in the product that takes text away, and it can only reach
  emoji TodoWerk itself put at the front of a title.
  Confirming one is confirming a specific set of tasks: the dialog shows the exact
  old-title/new-title pairs and names any list left out because it has never been read end to
  end. It is refused, with the reason on screen, for a second change while one is in flight, a
  target the extractor would not read back as one hashtag, a plan past the configurable
  1,000-task ceiling, or a merge nobody confirmed twice.
- The write path. One PATCH per task, sequential, through the same gateway the reads go
  through. Every task is re-read immediately before it is written and the rewrite applied to
  what came back, because `todoTask` carries no ETag and `Update todoTask` accepts no
  `If-Match`, so there is no compare-and-swap at all. A task whose hashtag has
  gone by then is skipped rather than failed, and the count the preview showed is therefore
  advisory, which the UI says. Only the hashtag's own text is rewritten;
  spacing, punctuation and the rest of the sentence are left exactly as they were. Watched
  against a real mailbox since M3: a title-only PATCH is accepted
  and leaves a task's due date, note, importance and checklist steps untouched, and a task
  deleted since the plan is skipped while the rest of the Change runs. A rewrite that would
  pass 255 characters is refused rather than sent, because To Do stores a truncated title and
  says nothing — see the table below.
- Undo, and the journal that makes it possible. Every task written gets a row holding the
  title read immediately before the write, so undo restores what was really there rather than
  what the preview showed. It runs as a change in the other direction — queued, exclusive,
  journaled like any other — is offered for a whole change one level deep inside thirty days,
  and leaves alone any task somebody edited after TodoWerk changed it: their edit wins.
- Two queues that never run at once for one person. Changing and indexing are mutually
  exclusive per user, enforced inside each claim rather than in a method that can be bypassed,
  and a confirmed change preempts a running scan at a page boundary so that "starts
  immediately" is true even mid-scan. A scan that has stood aside once runs to completion the
  next time, so a run of changes cannot starve somebody's index. After a change finishes, the
  index catches up through Graph rather than through index writes from the Changes module —
  so the table is briefly behind by one scan, and says so.
- Tenant Consent, which is the half M3 added. An administrator reaches Microsoft's
  admin-consent endpoint from inside TodoWerk, approves the delegated grant TodoWerk already
  asks each person for, and from then on nobody in that tenant is prompted at sign-in.
  TodoWerk gains no new access whatsoever: no application permission, no directory
  permission, no app-only credential, and it still reaches no mailbox whose owner has not
  signed in ([ADR-0008](adr/0008-tenant-consent-is-delegated.md)). It cannot see a grant made
  in the Entra portal, so every surface says the grant was or was not made *through TodoWerk*
  rather than claiming to know. The callback is not believed on the strength of its own
  parameters — Microsoft's documentation says the tenant it names can be forged — so the flow
  carries a state TodoWerk remembers in a protected cookie and records nothing without it.
  Nor is `admin_consent=True` read as approval on its own: Microsoft's real decline carries it
  alongside `error=consent_required` (observed live), so any error
  refuses the record whatever else the redirect claims, and a success must name the tenant the
  flow started for — the real success does.
- The Tenant Overview: a screen of its own showing a tenant how many of its people have signed
  in, how many did so inside three trailing windows, when the first of them started, how many
  Occurrences they hold between them and the average per person. Counts computed from the
  database at request time — it never names a person, never shows a per-person row, and never
  lists or compares Hashtags across people. Any signed-in user of the tenant may see it; there
  is no role check and no administrator concept. Below a configurable floor of five identifiable
  people the statistics are absent from the response and from the screen, with no placeholder,
  because a total across three people is still those three people's data. Somebody who has been
  forgotten counts toward the total shown but never toward the floor, because the figures
  describe only the people still on record. The floor is measured on every request, so a tenant
  that had the statistics loses them again when erasures take it back below five. That is
  deliberate, and a panel that vanishes this way is not a defect. The consent invitation has no floor and renders anyway,
  which is how the feature gets found in the tenants most likely to need it.
- The Tenant Member record, and the fact that it is the smallest thing that could answer the
  question: a tenant, an Entra object id, a first sign-in and a last one. No name, no UPN, no
  address, no user agent, and no history between the two moments. Written at interactive
  sign-in and nowhere else — a scan running on a timer is not somebody using the product, and
  that is enforced by where the write lives rather than by a flag
  ([ADR-0009](adr/0009-what-todowerk-stores-about-a-person.md)). An architecture test pins the
  columns, so a fourth one cannot arrive quietly and make the privacy notice wrong. It guards the
  record and nothing else: M4 added one diagnostic log line that names a browser, which is not a
  column and which no test of the schema could have caught — hence the ADR's amendment and the
  privacy notice's own section on what reaches a server log.
- Erasure, and the retention rule behind it. "Delete my data" sits in the menu under the person's
  name, beside "Sign out", confirms first, and destroys the index, the per-list state, the Changes with their plans and journals,
  and the token cache entry — then anonymises the membership row in place, so the tenant's
  cumulative count survives somebody exercising a right with nobody identifiable left in it.
  Twelve months without a sign-in does the same thing down the same code path, on a worker,
  which is also what an administrator revoking TodoWerk in Entra amounts to. Each module purges
  its own rows behind a port in the shared application layer, so Onboarding names no Indexing
  or Changes type and the module-boundary tests hold without a new exception.
- The Teams Tab, which is what M4 built and what Teams has now loaded — desktop, web and a real
  Android device. The
  Workbench renders inside Teams in personal scope, because one person's Hashtags is what the
  product is and there is nothing in it to show a team. Signing in is the Teams identity: the tab
  asks its host for a token, the backend exchanges that on-behalf-of for the same Graph scopes
  every other path uses, and what the browser holds afterwards is the ordinary `todowerk.session`
  cookie — the same cookie, the same scheme, the same eight hours, so no endpoint has learned a
  second way of knowing who is calling ([ADR-0010](adr/0010-teams-tab-session-and-framing.md)).
  No token reaches the client on any path, which is what [ADR-0002](adr/0002-backend-held-tokens.md)
  said the Teams surface would do from M0.
- What the tab does when the exchange fails, which is the half that decides how M4 feels. The
  Teams token carries the OpenID scopes and never `Tasks.ReadWrite`, so in a tenant that has not
  granted Tenant Consent the exchange cannot succeed — that is the expected first run rather than
  an error, and it comes back as a problem code of its own that the client switches on. It renders
  an explanatory card with a button, never an automatic popup, which a pop-up blocker would eat.
  Pressing it runs TodoWerk's existing server-side OpenID Connect flow in a Teams authentication
  popup: no MSAL.js, no implicit flow, no redirect URI type that did not already exist, and nothing
  passed back through `notifySuccess` because the cookie is already set. Every other failure carries
  a different code, deliberately — a tenant that met a broken client secret and was offered a consent
  popup would loop through it forever.
- The blocked-cookie card, and the one signature that produces it. Safari refuses unpartitioned
  third-party cookies outright, and inside the Teams frame that is exactly what TodoWerk's session
  is. The tab detects the pair that means it (the exchange succeeded, and the request immediately
  after it came back 401), names the cause in a sentence, offers to open TodoWerk in a browser, and
  does not retry, because retrying produces the same answer every time. This mitigates the problem
  without fixing it, and was decided that way with the trade-off in view. The same condition
  reaches the server log as well as the screen. Only the tab sees both halves of the pair, so the
  confirming request wears a header saying what it is, and a 401 answered to a request wearing it
  is logged as the browser refusing the cookie rather than as somebody arriving signed out. The
  header decides a log line and no access. No more than sixty of those diagnostics are written per
  minute, and the sixty-first line says so rather than going quiet: an anonymous caller can
  produce a 401 at will, and the rate limiter never sees one, because authorization
  short-circuits an unauthenticated request before it runs. That line carries the user agent,
  because which clients refuse the cookie is exactly the open question. It is the only place
  TodoWerk writes one, and it is a request-scoped diagnostic, not part of what TodoWerk stores
  about a person ([ADR-0009](adr/0009-what-todowerk-stores-about-a-person.md)).
- The tab's theme, which is the Teams theme rather than CloudWerk's. Light, dark and high contrast
  map onto Fluent's own Teams ramps, arrive in the tab's URL through the manifest's placeholders so
  the first paint is already right, and re-theme without a reload when somebody changes them. The
  browser Workbench now follows the operating system into the dark through `prefers-color-scheme`
  and nothing else — tokens only, no toggle and no stored preference
  ([ADR-0004](adr/0004-fluent-ui-v9.md)'s amendment).
- The four surfaces in the tab that would otherwise navigate to a Microsoft sign-in page, which
  refuses to be framed. Sign-out is absent rather than disabled — the identity is the Teams
  identity, and signing out of TodoWerk while staying signed into Teams is a state the next tab load
  silently undoes. Erasure is offered, because it is an obligation rather than a convenience: the
  destruction happens on a request from the tab and the Microsoft session ends in a popup. Starting
  Tenant Consent and reconnecting after a refresh token expires run through the same popup. One
  "Open TodoWerk in your browser" control serves the absent sign-out, the Safari card and the app
  manifest's `websiteUrl` alike.
- Licensing, in the Hosted Service and nowhere else. For the signed-in person — tenant id and
  Entra object id together — TodoWerk asks an external licensing authority, ManagementPortal, what
  they hold: a Tenant Licence covering everybody in the organisation, a Personal Licence covering
  them alone, or a Trial, whose term the licensing service sets and TodoWerk keeps no clock for
  ([ADR-0012](adr/0012-three-kinds-of-licence-resolved-per-person.md)). The answer is cached per
  person for the portal's own recheck interval, and a confirmed negative closes the product for
  that person while their colleagues carry on. An unreachable portal changes nothing a user can see
  for twenty-four hours; past that they meet a card that says the Licence could not be verified —
  never that it ended — which clears itself on the next successful resolution with nobody pressing
  anything. A queued scan or Change for a denied person is left where it is rather than run, and a
  Change already running is never cut off mid-write. Erasure, the legal pages and `/version` survive
  a denial. A Self-Host leaves the `Licensing` section unset, registers no client to the portal, and
  is never refused; a half-filled section fails startup.
- One Licence endpoint the client reads once a session, and the three surfaces that read it: the
  Licence Banner, which speaks only about the reader's own Trial and names the date rather than
  counting days; the Tenant Overview's Licence panel, which gives the kind and the end date and no
  seat figure at all; and the Tenant Consent invitation, which now shows under a Tenant Licence and
  during a Trial and never under a Personal Licence. In the browser the banner carries the purchase
  link the licensing service delivers, when it delivers one, and so does the ended card, so the
  person being told their Trial has ended is offered somewhere to buy; inside the Teams Tab both
  carry the sentence and no link, on every device, and the denied card names no contact — the
  tab-versus-browser rule the client already had, and deliberately not a device check. No address
  is the ordinary state rather than a fault, and no link is then shown at all.
- Seat usage, reported on an interactive sign-in and at most once per person per day. What goes out
  is the licence id and a keyed hash of the object id; the object id itself never leaves the
  server, and TodoWerk holds no licence key to send. A figure and a "this person is back" signal, never a gate — a Tenant Licence is unlimited and a
  Personal Licence is one person. A background scan reports nothing, and a Self-Host reports
  nothing at all. A report the portal declines to store is a warning naming the portal's own word for
  why, and a seat counted above the licence's seat limit is a warning of its own, carrying both
  figures and stopping nobody.
- The App Package, and the script that builds it. One source manifest with placeholders; a
  PowerShell 7 script fills in the id, the host, the client id and the Application ID URI and
  produces the zip with its icons, so the Store package, the ManagementPortal package and a
  Self-Host's own cannot drift in anything else. The two CloudWerk ones carry different manifest
  ids on purpose, and the template in this repository holds placeholders rather than CloudWerk's
  values ([ADR-0011](adr/0011-two-app-packages-and-a-template.md)). What an administrator is told
  to do is manual — the Teams admin center, or the Store once M6 lists it; the Graph publish script
  beside it is a convenience with no module to install and no unattended mode, because publishing
  to an app catalog is a delegated-only permission.
- The terms of use and the privacy notice, served by the application itself at `/legal/terms` and
  `/legal/privacy` — anonymously, because the people who read them are deciding whether to sign in
  and the Microsoft reviewer who reads them never will. They are `TERMS.md` and `PRIVACY.md` from
  the repository root rather than a copy of them: both are compiled into the application and
  rendered from their markdown, so the page a consent dialog links to cannot drift from the file
  in this repository. Who the terms name as the Operator is configuration (`Legal:Operator`,
  `Legal:OperatorContact`, `Legal:GoverningLaw`), because the same documents are served by
  CloudWerk's deployment and by every Self-Host and the one thing that must differ between them is
  who the person signing in is agreeing with. A deployment that set none of them still serves
  documents that read as English, and says so once at startup. They are a starting point drafted
  for the software rather than legal advice, and have not been read by anybody qualified.
- Two more pages on the same renderer, for the two readers who are not contributors
  ([ADR-0013](adr/0013-the-handbook-is-split-by-kinship.md)). `/about` says what TodoWerk is, who
  operates this installation and where help is, and links onward — imprint, terms of business, a
  way to order, the Landing Page — only where the `Handbook` configuration names a target, so a
  Self-Host that names nothing serves a page that is still true. `/administrators` is written for
  the administrator deciding whether to allow TodoWerk: what consent asks for and what Tenant
  Consent does not grant, what is stored about a person, the floor, erasure and dormancy, how to
  revoke, how to roll out. It reads the floor, the undo window and the dormancy window from the
  running configuration, and an integration test starts a deployment with three non-default values
  and checks the page says them. Both answer anonymously and set no cookie, because the App Package
  names them — `developer.websiteUrl` and `publisherDocsUrl` — and the Teams admin center shows
  the first as the app's support link. The consent invitation on the Tenant Overview links to the
  second, and the header links to the Guide when `Handbook:GuideUrl` says there is one.
- A .NET 10 solution in four layers over a shared kernel, with vertical modules
  repeated per layer. Architecture tests fail the build when a layer or a module
  reaches somewhere it shouldn't, so the structure cannot quietly erode. Since M3 they also
  fail when a module named in that check holds no types at all, because a boundary check over
  an empty namespace passes for the wrong reason.
- Sign-in through the authorization-code flow against a confidential client. Graph
  tokens and the token cache stay on the server; the browser holds a session cookie
  and nothing else. One consent prompt, asking for `Tasks.ReadWrite` and nothing narrower
  ([ADR-0007](adr/0007-one-consent-grant.md)) — including for a user who only ever looks,
  because the worker that performs a write has no browser to prompt through.
- A durable token cache: MSAL's cache lives in the SQL database, encrypted at rest
  through Data Protection, so a deploy does not sign everyone out and the
  background jobs [ADR-0002](adr/0002-backend-held-tokens.md) exists for can hold a
  refresh token with no browser attached. Entries slide out 90 days after last use;
  sign-out evicts the account's entry. An integration test proves the round trip —
  seeded token, host disposed, second host redeeming it for a Graph read.
- A React and Fluent UI v9 SPA on the CloudWerk brand theme, talking to the app's
  own API through a typed fetch wrapper that turns every error into RFC 7807
  problem details.
- Sign-in, exercised end to end by the suite since M3. A test drives the challenge, the
  authorization request and the callback through the real handler — carrying the correlation
  cookie and an id token signed by a key the host is told to trust — and ends holding a session
  the application accepts on a later request. Two more prove that a callback with no correlation
  cookie, and one carrying a state this application never issued, produce no session. Everything
  M3 does happens *because* somebody signed in, and until this existed the suite minted cookies
  and could not express that.

  Walking it also settled what the nonce is worth here: on pure authorization-code flow
  ASP.NET Core mints a nonce, sends it and reads its cookie back, but compares nothing. A callback
  whose token repeats another flow's nonce, or carries none at all, still authenticates; both
  cases were measured, not reasoned about. TodoWerk did not open this gap, and it is not worth
  hand-rolling protocol validation over: the identity comes from a single-use code redeemed
  server-side with the client secret, under PKCE, tied to the correlation cookie, and the nonce
  matters in the hybrid and implicit flows TodoWerk does not use. A test pins the weaker behaviour
  so that nobody reads the nonce in the traffic and assumes it protects anything, and it fails the
  day a framework upgrade starts enforcing it.
- Continuous integration on every pull request: build, unit tests, architecture
  tests, integration tests that boot the real application, and, since M2, the client's
  own suite over the state it holds before the server has confirmed it. `main` is protected: a
  pull request needs the `build-and-test` and `cla` checks.
- A deployment that can say which build it is. CI tags every image `sha-<commit>` as well as
  `latest`, and `GET /version` answers `{"version", "commit", "licensing"}` — the first two read
  from the informational version the compiler stamps on the assembly, the third `absent` or
  `portal` for whether the `Licensing` section is configured — anonymous, like the legal pages,
  because everybody who needs it arrives without a session. It exists so an operator can see which
  build is running: when "the same build as yesterday" and "a different build" look identical from
  outside, an unchanged deployment cannot be told apart from a fixed one. The third field does the
  same for configuration, because one commit admits everybody with the section absent and can deny
  with it configured, and nothing else visible from outside tells the two apart.
- A Contributor License Agreement check. It records signatures on the `cla-signatures` branch,
  and creates that branch and its signature store itself when they are missing, so it does not
  depend on anybody having set them up by hand. Dependabot's pull requests are on its allowlist
  and skip it.

780 .NET tests and 243 client tests, and the build treats every compiler and analyzer warning
as an error. The client build also checks its own output rather than its imports: the browser
bundle is asserted to contain no TeamsJS and the consent popup's landing document to contain no
framework, both read off the built chunks, because one shared component reaching for
`app.openLink()` would put a Teams handshake into every browser page load with nothing on screen
to say so. The fake Graph the write tests run against truncates a title at 255 characters exactly
as the real service does, so a fake more permissive than Microsoft, the class of defect the first
real write found, cannot hide there again.

M4 went through a review pass before it was committed, and everything it found is fixed. The most
serious finding was that the tab did not work at all in the case it was built for: MSAL files an
on-behalf-of result in a different partition of the token cache from an authorization-code one, so
a tenant that had granted Tenant Consent would have seen the tab sign somebody in and then
fail every Graph call afterwards with "reconnect required". That is an empty Workbench, in
the configuration the whole milestone is about. Nothing in the suite noticed, because the tests
asserted on the session rather than on what the session could do; the two that now exist ask the
tab's session to read a task list and ask a background scan to do the same. Of the seven others, two
were "Sign in again" buttons the sweep missed, which inside the tab would have navigated the frame
to a page Microsoft refuses to let anybody frame; one was erasure leaving the tab still showing a
Workbench against a session it had just destroyed; and one was the framing relaxation applying to
every path under `/teams`, where the SPA's fallback would have served the browser Workbench with the
tab's headers.

M3 went through a review pass before it was committed, and everything it found is fixed. The most
serious finding was that any signed-in member of a tenant could have recorded a Tenant Consent
grant nobody made (start the flow for the state cookie, then reach the callback directly with
`admin_consent=True`), and nothing could ever undo it, so one user could have permanently hidden the
approval invitation from an organisation that had no other way in. The admin-consent flow returns no
signed response, so that cannot be closed; what it cost was the assumption that a recorded grant is
proof, and the screen now keeps an "approve again" route beside the record. Of the other seven, two
would have left data behind after somebody asked to be forgotten: the scan queue was purged once
rather than on every pass, so a row the sync scheduler wrote back could survive and then be
unreachable once the membership record was anonymised; and a purge that exhausted its retry bound
while still deleting reported success, so erasure anonymised on top of a residue. The rest were a
blanket `catch (DbUpdateException)` reading a deadlock as "somebody else won the race" in two stores,
a `SweepInterval` that validation allowed up to a year while `PeriodicTimer` throws past 49 days
(a boot crash from a setting that passed startup validation), a browser navigation answered with
problem-JSON in the tab, and a nav link styled on a wrapper rather than on the anchor, so the header
would have rendered browser-blue.

A second adversarial pass ran over the finished milestone, with one reviewer hunting what the
first pass and the suite both missed, and its findings are fixed too. The most serious: the
statistics floor was measured against the cumulative member count, which by design keeps the
forgotten, so a tenant of six where five had erased themselves passed a floor of five while every
figure on screen described the one identifiable colleague left — the exact inference the floor
exists to prevent. The floor and the average's denominator now use the rows that still name
somebody, and a test seeds forgotten members to hold it there. The same pass found that a tenant
with no membership rows at all — reachable, because the sign-in write is logged rather than fatal —
could answer its overview with a 500 out of the aggregate query's empty shape; that the sweep
cleared a backlog at only one batch per hourly tick; and that two tests could not fail (a
last-moment assertion satisfied by a no-op, and a leak assertion hunting a string the seeds never
wrote). What it could not fix it wrote down: a guest's token-cache entry survives their erasure
unusable, and a person who returns after erasure is counted anew — both now in the table below and
in ADR-0009.

M2 went through the same pass, and everything it found is fixed.
Two of the eight would have wedged somebody: the change runner left its loop on shutdown
instead of throwing, so the hand-back never ran and the row sat claimed under a live lease
for an hour — blocking that user's changes and, through the new exclusivity, their scans
too; and the scan's new preemption check sat above the last-page break, so standing aside on
a list's final page threw the whole read away and then excluded that list from the plan of
the very Change it made way for. The other six were an unbounded source key that would have
overflowed its column, a missing index behind a two-second poll, a change tracker never
cleared across a thousand tasks, a journal written after the PATCH rather than before it, a
configuration key bound to nothing, and a colliding React key. A ninth turned up while
writing this page: both write endpoints returned a `Location` header naming a route that
does not exist.

M1 went through the same exercise (three reviewers by lens, then two adversarial
passes over the fixes themselves), and everything it found is fixed. It turned up the kind
of thing a green suite does not: an open redirect
in the sign-in return URL, an inventory table whose column headers could never be selected,
and a single over-long hashtag that could wedge an account's scan permanently. The scan
now holds a lease it renews as it works, and hands its row back on shutdown rather than
freezing it for an hour. See the changelog for the rest.

Applying migrations is an explicit step in every environment — see
[CONTRIBUTING § Development setup](../CONTRIBUTING.md#development-setup).

## What does not work yet

Self-hosting has no support commitment: it is available as is. Nothing in TodoWerk
manages another person's Hashtags, counts Hashtags across people, or lets one person change a
colleague's tasks — that was the old app-only Org Mode, and
[ADR-0008](adr/0008-tenant-consent-is-delegated.md) records why it will not be built. Outlook and
the Microsoft 365 app, on the other hand, are hosts since 2026-09-09: a manifest at schema 1.13 or
later offers a personal tab there whether or not anybody meant it to, so the framing policy and the
Entra registration name them and the walk covers them
([ADR-0010](adr/0010-teams-tab-session-and-framing.md), amended;
[runbooks/teams-tab-walk.md](runbooks/teams-tab-walk.md)).

Two things about the Teams tab look like mistakes in the code and are not. All three of TodoWerk's cookies are now `SameSite=None; Secure` and unpartitioned, and
framing is relaxed on the tab's own document path while every other document keeps
`frame-ancestors 'none'` and `X-Frame-Options: DENY`. Both are decisions with their reasons written
down in [ADR-0010](adr/0010-teams-tab-session-and-framing.md), and the first of them means the
antiforgery double-submit pair has stopped being defence in depth and become the defence.

Four gaps are named here because the code looks more finished than it is; everything else
outstanding is listed after them. Two earlier gaps were closed on 2026-08-25 by walking them
against a real tenant, and each walk found a defect no fake could have shown.

*No tenant has ever approved TodoWerk for real* is closed: the
redirect URI is registered, Microsoft accepts the scope list as spelled, the consent screen
shows exactly the permissions sign-in shows an individual, an approval landed end to end and
is visible in Entra ID as a delegated admin-consent grant, and a second user then signed in
with no consent prompt. The decline path was the find: the real redirect carries
`admin_consent=True` *alongside* `error=consent_required` and no `tenant` parameter, so an
administrator selecting Cancel was recorded as an approval. That was fixed the same day and is
pinned by tests carrying the captured shapes, not invented ones.

*No write has ever reached a real mailbox* is closed too, and the
answers are in "What works today" above: `Update todoTask` takes a title-only body and leaves
due dates, notes, importance and checklist steps alone; only the Hashtag's own text changes;
both Spellings of one Hashtag are rewritten together; undo restores the exact prior title,
lowercase included; a real 404 is skipped and the rest of the Change still runs. Throttling
stayed unobserved, as it did in the first live-tenant run: a seven-task mailbox rate-limits
nothing. The find was the title ceiling. Microsoft To Do keeps 255 characters and silently
stores a truncated title rather than refusing an over-long one, which cut a Hashtag in half
and left a journal row holding a title that never existed in the mailbox, so undo could
never match it. TodoWerk now refuses that write.

**Two cells of the Teams tab's walk could not be performed.** The walk itself is done
(checklist in [docs/runbooks/teams-tab-walk.md](runbooks/teams-tab-walk.md)): the tab was loaded
by Teams on the desktop, on the web and on a real Android device; a Change was previewed, run and undone against a real
mailbox through it; all three themes were exercised including a switch made with the tab open; the
package was uploaded to a real tenant catalog and the app registration steps in
[teams-app-registration.md](runbooks/teams-app-registration.md) were performed against a real
registration. Fourteen cells passed. One defect was reported, reproduced and dismissed as not a
defect the same day — the window that looked like an unprompted popup was
Teams' own post-Add confirmation dialog, and the tab contains no code that could have opened one.

What was not walked, and what that costs:

- **Teams on the web in Safari.** Not verified on macOS or iOS. ADR-0010's account of the refused
  cookie and the card built for it remain predictions that have never run in the browser they exist
  for.
- **A tenant that has not granted Tenant Consent, walked by its first-ever user.** No second tenant
  was available. This is the first run for every new tenant — the explanatory card, the button,
  the popup that follows it — and none of it has been seen working.

Everything else on this page about the tab is now an observation. Those two are not.

**Flagged Emails was the id-collation defect, and the report about it was written after the
fix.** This page said for a week that the list could not be read, while the Teams tab's walk had
observed the opposite on the same real account. The screen read, in round figures:

```text
Flagged Emails — 700 tasks, synced 20 minutes ago
720 tasks indexed
```

The cause was the list's size, not its kind. Nearly all of the account's
tasks are in that list, which makes it the only one large enough to page and the only one likely
to hold a pair of Exchange ids differing in one letter's case. That pair is the id-collation
defect: under the database's default collation the unique index over task ids rejected the second
of two different tasks as a duplicate key, deterministically, so the list never finished indexing
and reported itself as one that could not be read. It was met at some six hundred indexed tasks,
a count only reachable inside this list, because the others hold a handful of tasks between them.

The dates show the order. `20260811064210_GraphIdBinaryCollation` re-collated the id columns at
06:42 on 2026-08-11 and the collation fix was closed within the hour; the report that Flagged
Emails could not be read was written that evening, as a write-up of repeated earlier sightings it
says outright had never been written down. The symptom was real and had already been repaired when
somebody recorded it. Two tests in `IndexScanTests` put the projection list through a first pass
and a delta pass (the list had never been scanned in the suite at all), and the first of them
fails with a duplicate key the moment that migration's collation is put back. Graph's delta endpoint serves the list like any other, which
is what the report assumed it could not do.

**Live-tenant verification is incomplete.** A first round trip against a real tenant
has been run: sign-in, a live task-lists read, and a sign-out defect it surfaced, which is
fixed. A live scan then wedged a whole list on ids Microsoft Graph issues in mixed case and the
database compared without regard to case. That is fixed too; nothing in the fake-Graph suite
could reach it, which is the reason the exercise exists. Four defects came out of it in all, and
all four are fixed: that id collation, a write failure reported as a read failure, a connection
string the driver could not parse that still let the application start, and the Flagged Emails
read, which was the id collation under another name and is described above. What it never
produced is the other half of its checklist. Paging and throttling against live Graph went
unobserved, because the tenant answered every request and rate-limited nothing, and the delta
endpoint has still only ever run against a fake. The scan handles all three the way the
documentation says to; nobody has watched it happen. Finishing the exercise needs a mailbox
that misbehaves.

**The hashtag grammar is settled by argument, not by observation.** ADR-0005 fixes
what a Hashtag is and the extractor implements it, but several of its rules are
recorded as uncertain — whether the To Do clients highlight non-ASCII letters, digit-
only tags, or a `#` after a bracket. `scripts/Probe-HashtagGrammar.ps1` seeds the
cases and emits a checklist; until somebody runs it against real clients and fills it
in, the extractor is deliberately conservative and may under-recognise. The decision is made
and the code follows it, so this page is the only thing tracking the observation it still wants.

### Everything else known to be outstanding

Smaller than those four, and written down so that none of it has to be rediscovered:

| Gap | Where it stands |
|---|---|
| Every existing user meets the reconnect path on upgrade | Sign-in now asks for `Tasks.ReadWrite` ([ADR-0007](adr/0007-one-consent-grant.md)), and a token-cache entry issued under `Tasks.Read` cannot silently widen: MSAL raises `MsalUiRequiredException`, the gateway maps it to reconnect-required, and background syncs stop until the user signs in again. In practice that is only whoever signed in before the scope widened, which predates the first release, and it is the reason the Workbench's sign-in offer now keys off a failure code rather than a matched sentence. |
| The module boundary is enforced within a layer, not across layers | `ModuleBoundaryTests` checks that `Infrastructure.Changes` does not depend on `Infrastructure.Indexing`, and the same pair in each other layer. It does not check `Infrastructure.Changes` against `Domain.Indexing`, because the assemblies differ — and two places rely on exactly that: each queue's claim names the other's entity so that "never both at once for one user" can be one conditional `UPDATE` rather than a check with a window after it. Deliberate and commented at both call sites, but nothing stops a third use appearing that is not deliberate at all. |
| The confirm dialog draws 200 pairs, not 1,000 | The dialog was specified with room for up to a thousand old-title/new-title pairs. The dialog renders the first two hundred and counts the rest ("…and 43 more"), because a thousand rows of two titles is a scroll nobody reads and a render cost on every keystroke of the target. The plan itself is unaffected — the ceiling is still a thousand tasks, and all of them are changed. If somebody wants to inspect all thousand, this is the thing to revisit. |
| Two Pending rows for one user can still race into running together | The Change claim refuses while a scan is running and the scan claim refuses while a Change is running, both inside the claim's own `WHERE`. Two workers claiming simultaneously can still both see no live row and both win, because read-committed does not serialise the `EXISTS` against the other's uncommitted update. The consequence is the cosmetic one ADR-0006 already accepts — a half-renamed inventory moving under somebody watching — not a corrupt index, and it needs both timers to fire inside the same few milliseconds. Closing it properly means a lock hint or an application lock, which was not worth adding for a cosmetic race. |
| A crash between journalling a write and making it can leave a count one too high | The journal row is written and saved before the PATCH goes out, deliberately: the two orders fail differently, and this one fails safely. Crash in that window and the journal claims a write that may not have happened — undo compares the current title against what was recorded, finds no match, and leaves the task alone. The other order loses the record of a write that did happen, and undo would then omit a task TodoWerk really changed. A count that is one too high beats a promise quietly broken, but the count is still wrong. |
| A rewritten title longer than Microsoft To Do stores is skipped, not written | To Do keeps 255 characters of a title and silently stores a truncated one — the first 252 plus three full stops — answering success either way (observed live). A Change that would produce a longer title therefore skips that task with a reason: writing it would cut a Hashtag in half and journal a title that never existed in the mailbox, which undo compares against, fails to match, and skips, so the damage would outlive the only thing that could take it back. Reachable by renaming a short hashtag to a long one in a title already near the limit — seen firing, both live and in tests. The 512-character columns remain the last word on what can be recorded at all, for a title TodoWerk did not write. |
| Any write to a task restarts its staleness clock | A Hashtag is called stale when the newest `lastModifiedDateTime` among its tasks is older than `Indexing:StaleAfter`, and Graph gives a task a new one whenever anything writes to it — including a Change TodoWerk itself ran, and including the user simply completing the task. So a Change across a hundred tasks clears the flag from every Hashtag it rewrote, and a Hashtag nobody has thought about for years stops looking old the moment its last task is ticked off. [ADR-0006](adr/0006-what-a-change-is.md) accepted that as wrong in the harmless direction: those are the tags the user has just deliberately handled, and there is no other clock — Graph exposes nothing for a task having been looked at. It becomes wrong in the unhelpful direction the day somebody renames in bulk without caring which tags they touch, because the whole inventory then reads as current and the flag has nothing left to say until the window passes again. |
| Applying markers does not skip completed tasks | A task that is done carries its hashtags like any other, so an Apply Markers rewrites its title like any other — and so do Rename and Merge, which have never distinguished them either. The index does not know completion state: `IndexedTask` holds the title, the list and the last-modified moment, because that is what counting hashtags needs, and [ADR-0014](adr/0014-marker-rules-are-a-fourth-change.md) declined to widen it for this. Knowing would mean reading and storing a status per task and keeping it current through delta pages, which is a change to what TodoWerk holds about somebody rather than a filter on a query. Recorded rather than fixed: somebody who marks a hashtag they have used for years will find the emoji on tasks they finished last spring. |
| A rule is carried by a Rename only when the Rename wrote something | "At least one task written" is the condition ([ADR-0014](adr/0014-marker-rules-are-a-fourth-change.md)), and it is asked of the plan rows at completion. A run that is resumed after its process was replaced counts the writes from every pass, because the question is asked of the table — but a Rename that wrote nothing leaves the rule on the old name, which is right, and one that wrote a single task moves it, which is also right and may surprise somebody who cancelled after the first task. |
| Whether a marker is stale is answered from two sources, which agree by construction rather than by check | The stale count reads a task's hashtags off its Occurrences; a Remove's preview reads them off the stored title, through the same grammar the scan extracted the Occurrences with. Two readings of one fact, and nothing asserts they match. They cannot diverge on a title Microsoft To Do stores — it keeps 255 characters and the column holds 512 — so what would have to happen is the extractor changing between the scan that wrote the Occurrences and the read that plans the Change. Recorded rather than fixed: making the planner's reader hand back the keys, as the coverage reader already does, is a widening of that port for a divergence nobody has produced. |
| A row kept only to keep a marker known is emptied by a Remove that reached every task carrying it, and not otherwise | A deleted rule's row goes when a Remove Markers has taken its emoji out of every block there was. "Every block there was" is asked strictly: a task passed over because its title is nothing but the markers being removed, or a list that has never been read end to end, makes the plan short of the emoji, and the row then stays however much the run wrote — a row dropped over an emoji still sitting in a real title would leave the block reader stopping in front of it, which is the artefact those rows exist to prevent. The person is offered the removal again next time, so this settles itself. What does not settle is a row whose emoji no task carries at all — a rule deleted before it was ever applied: there is nothing for a plan to write, so nothing ever reaches it and it stays until erasure. It is listed nowhere and offered nowhere; the cost is one row per emoji abandoned that way, which nothing but erasure reclaims. It costs nothing at read time, because the coverage query carries no parameter per emoji. Emptying it would mean deleting a row on the strength of the index saying "no task has this", which is the one thing the index is not authoritative about. |
| A rule remembers one retired marker, and two edits with a partial run between them can orphan one | A rule keeps the one Marker its titles carry that is not its own, and it is told what they carry when an Apply or its undo finishes — so a rule edited while its Apply was queued, and an undo that puts an old Marker back, both leave the rule able to reach what is out there. What it cannot do is remember two: change 🍞 to 🥐, apply, change to ☕, undo the Apply, and the titles the undo restored carry 🍞 while any it skipped for being edited since carry 🥐, and the rule can be told only one of them. The one it is not told stays in that title until a Remove Markers takes it, which is now something the person can ask for. ADR-0014 settled on one retired Marker deliberately, and this is the corner of it. |
| The marker figures are as fresh as the last scan, and one of them reads low until it lands | What counts as marked is the block grammar, which the database cannot run — so the index stores the run of emoji each title opens with, and `GET /api/marker-rules` asks for the tasks where that is not empty and walks the runs in C#. Two queries however many rules there are, no query parameter per emoji, and a filtered index over the same predicate, so the read walks the marked tasks rather than every title. What it inherits is the freshness of the index: a title edited in Microsoft To Do since the last scan is counted as the scan left it, which the freshness chrome above the table already says of every figure on the page. The run is written by the ordinary scan write path, so a migration that adds the column backfills by clearing every delta link and letting the next sync re-read each list; until that lands the coverage and stale figures read nought while the inventory beside them is unaffected. |
| The ceiling for an Apply or a Remove is measured against tasks that could need a write, not against tasks that do | The three Hashtag Changes exclude "already spelled that way" in SQL; a block cannot be excluded that way without the grammar going into the database. So a person with more than a thousand tasks carrying marked hashtags is refused an all-rules Apply with the count even on the second run, when almost nothing needs writing, and a Remove is refused on the same footing — its ceiling counts every task whose title holds any of their emoji, whether or not any of them is stale. Doing one rule, or one emoji, at a time is the way through it, as [ADR-0014](adr/0014-marker-rules-are-a-fourth-change.md) says — but the refusal will read as arbitrary to whoever meets it after a successful first run. |
| The change history is capped at 50 | `ChangeStore` hands the Workbench the 50 most recent Changes. Thirty days of history is usually a handful, but somebody who makes a great many would find the oldest of them undoable in principle and invisible in practice. |
| No test watches the application refuse to start | `ConfigurationValidationTests` used to boot the whole app and wait for the failure, and was green about four runs in five. The cause found is in the test host, not the product: under `WebApplicationFactory` startup validation runs on the entry point's own thread, after the host has been handed back and while `RunAsync` is disposing it, so what `CreateClient` throws is decided by scheduling — demonstrated, though at one boot in 300 and with a different symptom than the one reported, which never reproduced. The test now asks the startup validator directly and is deterministic. Two things are no longer watched by anything: the host actually stopping, and a `Program`-level `PostConfigure` that quietly repaired a bad setting. Both could be covered by moving `Program`'s composition into a method a test can call and start on its own thread; that was weighed and declined, because it reshapes the file every contributor reads first in order to suit a test. That prediction was met: two licensing tests written after this row booted the app and asserted on the throw, and failed under load exactly as described. Both now ask the validator, and `TestConventionTests` fails the build on the next test that asserts a boot throws, so this row records a gap in coverage, not a warning about what will flake next. |
| Only some of the client is tested | The suite arrived with M2 and covers what it arrived for: the state the client holds before the server confirms it. The grid has no test of its own. The Workbench's page test finds rows in it on the way to the query state it is about, and asserts nothing about its columns, its sort headers or its flags. Neither has the Teams tab's shell: the cards it can show instead of the Workbench, and the wrapper that picks one, are verified by `tsc` and by reading, while the decision *which* card it is, made in `session.ts` against a stubbed gateway, is tested. The confirm dialog, the freshness chrome, the Tenant Overview and the erasure dialog each acquired a test during M5, at the moment it took on state of the kind this suite is for: a spelling refused before the server sees it, a breakdown that unfolds by itself while a scan runs, an invitation hidden under a Personal Licence, a dialog that will not close once destruction has been asked for. That is the intended shape ([CONTRIBUTING § Testing](../CONTRIBUTING.md#testing)): a client change gets a test when it touches that held state. It does mean a rendering regression in the untested parts would reach a person before it reached a test. |
| Nothing but branch protection stops a commit skipping CI | A ruleset on `main` makes the pull request compulsory and requires the `build-and-test` and `cla` checks. Repository administrators can bypass it and push to `main` directly; CI then runs on the commit after it has landed, not before. That bypass is the one way left for a commit to reach `main` unvalidated. Before the rule existed, the first live-tenant fixes, all of M2 and all of M3 went straight to `main`, so validation never ran on any of it until the first pull request after M0. That run passed, on Linux in Release against a SQL Server container, so nothing was hiding in the difference from a Windows workstation. |
| Startup checks the connection string, not the database | An unparseable connection string fails the boot, but a well-formed one pointing at a database that is down does not: `DatabaseReadinessCheck` reports it in the log and the app serves. That is deliberate, and reversing it should be a decision too, not an accident. |
| The first scan after upgrading past the id-collation migration re-reads everything | The collation migration (`GraphIdBinaryCollation`) clears every delta link on purpose: rows a case collision swallowed were never written, and no incremental pass would ever mention them again. Expect one full pass per list, and freshness to read as unknown until it finishes. |
| Erasure is not exclusive with a scan or a Change | Those two exclude each other inside their own claims; erasure joins neither scheme. A scan running at the moment somebody erases themselves is stopped by the disappearance of the row it claimed — the runner renews its lease after every page and gives up when the row is gone — and the purge repeats itself while it keeps finding rows, so the page written in between is caught on the second pass. What is left is a window of milliseconds in which a page could land after the last pass. Closing it properly means a third participant in both claims' conditions, which was not worth adding for a residue the repeat already sweeps. |
| The statistics begin the day the migration is applied | Nothing recorded a sign-in before M3, so there is nothing to reconstruct the earlier ones from. On a real tenant already using TodoWerk before then, that means the first sign-in date is the upgrade date rather than the truth, and every count starts from zero. Deliberate: a backfill would have to invent the moments it was inventing the rows for. |
| A sign-in whose membership write fails is not counted | Logged as an error and allowed to proceed, because refusing somebody the product over a statistics row is the worse failure. It costs one uncounted visit and a retention clock that did not move. A failure persistent enough to matter is one that stops the sweep too, since both read the same table through the same context — but the count is still short, and nothing but the log says so. |
| TodoWerk cannot see consent granted or revoked outside itself | Reading the service principal's grants from Graph costs a directory permission to answer a boolean, which [ADR-0008](adr/0008-tenant-consent-is-delegated.md) declined. So the screen says the grant was or was not made *through TodoWerk*, an administrator who approved in the Entra portal keeps seeing the invitation, and a revocation is handled by everybody going dormant rather than by TodoWerk noticing. |
| A recorded consent grant is TodoWerk's own note, not proof | The admin-consent flow returns no signed response, so nothing in the redirect proves it came from Microsoft. A signed-in member of the tenant can therefore start the flow and reach the callback themselves, and a grant gets recorded that nobody made. Not closable without the directory permission ADR-0008 declined, so the blast radius is what was bounded instead: the record hides the invitation and does nothing else, and the screen keeps an "approve again" route, so a wrong note is recoverable by anybody who notices people are still being prompted. |
| A guest's token-cache entry survives their erasure | The purge evicts by the directory identifiers (`oid.tid`); MSAL keys entries by the home ones (`uid.utid`), and for a guest the two differ. What survives is unusable — `GraphGateway` rebuilds the same directory-claims principal and misses the entry too — and lapses within the 90-day sliding expiry, but "their token cache entry is evicted" is false for guests. The same caveat the gateway already carries: guests are unsupported until tried against a real tenant, and fixing this properly means storing home identifiers, which [ADR-0009](adr/0009-what-todowerk-stores-about-a-person.md) forbids. |
| A person who is erased and later returns is counted twice | Inherent to anonymisation rather than an oversight: recognising the returner would take the identifier erasure removes ([ADR-0009](adr/0009-what-todowerk-stores-about-a-person.md)). The cumulative count records arrivals, not distinct persons. The floor and the average are unaffected — both are computed over the rows that still name somebody. |
| The dormancy sweep is its own worker, not a job on an existing one | The sweep was specified "on the existing worker cadence". The existing workers live inside the Indexing and Changes modules and cannot reach Onboarding's eraser without the cross-module dependency the boundary tests forbid, so the sweep got a worker of its own on an hourly tick — a year-long deadline needs no three-second poll. Deliberate, and recorded here because the specification said otherwise. |
| An erasure that could not finish shows the reader problem-JSON | The erasure control is a form POST, because its success answer is a redirect to the identity provider's end-session endpoint and only a browser navigation follows one. So its failure answer is a problem-details document rendered as text in the tab — the same shape sign-out has always had on its own failure path. The `detail` is a sentence written for a person ("something was still working on your data — try again in a moment"), so it reads rather than merely appears, but it is a JSON page after a sensitive action. Fixing it properly means the SPA driving erasure and handling the sign-out redirect itself. |
| The Teams tab's bootstrap is reachable by no test | `app.initialize()`, `getAuthToken()` and the on-behalf-of round trip run in one module that imports TeamsJS, and the client suite tests the state the client holds rather than what it renders ([CONTRIBUTING § Testing](../CONTRIBUTING.md#testing)). What *is* tested is the decision the bootstrap makes given three responses — sign in, offer consent, blame the cookie — which is the part that has branches. What is not is the handshake itself, and reshaping the code so a test could reach it would buy a mock of Microsoft's client rather than evidence about it. The Teams tab's walk closed it by other means: the handshake was exercised by Teams desktop, Teams web and Teams mobile, and it worked on all three. The gap is therefore in the *test suite*, not in the code: no test guards the handshake against a regression, and the next one will be found by a person, not by CI. |
| Teams on the web in Safari is expected not to hold a session, and the mitigation has never run | Safari blocks unpartitioned third-party cookies outright, and inside the Teams frame that is what `todowerk.session` is ([ADR-0010](adr/0010-teams-tab-session-and-framing.md)). The tab detects the exact signature — a successful exchange followed by a 401 — offers the browser instead of failing silently, and the server logs the same condition as itself rather than as one more anonymous 401. Closing it properly would mean the bearer-token design ADR-0010 declined: it would buy the Safari case with two ways of knowing who is calling, on every endpoint, permanently. The walk answered the mobile half and could not answer the Safari half. Teams on a real Android device held the session perfectly and no card fired, so the mitigation is not needed there. Safari itself has **not been verified on macOS or iOS**, so every sentence in this row about Safari's behaviour, and every line of the card built for it, remains a prediction that has never run in the browser it exists for. A hosted browser session would settle it in one sitting; the log line carries the user agent so that whoever does it can confirm the signature, and is diagnostic evidence rather than proof: the header that produces it is asserted by the client, grants nothing, and is capped at sixty diagnostics a minute with the ceiling reporting itself. |
| The first run of a tenant that has not granted Tenant Consent has never been seen | The tab's answer to a failed on-behalf-of exchange — the explanatory card, its button, and the popup that runs the existing server-side OIDC flow — is the expected first run of every new tenant, and the walk could not reach it: no second tenant was available. The decision logic *is* tested (`session.test.ts` pins which of the five outcomes each response produces) and the popup path is the same one erasure and reconnect use, both of which were walked. What has never happened is the whole sequence, in a real tenant, for the person who arrives first. A free Microsoft 365 developer tenant would close it. The browser's half of the same first run *has* now been walked, and walking the decline path found that the sign-in callback answered HTTP 500 with a problem-details document, because nothing handled a remote failure. That is fixed — the callback never throws now, and `SignInFailureTests` pins where each way of failing lands. |
| The `glass` theme is untestable here | Teams reports it on Apple Vision Pro, which nothing in this project can be tested against. It falls back to the light Teams theme rather than throwing, and a test pins the fallback — but "falls back gracefully" is a claim about code rather than an observation, and it stays one. |
| A Teams sign-in's tokens live in a different partition of the cache from a browser sign-in's | This is a design to keep, recorded so that nobody simplifies it away. MSAL files an authorization-code result under the home account id and an on-behalf-of result under a hash of the assertion, which nothing outside that one request holds — so the tab's tokens are filed under a session key TodoWerk chooses instead (`TeamsSsoDefaults.SessionKeyFor`), and the Graph gateway looks in the account partition first and that one second. The cost is one thrown-and-caught exception on the first Graph call of every request made by somebody who signed in through Teams. Removing either half breaks one of the two sign-ins, and only the tests added with it would say so. |
| `validDomains` names TodoWerk's host and not the identity provider | The original plan listed both. Microsoft's schema says not to list the domains of identity providers, and the Teams Store validation guidelines name `*.microsoftonline.com` as a domain that is not allowed at all — a must-fix finding. The plan's own reasoning argues the same way: the popup starts and ends on TodoWerk's domain with the round trip in the middle, which is exactly why the middle needs no entry. Deliberate, and recorded here because the plan said otherwise. |
| The Guide — the Handbook's half for the person using the product — is not part of this repository | The application's half of the Handbook is built: `/about` is what the manifest's `websiteUrl` now names, so the Teams admin center's support link no longer lands on a sign-in page, and `/administrators` is what `publisherDocsUrl` names. The Guide is published separately and is not part of this repository ([ADR-0013](adr/0013-the-handbook-is-split-by-kinship.md)). A deployment that leaves `Handbook:GuideUrl` unset carries no Guide entry in the header's help menu, and its About Page's "Read more" lists the page for administrators alone. The screenshot seeder (`scripts/Seed-HandbookScreenshots.ps1`) is here; the pictures are not. M7 adds to what the Guide has to say: five Changes rather than three, and five promises about Marker Rules — rules never write on their own, applying adds and reorders but never removes, a stale marker stays until you remove it yourself, removing takes only emoji TodoWerk put there and never one you typed, and a rename carries the rule. It promises nothing about how To Do sorts or searches a title that starts with an emoji, because nothing here knows. |
| Licensing has been walked against a local licensing service only | TodoWerk's resolver, portal client and seat reporter have been walked over the network against a licensing service built from source and run locally, not against a deployed one. |
| A Licence answer is cached in memory, so a restart costs one resolution per person | Deliberate, and the alternative is the second Licence authority [ADR-0001](adr/0001-single-entitlement-authority.md) exists to prevent: a table of who is licensed is a Licence record, whatever it is called. What it costs is one call to the portal per active person after every deploy, and — because the once-a-day seat claim lives in the same memory — one extra usage report, which the portal treats as a refresh of a last-seen timestamp rather than a second seat. |
| TodoWerk cannot tell a declined sign-in from one that needed an administrator | Both come back as `access_denied`, and which AADSTS code rides along depends on the tenant's consent policy rather than on what the person did — so the card after a refused sign-in names both readings and offers a route for each rather than asserting one. Two cards would mean guessing, and the guess would be an accusation to whoever it was wrong about. The log line carries the code Entra ID actually sent, so a run of real occurrences can sharpen this into two cards later; until then nobody has the evidence to. |
| An approval started from the refused-sign-in card is reported and never recorded | That flow begins with nobody signed in, so its state binds to no tenant, and the `tenant` parameter Microsoft sends back is — by Microsoft's own documentation — forgeable. Recording it would let anybody who can reach the callback switch off any organisation's invitation to approve, which is strictly worse than the bounded version [ADR-0008](adr/0008-tenant-consent-is-delegated.md) already accepts. So the round trip tells the person what happened and writes nothing: the grant is recorded when somebody approves from inside the product, and until then the invitation keeps showing — the same blindness this page already records for consent granted outside TodoWerk. |
| A denied person's queued scan is claimed and handed back on every tick | The claim is what learns whose row it is, so the gate is asked after it and the row is held for the rest of that tick to keep the drain loop moving past its owner. For a denied person with work queued that is a claim and a release every fifteen seconds (three for a Change), for as long as they stay denied and the row stays queued. Bounded by the number of denied people with pending work, which in a healthy deployment is nobody — but it is churn against the database for rows that will not run, and a `WHERE` that could exclude them would need the licence answer in SQL, which is the thing that must not be there. |
| A Change running while its owner's Licence lapses is watched as a row, not as a write in flight | Closed in part: `WorkerLicensingTests` now drives both workers over a real queue, and pins that a denied person's queued scan and queued Change survive a tick exactly as they were while their colleague's run in the same tick, that the claim really did ask the portal about them, and that a Change already claimed keeps its lease and is not asked about at all. What no test arranges is the write itself — a runner PATCHing tasks at the moment the portal turns its owner away — because nothing consults the gate once a Change is running. "Never cut off mid-write" is therefore a fact about there being no second check rather than an observation of one, and it would stop being true the moment somebody added one. |
| One person's memoised near-duplicate set outlives their erasure on other instances | The inventory memoises each person's Hashtag names in process for a minute; erasure evicts it from the instance that ran the erasure. On one instance — which is every deployment today — that is the whole of it. Scaling out would leave the other instances holding it until the lifetime expires, exactly like the token cache's L1, and the fix is the same `DisableL1Cache` conversation rather than a change here. |

## Roadmap

Work is cut into vertical slices. Each milestone is meant to leave something you can
demonstrate, not a layer you cannot see.

| Milestone | Outcome | State |
|---|---|---|
| M0 — Walking skeleton | Solution scaffold, sign-in, one real Graph read, CI | Complete |
| M1 — Index & inventory | Hashtag index with delta sync; inventory table with issue flags | Complete — closed 2026-08-11; the Flagged Emails read reported against it turned out to be the id-collation defect and was already fixed, and live paging and throttling are still unwatched |
| M2 — Rename & merge | Job queue, dry-run preview, journaled undo | Complete — closed 2026-08-12; the write path verified against a real mailbox 2026-08-25, which found and fixed a silent title truncation undo could not recover |
| M3 — Tenant Consent & Tenant Overview | Delegated admin consent, tenant statistics, retention and erasure | Complete — closed 2026-08-12; verified against a real tenant 2026-08-25, which found and fixed a decline recorded as an approval |
| M4 — Teams tab | Teams SSO, app package | Complete — built 2026-08-26, closed 2026-09-01; walked against a real tenant on Teams desktop, Teams web and a real Android device, with a Change run and undone against a real mailbox through the tab. Fourteen cells passed and the one defect reported was dismissed the same day as not a defect — the first walk in this project to find nothing real. The App Package passes the Teams Store validation tool with zero errors and zero warnings, on a build carrying the settled developer name. Two cells could not be walked and are recorded above as gaps rather than passes: Safari, and the first user of an un-consented tenant |
| M5 — Licensing & polish | Licence resolution over the First-Party Path, Licence Banner, Tenant Overview Licence panel, browser dark mode | Complete — shaped 2026-09-02 ([ADR-0012](adr/0012-three-kinds-of-licence-resolved-per-person.md)) and built 2026-09-03 against the licensing service's documented contract. Walked against a licensing service built from source and run locally — six cells observed, one finding, fixed. The notification email is dropped: it was designed for the Org Mode ADR-0008 retired, and would have needed an address on disk that ADR-0009 forbids. The tips card is deferred with no plan attached |
| M6 — Self-Host & listing | Compose packaging, setup docs, marketplace listing | Partly done — the first release (1.0.2) shipped: the Microsoft Store listing is published and this repository is public. The application's half of the Handbook landed 2026-09-03 ([ADR-0013](adr/0013-the-handbook-is-split-by-kinship.md)), the Guide is published separately, and setup docs exist as [runbooks/deploying-a-self-host.md](runbooks/deploying-a-self-host.md). Compose packaging is not done |

Self-hosting is available as is. You can run TodoWerk from source, as described in the
[README](../README.md), or run the CI container on a server of your own, as written down in
[runbooks/deploying-a-self-host.md](runbooks/deploying-a-self-host.md). Neither carries a support
commitment yet, and until Compose packaging exists a deployment needs its own compose file.

## Decisions already settled

Fourteen architecture decisions are recorded and are not up for casual revision. ADR-0010 and
ADR-0011 are M4's, settled by design before any of it was built and unchanged by building it.
ADR-0002 gained an amendment on the way: the Teams tab put a bearer token on exactly one endpoint,
which its consequences had written down as never happening. ADR-0012 is M5's, settled on 2026-09-02
before a line of it existed.

- [ADR-0001](adr/0001-single-entitlement-authority.md) — a Licence is resolved per person from one
  external licensing authority over a defined contract; TodoWerk holds no Licence record and
  contains no marketplace code, and a Self-Host has no Licence at all and is never refused.
- [ADR-0002](adr/0002-backend-held-tokens.md) — every Graph token stays on the
  server; the browser gets a session cookie.
- [ADR-0003](adr/0003-single-sql-store-own-index.md) — one SQL database holds the
  index, the sync state, and the job queue.
- [ADR-0004](adr/0004-fluent-ui-v9.md) — Fluent UI v9 with the CloudWerk brand theme.
- [ADR-0005](adr/0005-what-a-hashtag-is.md) — casing does not distinguish one Hashtag
  from another; the folded key is computed in C# and compared as bytes.
- [ADR-0006](adr/0006-what-a-change-is.md) — a Change is a set of source Hashtags and one
  target Spelling; only the Hashtag's own text is rewritten, each task is re-read
  immediately before it is written, and a journal makes the whole Change undoable.
- [ADR-0007](adr/0007-one-consent-grant.md) — one consent prompt covering everything;
  sign-in asks for `Tasks.ReadWrite` and nothing narrower.
- [ADR-0008](adr/0008-tenant-consent-is-delegated.md) — Tenant Consent is admin consent to
  that same delegated grant; no application permission, no directory permission, no app-only
  credential, and no tenant-wide management of anybody else's Hashtags.
- [ADR-0009](adr/0009-what-todowerk-stores-about-a-person.md) — a tenant id, a pseudonymous
  object id and two timestamps; twelve months of dormancy; anonymisation rather than deletion
  on cancellation, so the cumulative count survives with nobody identifiable in it. Amended in
  M4: its "no user agent" is about the record, and one diagnostic log line mentions a browser.
- [ADR-0010](adr/0010-teams-tab-session-and-framing.md) — the Teams Tab keeps the cookie
  session rather than taking a bearer token, so all three cookies become `SameSite=None;
  Secure` and unpartitioned; framing is relaxed on the tab's own document path and nowhere
  else.
- [ADR-0011](adr/0011-two-app-packages-and-a-template.md) — App Packages are built per
  deployment from one template; a Self-Host builds its own and no deployment's values are
  committed.
- [ADR-0012](adr/0012-three-kinds-of-licence-resolved-per-person.md) — three kinds of Licence:
  a Tenant Licence for everybody, a Personal Licence for one person, and a Trial whose
  time-limited term the licensing service sets, with no trial clock in TodoWerk; resolved per
  person from the external licensing authority, denied per person, and Tenant Consent offered only
  under a Tenant Licence or a Trial.
- [ADR-0013](adr/0013-the-handbook-is-split-by-kinship.md) — the Handbook is split by kinship: the
  About Page and the Administrator's Guide live with the application, served by every deployment
  and named by the App Package; the Guide is published separately and is not part of this
  repository. The Administrator's Guide is a second telling of what
  the ADRs decide, and reads its numbers from the running configuration.

New decisions are written up as they are made. A pull request that contradicts one should
make its case against that decision openly, not work around it.
