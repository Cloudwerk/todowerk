# Changelog

All notable changes to TodoWerk are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
this project will adopt [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
at its first release. There is no release yet — see
[docs/status.md](status.md) for what works today. Until then, entries are grouped by
milestone rather than by version.

## [Unreleased]

### Added

- You can give a hashtag an emoji, and every task carrying that hashtag comes to start with it.
- Nothing is written until you apply your markers, which is previewed as the exact titles, runs in the background, and can be undone as a whole for thirty days like every other change.
- You can apply all your markers at once, or only the one you are looking at.
- Applying adds and reorders emoji and never takes one away, so an emoji whose hashtag you have since removed stays where it is until you say otherwise.
- Removing markers is how you say otherwise: TodoWerk takes an emoji off the tasks that no longer carry its hashtag — all of them at once, or one emoji at a time — previewed as the exact titles and undoable for thirty days like every other change.
- An emoji you typed yourself is never touched: TodoWerk only removes emoji it put at the front of a title, and a task that still carries its hashtag keeps its emoji whatever else you remove.
- A task titled with nothing but the emoji being removed is named in the preview and left alone, rather than left with no title at all.
- Each marker says how far it has got — "3 of 7 tagged tasks carry 🍞" — beside the hashtag it is about and in the list of your markers, and how many tasks still carry it without the hashtag.
- Deleting a marker rule no longer loses sight of the emoji it left on your tasks: those are now listed under your markers, with the number of tasks carrying each and a way to take them off. Once they are off, TodoWerk stops keeping a note of the emoji at all.
- A rule that is waiting to swap one emoji for another now says how many tasks are still waiting, and stops saying anything once the swap has reached them all.
- Renaming a hashtag takes its emoji with it, and undoing the rename brings it back.
- Folding two hashtags that carry different emoji into one asks which emoji survives before it will run.
- A Licence Banner on the Workbench says when your trial is running and names the date it ends.
- The screen that says your access has ended now offers a way to buy TodoWerk, in a browser, when there is one.
- Your organisation's page names the licence that covers you and the date it runs to.
- The browser Workbench follows your operating system into dark mode, and changes with it while the page is open.
- A page about TodoWerk at `/about` — what it is, who runs this installation, and where to get help — and a page for administrators at `/administrators`, which explains what approving TodoWerk for an organisation grants and does not, what it stores about a person, how somebody is forgotten, how to revoke it and how to roll it out. Both open without signing in.
- A help control in the header opens the page about TodoWerk, the page for administrators, the terms of use, the privacy notice, and the Guide on installations that have one.
- The invitation to approve TodoWerk for the organisation now says what is on the page for administrators before you open it.
- If nobody approved TodoWerk when you signed in, the screen now says so and offers to have it approved for your whole organisation — without needing you to sign in first, which you cannot do until somebody has.
- The help control opens before you sign in, so the page about TodoWerk and the page for administrators can be reached from TodoWerk's own front door — which is where an administrator deciding whether to allow it arrives, and they may never sign in at all.
- A deployment now says, to anybody who asks, whether it checks licences at all or lets everybody in — so whoever runs it can see from outside that it is running the way it was promised to.
- When a scan finds no hashtag in any task, the Workbench now says how to write one into a task title in Microsoft To Do and what a hashtag buys you there, and offers the next scan from the same place.
- On a phone, the Teams tab now says that the hashtag workbench needs a desktop or laptop screen instead of showing a table that does not fit, and it says so before signing anybody in.
- Inside a host, the menu under your name now says where sign-out lives: signing out of Microsoft Teams, Outlook or the Microsoft 365 app signs you out of TodoWerk too.
- Every link that leaves the tab for your browser carries a pop-out glyph, so it says so before you click.

### Changed

- The tab no longer prints "TodoWerk / Hashtag Manager" inside Microsoft Teams, Outlook or the Microsoft 365 app, which already print the app's name above it. In a browser the header is unchanged.
- The About page, the page for administrators and the tab's sign-in cards now name every host the tab runs in: Microsoft Teams, Outlook and the Microsoft 365 app. The About page and the Store description also say that TodoWerk is available in English.
- The Store description, the manifest and the terms of use no longer promise that the tab names whom to contact when a licence ends; the About page does, and the sentence now says so.
- The privacy notice and the terms of use now state this installation's own dormancy window — the number of days without a sign-in after which a person is forgotten — rather than the default, the way the Administrator's Guide already did. A deployment that shortens the window no longer serves a notice promising the longer one.

### Fixed

- Classic Outlook on the web and Outlook on Windows refused to show the tab at all. Its framing policy now names every host Microsoft documents for tabs, not only the ones it had been walked in.
- The Teams tab no longer blames Safari when it is not Safari that dropped the session. Seen in Edge InPrivate, whose default strict tracking prevention refuses cookies in frames just as Safari does: the card now names private windows and tracking prevention as causes, tells you a normal Edge or Chrome window or the Teams desktop app works, and no longer claims Edge is unaffected.
- Opening the markers list is no longer slower the more tasks you have marked. It used to read the full title of every task carrying any of your emoji, every time the list was drawn.

- Declining Microsoft's consent screen used to answer with a page of error text and a trace identifier. TodoWerk now says that nothing was approved, offers the two ways on, and changes nothing about your account. The same page answers a sign-in that was interrupted rather than refused.
- The message saying your licence could not be checked no longer sits there until you reload the page: it now always clears itself once the check succeeds again.
- A request that goes unanswered gives up after a minute and a half and says so, rather than leaving the screen waiting on it for as long as your browser feels like.
- A hashtag your tasks still carry was labelled "unused", beside the count saying how many carry it. It now reads "stale", and names the date none of those tasks has been edited since. The Teams app package's own description said it too, and now says the same thing as the product.
- Choosing an emoji from the grid showed no button pressed when your rule held the same emoji written the other of the two ways it can be written. The grid now reads them as one emoji, which is what the server has always done.
- A rule you created on a hashtag name a rename had just freed could be deleted behind you, if the process running that rename was replaced in the moment between finishing its work and recording that it had. Your new rule is left alone.
- Opening a pull request no longer fails the CLA check for a reason that is nothing to do with you: the workflow now creates the store it keeps signatures in, instead of assuming somebody made it by hand.
- Signing out while shut out for want of a licence answered with a page of error text and left you signed in. Signing out now works whether or not you hold a licence, which is when you are most likely to want it.
- When Microsoft To Do shows you a list but then refuses to give up its tasks, TodoWerk no longer tells you the list has been deleted — it says it could not read the list, and will try again.
- Opening the Workbench at the moment your licence was due to be checked again asked the licensing service several times over, once for each thing the page loads. It now asks once.

**Every request that found a stale licence answer asked the licensing service for itself.** The
answer for a person is cached for the portal's recheck interval, and the cache is written when the
answer arrives — nothing in it said "being asked right now". Opening the Workbench sends half a
dozen requests within the same few milliseconds, each behind the Licence gate, so whenever the
cached answer had just aged out they all missed it together and each made its own call. Seen as
five identical resolutions for one person under one timestamp, from a single page load. Nobody was
denied by it, and every answer was the same; what was wrong is that the rule "never resolve per request" did not hold at
the one moment it was tested. The resolver now keeps one call in flight per person, and every
request that misses the cache while it is out waits on that call rather than sending another. The
call belongs to everybody waiting on it, not to the request that sent it: a browser tab that
navigates away stops waiting, the rest still get their answer, and the cache is still written.

**A declined consent screen was answered with a 500 and a page of JSON**, which is what an
administrator declining Microsoft's approval prompt met — the expected first run of
every organisation that has not approved TodoWerk centrally. The sign-in callback had no answer for
anything but a ticket, so the framework's default applied and the failure was rethrown into the
error handler. It now sends the browser to a card that names both readings of Microsoft's answer —
somebody declined, or the organisation reserves the decision for an administrator — because
Microsoft's own answer does not distinguish them, and offers a route for each: approve for the
organisation, or try the sign-in again. The approval runs the round trip TodoWerk already had, now
startable by somebody with no session, and Microsoft enforces who may complete it. Nothing is
recorded when it comes back that way: the state it began with names no tenant, and the tenant
naming itself on the way back is a parameter rather than a fact. Inside the Teams tab the same
answer reaches the popup's own document, so a declined consent is reported to the tab instead of
leaving it waiting for a window that never speaks.

**A seat report the licensing service declined to store was read as one that landed**, because
only the HTTP status was read; the client now reads the answer's body, and the warning names the
service's own word for why the seat went uncounted.

**A denied browser stopped asking after one retry that failed for a transient reason.** The
"could not be verified" card is meant to clear itself within about half a minute of the licensing
service coming back, with no button and no reload, and it did — as long
as every retry either succeeded or was another denial. One that failed transiently instead ended
the polling for good, and the card then stayed on the screen until somebody reloaded the page. The
cause was the very thing that keeps a denial from being dropped on a dropped connection: the
provider kept it by handing React back the same state object, React skipped the render, and the
effect that arms the next timer never ran again. One dropped connection was enough to reach it —
the same dropped connection that put the card there in the first place. The timer is now armed off
a count of the answers that have come back, which cannot be made equal again by tidying, rather
than off the state changing identity. Deliberately not off the count of *asks*: that would measure
the half-minute from the asking rather than from the answer and land the next ask inside the
server's own thirty-second floor, which runs from the end of the server's own attempt on the
licensing service — after the ask, and before the answer reaches the browser. Found while fixing
the flake below.

**Two licensing tests expected the application to refuse to start, and were decided by the
scheduler.** `docs/status.md` had said since `ConfigurationValidationTests` was fixed that any test
expecting a boot to fail under `WebApplicationFactory` would meet the race between startup
validation on the entry point's own thread and the disposal that follows it. Two tests were written
that way afterwards regardless, and were green until the machine was busy. Both now ask the startup
validator directly, as `ConfigurationValidationTests` already did, and a convention test fails the
build on the next test that asserts a boot throws. The
prediction was already written down and nothing enforced it — that is the part that was actually
fixed.

**The guard against asking the licensing service once per request was inert whenever
`Licensing:RequestTimeout` was set to thirty seconds or more**. After
a failed attempt the server puts a thirty-second floor under the next one, and it stamped that
floor from the moment the attempt *began* rather than the moment it ended. The round trip takes the
whole of `RequestTimeout` precisely when the portal has stopped answering — which is the case the
floor exists for — so the guard was thinnest exactly when it was needed: at the default ten seconds
it covered twenty of its thirty, and at thirty or more it had expired before it was written. Every
request from somebody being served on the fail-open window then paid a full timeout, instead of one
request in thirty seconds paying it and the rest being answered from memory. `/api/licence` paid it
twice over, because the Licence gate and the endpoint behind it each resolve, and the second found
the floor already gone.

The floor after a failure and the recheck interval after an answer now both count from the instant
the attempt ended, from a single reading of the clock — two instants for one answer is how this
went wrong in the first place. The fail-open window counts from there too, which is what its own
field always said it did: when the answer arrived. Nobody was ever denied by any of this. What was
wrong is how slow a page felt while the portal was down, and how often a dark portal was asked.

**No request in the client had a timeout.** `apiFetch` passed `fetch`
only the caller's abort signal, which is used for unmount, so a request that *hung* — something
between the browser and TodoWerk swallowing the connection rather than refusing it — never settled.
Anything armed off that read's answer waited with it: the denied Licence card's retry, and the two
Workbench polls, which refuse to start a second read while one is in flight. The wait ended when the
browser's own network stack gave up, which is minutes and a different number in each browser.

Ninety seconds now, one number for every call. It is sized against the server rather than against
anybody's patience: the Licence gate sits in front of every authenticated endpoint, so any request
at all can end up waiting on one leg to ManagementPortal — which one does is decided by the state of
that person's cached answer rather than by the path — and that leg is bounded by
`Licensing:RequestTimeout`, ten seconds by default but validated up to a minute. The client cannot
see which is configured, so the number has to clear the ceiling and not the default. Under it, this
would have been worse than no timeout at all: the browser would abandon an answer the server was
about to send, and because the server deliberately does not record a caller's cancellation as an
outage, the thirty-second floor that stops it calling the portal once per request would never be
stamped — so somebody being served on the fail-open window would pay a full portal timeout on every
poll instead of one slow request with the rest answered from cache. The remaining thirty seconds are
for everything that is not the portal. Both ends now carry a comment pointing at the other, because
the two numbers are one decision.

Not `AbortSignal.any` with `AbortSignal.timeout`, which is the idiomatic spelling of exactly this:
it is missing on Safari before 17.4 and throws rather than degrading, and this code runs inside the
shell both hosts render, so on that Safari the throw would be the tab failing to paint rather than a
slow retry. TypeScript's own DOM library declares both, so nothing but a comment would have caught
it.

**The client suite's flake was not a timeout.** `npm test` went red
about one run in three, on a test that moved between two files, and every one of them passed when
its file ran alone — which reads as a slow machine, and was written down as one. It was not: the
query that failed was a synchronous `getAllByRole`, which has no timeout to exhaust, and it failed
by reporting no such element while the element sat plainly in the DOM. Fluent hands a modal to
tabster, which decides what is exposed to assistive technology from where the focus is, and in
jsdom tabster decides nothing is focusable at all: it measures that with `getBoundingClientRect`,
and jsdom lays nothing out. So the erasure dialog opened from the session menu never became the
active modal, and a quarter of a second later tabster marked it `aria-hidden` and left it that way,
putting every role inside it out of reach for good. The test was racing that timer, and won it on
an idle machine. It now asks by text, which does not consult the accessibility tree; the assertion
about which of the two buttons comes first moved to the dialog's own tests, where it is now made of
the browser's confirmation as well as the tab's rather than only the tab's. Two smaller things went
with it: `licence.test.tsx` waited for the element carrying the answer rather than for the answer,
which held by luck rather than by construction, and Testing Library's one-second budget for an
await — wall clock, on a runner shared with a SQL Server container — is now five seconds. Nothing
about the product changed and nothing about it was ever wrong; what was wrong is that a red client
run meant "read which test failed and decide whether to care".

### Changed

- TodoWerk stops refreshing your hashtag index in the background once you have not signed in for fourteen days, and starts again within seconds of your next sign-in. Until now every person who had ever signed in was synced every half hour for as long as the installation ran — and where licensing is configured, asked about at the licensing service first, every time, which kept the licensing service busy for idle deployments. Nothing you ask for is affected: a re-scan you request, and the read that follows a change, run as before. Operators set the window with `Indexing:IdleAfter`.
- When a licence has ended TodoWerk says so and stops, for that person alone — their colleagues carry on working.
- When TodoWerk cannot reach its licensing service it keeps working for a day, then says it could not check rather than claiming your access ended, and recovers on its own as soon as it can.
- Deleting everything TodoWerk holds about you still works after your access has ended.
- The invitation to approve TodoWerk for a whole organisation is now offered only to people whose licence covers the organisation.
- Running TodoWerk on your own infrastructure stays free and contacts nobody: no licence, no check, nothing sent anywhere.
- Every attempt to approve TodoWerk for an organisation now leaves a line in the server's log saying how it ended, so an operator can answer "did last night's approval work?" without asking the administrator who tried.
- An organisation using more seats than its licence was sold now leaves a line in the server's log, with both figures on it. Nobody is stopped and every seat is still counted — the licensing service calls this soft, and so does TodoWerk.
- The Teams App Package points its website link at `/about` rather than at the sign-in page, and names `/administrators` as its documentation for administrators.
- Inside Teams, the tab now shows Teams's own loading indicator until it is ready, instead of an empty frame.
- The hashtag table is the first thing on the screen again: the per-list freshness breakdown folds away until a scan is running or a list has failed, and the history of your changes moves below the table.
- Changing the sort, the filter or the page now shows that something is happening, and the page opens with the shape of the table rather than a spinner.
- The dialog that confirms a change marks the hashtag it puts into each title and strikes the one it takes out, says at once when a new spelling cannot be one, and puts the number of tasks on the button.
- "Sign out" and "Delete my data" live in a menu under your name, and the way to keep your data is the highlighted choice when you are asked to confirm deleting it.
- The detail panel stays beside the row you selected as you scroll, and offers one button that follows the selection: change this hashtag, or combine the ones selected.
- Counts are right-aligned, columns are sized to what they hold and can be dragged, the flags carry glyphs, and the pager says which rows are on screen.

**Licensing has been walked against a real licensing service.** Every licensing test in this
repository answers from a fake built to the licensing service's written contract, which proves that
TodoWerk implements what is written, not that the two ends agree. A walk against a licensing service
built from source and run locally found them agreeing on every cell it covered, and found the one
defect written up under Fixed above.

**A polish pass over the Workbench, from a design review of the client.** The review's three
findings that blocked professional quality were that the inventory table — which CONTEXT.md calls
the product — sat below a freshness panel listing every task list permanently, a licence banner,
up to four message bars and the change queue; that a sort, filter or page change gave no feedback
for the whole round trip, so "Page 2 of 7" was read over page 1's rows; and that the one
destructive action in the product wore the brand's primary button. All three are fixed inside
[ADR-0004](adr/0004-fluent-ui-v9.md)'s rules — component props and theme tokens, no per-component
style surgery: the per-list breakdown is a disclosure that unfolds by itself while scanning or when
a list failed, `useApiQuery` now says whether a request is in flight and whether the rows on screen
are stale, and the erasure dialog's primary button is the one that keeps the data. The rest of the
review followed: a session menu under the person's name, column sizing and right-aligned counts,
a sticky detail panel with one contextual button, marks and strikes in the confirm preview with
live validation of the spelling, prose capped at a readable measure, medium-sized controls where
the tab reaches phones, a Skeleton in place of the first spinner, message bars that animate in a
group and can be dismissed, and `@fluentui/react-icons` declared as the direct dependency it
already was transitively. The contrast suite gained the two token pairs the pass introduced.

**TodoWerk now documents itself for the two readers who are not contributors.**
The Teams admin center shows an app's `websiteUrl` as its support link, and TodoWerk's was the
sign-in page; `publisherDocsUrl` — "documentation for the admins to use to understand, allow,
configure, and rollout the app" — had nothing to point at, and the one explanation of what Tenant
Consent does not grant was an ADR written for contributors. Two pages fix that, on the renderer the
legal pages already had: `handbook/about.md` and `handbook/administrators.md`, compiled in and
served at `/about` and `/administrators` by every deployment, Self-Host included. The About Page
names the operator from `Legal:Operator` and links onward only where a new `Handbook`
configuration section names a target, so a Self-Host that names nothing serves a page that is true;
the Administrator's Guide reads the statistics floor, the undo window and the dormancy window from
the running configuration rather than stating defaults, and an integration test proves it. The
Guide — the half for the person using the product, with screenshots — is deliberately not here: by
[ADR-0013](adr/0013-the-handbook-is-split-by-kinship.md) it is published separately, and the
application learns its address from `Handbook:GuideUrl` or not at all. `scripts/Seed-HandbookScreenshots.ps1` seeds
the To Do lists its pictures are taken of.

**`dotnet test` was never broken on Windows; the flag we documented was.** CONTRIBUTING and
the README both warned that `dotnet test` reports "Zero tests ran" on a Windows workstation and told
contributors to run each suite's executable instead. The cause was `--nologo`, a VSTest-era flag
that Microsoft.Testing.Platform does not accept and `dotnet test` does not list. The SDK forwards an
unrecognised option to the test host, the host rejects it, and the rejection is reported as
`Zero tests ran` with exit code 5 — which reads, from outside, exactly like a repository containing
no tests. Every reproduction carried the flag, including the ones that appeared to rule out
filtering; CI passed throughout because `pr-validation.yml` never passed it. Without it,
`dotnet test TodoWerk.slnx` runs all 410 tests on the same workstation, and `--filter-class` works
where the executable's `-class` had been documented as the only option. Both entry documents now
carry the rule that actually helps — pass no VSTest-era flags, and suspect the argument list first
when a run reports zero tests.

**The company has one spelling, and it is `CloudWerk`.** The company name is spelled CloudWerk
everywhere; `developer.name` now reads `CloudWerk GmbH`, and so do the CLA, the terms, the privacy
notice, the README and the assembly copyright. CONTEXT.md carries the rule: the name is `CloudWerk`
wherever a reader meets it, while lowercase `cloudwerk` stays correct in code, domains and the
GitHub organisation slug, none of which anybody reads as a company name.

**A deployment can be asked which build it is.** `/version` answers `{"version", "commit"}` from the
informational version the compiler stamps on the assembly, anonymously — everybody who needs it
arrives without a session, and TodoWerk's history is public, so the commit tells a stranger only
which public revision they are talking to. `/health` already said whether a deployment was well;
nothing said *what* was deployed, and the image is pulled by tag rather than by digest. CI has
always published `sha-<commit>` alongside `latest`, so the two now meet: the value at `/version` is
the tag of the image that is running, and the runbook says to deploy that tag rather than `latest`.
This exists so an operator can see which build is running — when "the same build as yesterday" and
"a different build" look identical from outside, an unchanged deployment is indistinguishable from
a fixed one.

**The authority a deployment signs in against is now written down** (§8 of the [Self-Host runbook](runbooks/deploying-a-self-host.md)).
`EntraId:TenantId` defaults to `organizations` and a deployment that overrides it with its own
tenant id serves that tenant and rejects every other one, with `AADSTS50020` shown to the person
signing in and logged nowhere on the operator's side. The app registration's Supported account
types does not override it, healthy deployments show no sign of it, and everyone in the operator's
own tenant signs in perfectly.

**Terms of use, and a privacy notice the application serves itself.** `TERMS.md` joins `PRIVACY.md`
at the repository root, and TodoWerk now renders both at `/legal/terms` and `/legal/privacy` —
anonymously, because the people who read them are deciding whether to sign in and the Microsoft
reviewers who read them never will. They are the documents themselves rather than a copy: both are
compiled into the application and rendered from their markdown, so the page a consent dialog links
to cannot drift from the file in the repository. The Teams manifest's `developer.termsOfUseUrl` no
longer points at `LICENSE` — AGPL-3.0 grants rights over source code and says nothing about a
service — and `privacyUrl` no longer points at a blob URL that moves the day a file is renamed;
both now follow the deployment's own host. Who the terms name as the Operator is configuration
(`Legal:Operator`, `Legal:OperatorContact`, `Legal:GoverningLaw`), because the same terms are served
by CloudWerk's deployment and by every Self-Host and the one thing that must differ between them is
who the person signing in is agreeing with. The new app icon, described below, landed alongside.

**M4 — Teams tab** is complete and walked against a real tenant. TodoWerk renders inside Microsoft
Teams as a personal tab: the Workbench, signed in silently in a tenant that has granted Tenant
Consent and by one popup in a tenant that has not, following the Teams theme including dark and high
contrast. An App Package can be built from one source manifest for the Store, for ManagementPortal,
and by a Self-Host for its own deployment. The walk loaded the tab in Teams on the desktop, on the
web and on a real Android device, uploaded the package to a real tenant's catalog, and previewed,
ran and undid a Change against a real mailbox through the tab — fourteen cells, all passing. The one
defect it reported was reproduced and dismissed the same day as not a defect: the window that looked
like a popup nobody opened was Teams' own post-Add confirmation dialog, and the tab holds no code
that could have opened one. This is the first of the four walks to find nothing real, which is worth
recording precisely because the previous three each found something (ids compared without regard to
case, a silent title truncation, a decline recorded as an approval).

Two cells of it could not be performed, and are gaps rather than passes. **Teams on the web in
Safari** was not walked: it has not been verified on macOS or iOS, so ADR-0010's account of the
refused cookie, and the card built for it, remain predictions that have never run in the browser
they exist for. **The first-ever user of a tenant that has not granted Tenant Consent** was not
walked either, for want of a second tenant; that is the first run of every new tenant. Both are in
[docs/status.md](status.md), written down rather than softened.

The review pass before it was committed found that the tab did not work in the case it exists for:
MSAL files an on-behalf-of result in a different partition of its token cache from an
authorization-code one, so a tenant that *had* granted Tenant Consent would have watched the tab
sign somebody in and then fail every Graph call with "reconnect required". Fixed by naming the
session key both ends can compute, and pinned by two tests that ask the tab's session — and then a
background scan — to actually read a task list.

**M3 — Tenant Consent & Tenant Overview** is complete and verified against a real tenant: an
administrator can approve TodoWerk for a whole organisation, anybody in it can see how much it
is used, and anybody can have everything it holds about them destroyed. The Org Mode planned for this milestone — app-only
consent, an Inclusion List, tenant-wide management of other people's Hashtags — will not be
built, and [ADR-0008](adr/0008-tenant-consent-is-delegated.md) records why. Tenant Consent has
since been walked against a real tenant: the redirect URI registered, an approval taken end to end
and visible in Entra ID, a second user signed in with no consent prompt, and the decline path observed for real — which found the
defect recorded below. Still unreleased.

**M2 — Rename & merge** is complete before it, and verified against a real mailbox: TodoWerk
now writes. A Change is previewed as an exact set of old-title/new-title pairs, confirmed,
queued, run one PATCH at a time against Microsoft To Do, journaled, and undoable as a whole
for 30 days. Its write path has since been
walked against a real mailbox — the first TodoWerk writes ever to reach real data — which
confirmed the contract and found the truncation defect recorded below.

**M1 — Index & inventory** is code-complete before it: hashtags are extracted from Microsoft
To Do, kept current by a background delta sync, and shown in the Workbench inventory table
with their usage counts and issue flags. The live-tenant verification run
has been walked against a real tenant, and all three defects it turned up are fixed: ids
compared without regard to case, a write failure reported as a read failure, and a connection
string the driver could not parse that still let the application start. What that run never
did is watch paging or throttling against live Graph — the tenant answered every request and rate-limited
nothing — so that stays a gap in [docs/status.md](status.md) rather than a closed
question.

### Added

- **A Microsoft Teams tab.** The Workbench, inside Teams, in personal scope — one person's
  hashtags, which is what the product is; there is no channel or group tab and there will not be
  one. Signing in is the Teams identity: the tab acquires a Teams token, the backend exchanges it
  on-behalf-of for the Graph scopes TodoWerk already uses, and what the browser ends up holding is
  the same `todowerk.session` cookie the browser SPA gets — no token reaches the client on any path,
  exactly as [ADR-0002](adr/0002-backend-held-tokens.md) said the Teams path would work from M0.
  In a tenant that has granted Tenant Consent nobody is prompted at all, which is what makes M3 the
  milestone that decides whether M4 feels silent. In a tenant that has not, the tab says so and
  offers a button that runs TodoWerk's own server-side OpenID Connect flow in a Teams popup —
  no MSAL.js, no implicit flow, no redirect URI type that did not already exist.
- **The tab follows the Teams theme**, including dark and high contrast, and re-themes without a
  reload when somebody changes it. The theme arrives in the tab's own URL through the manifest's
  placeholders, so the first paint is already right rather than flashing light and correcting. The
  browser Workbench keeps the CloudWerk brand theme and is unchanged; dark mode for the browser is a
  real feature with real scope and belongs to M5
  ([ADR-0004 amendment](adr/0004-fluent-ui-v9.md)).
- **The four surfaces that end at a Microsoft sign-in page each get an answer.** Sign-out is
  not offered in the tab at all — the identity there is the Teams identity, and signing out of
  TodoWerk while staying signed into Teams is a state the next tab load silently undoes. Erasure
  *is* offered, because it is an obligation rather than a convenience: it destroys the data with a
  request from the tab and ends the Microsoft session in a popup. Starting Tenant Consent and
  reconnecting after a refresh token expires both run through the same popup. One "Open TodoWerk in
  your browser" control serves everything else.
- **The blocked third-party cookie is named rather than left as a blank screen.** Safari
  refuses TodoWerk's unpartitioned session cookie inside the Teams frame outright, and the only
  trace of it is a pair of responses: an on-behalf-of exchange that succeeded, and a 401 to the
  request immediately after it. The tab detects exactly that pair, says in a sentence that this
  browser will not keep the session inside Teams, offers to open TodoWerk in a browser instead, and
  does not retry — retrying produces the same answer every time. Mitigated rather than fixed, and
  decided that way with the trade-off in view ([ADR-0010](adr/0010-teams-tab-session-and-framing.md)).
- **An operator can find that condition in the log, and it names the browser.** Only the tab
  ever sees both halves of the pair, so the confirming request says what it is and a 401 answered to
  it is logged as the browser refusing the cookie rather than as somebody arriving signed out.
  Sixty a minute, and the sixty-first line says the rest of the minute is missing rather than going
  quiet.
- **One log line now records a user agent, and the privacy documents say so.** It is the only
  place TodoWerk writes one, it is written at the moment TodoWerk does not know who the caller is,
  and nothing correlates it with anybody — but "no user agent" appeared in
  [ADR-0009](adr/0009-what-todowerk-stores-about-a-person.md) without a qualifier and would fairly
  be read as covering everything rather than the membership record. The ADR gains an amendment, and
  [PRIVACY.md](../PRIVACY.md) gains a section of its own for what reaches a server log — the
  document somebody actually reads. It is there because *which* clients refuse the cookie is the
  question the mitigation leaves open, and a card on somebody's screen in another country cannot
  answer it.
- **An App Package, built from one source manifest.** One template and one PowerShell 7
  script produce every deployment's package, so the packages cannot drift in the ways that matter.
  The template in this repository holds placeholders rather than any deployment's values
  ([ADR-0011](adr/0011-two-app-packages-and-a-template.md)).
- **Install documentation, and a convenience script nothing requires**
  ([docs/runbooks/teams-app-install.md](runbooks/teams-app-install.md)). What an administrator is
  told to do is manual — upload in the Teams admin center, or install from the Store once the
  listing exists. `Publish-TodoWerkTeamsApp.ps1` does the same upload over raw Microsoft Graph with
  no PowerShell module to install first, and signs an administrator in interactively every time:
  publishing to an app catalog is a delegated-only permission, so there is no unattended path and
  none can be added.
- **Tenant Consent: one approval for the whole organisation.** An administrator reaches
  Microsoft's admin-consent endpoint from inside TodoWerk, approves the delegated grant TodoWerk
  already asks each person for, and from then on nobody in that tenant is prompted at sign-in.
  TodoWerk gains no new access: no application permission, no directory permission, no app-only
  credential, and it still reaches no mailbox whose owner has not signed in. It cannot see a grant
  made in the Entra portal, so every surface says the grant was or was not made *through TodoWerk*
  rather than claiming to know — an administrator who approved elsewhere is not told they have not
  approved. The callback is not believed on its own parameters: Microsoft's documentation warns the
  `tenant` it carries can be forged to impersonate a response, so the flow starts with a state
  TodoWerk remembers in a protected short-lived cookie and records nothing unless the state matches
  and names the same tenant. Anything short of success records nothing at all. The invitation is
  subject to no floor, which is how the feature gets found in the tenants most likely to need it.
- **The Tenant Overview**, on a route of its own rather than a panel on the Workbench: how
  many people have signed in, how many did so inside each of three trailing windows, when the first
  of them started, how many Occurrences they hold between them, and the average per person. Counts
  computed from the database at request time rather than read from a maintained total — a second
  source of truth for a figure nobody makes decisions on would be wrong exactly where that is
  hardest to notice. It never names a person, never shows a per-person row, and never lists or
  compares Hashtags across people. Any signed-in user of the tenant may see it: no role check, no
  administrator concept, no permission-denied path. Below a configurable floor of five people the
  statistics are absent from the response as well as from the screen, with no placeholder and no
  explanation, because nothing should invite a reader to misread a promise as a defect.
- **The Tenant Member record**, and the Onboarding module across all four layers. A tenant, an
  Entra object id, a first sign-in and a last one — no name, no UPN, no address, no user agent, and
  no history between the two moments. Written at interactive sign-in and nowhere else: a scan
  running on a timer is not somebody using the product, and that is enforced by where the write
  lives rather than by a flag somebody could set wrongly. The tenant is the `tid` claim, never a
  domain lifted off a UPN — one tenant holds several verified domains and a guest's UPN names
  another organisation entirely. An architecture test pins the columns, so a fourth one cannot
  arrive quietly and make the privacy notice wrong.
- **Erasure, and a retention rule.** "Delete my data" sits beside "Sign out", confirms
  first, and destroys the index and its Occurrences, the per-list sync state, the Changes with their
  plans and journals, and the token cache entry — then anonymises the membership row in place, so
  the tenant's cumulative count survives somebody exercising a right with nobody identifiable left
  in it. Nothing records that somebody left and no screen shows a churn figure. Twelve months
  without a sign-in does the same thing down the same code path on a background worker, which is
  also what an administrator revoking TodoWerk in Entra ID amounts to: nobody can sign in
  afterwards, so everybody goes dormant and the tenant clears itself. The sweep is restart-safe by
  construction rather than by a checkpoint — it only finds rows that still name somebody, so nobody
  is erased twice and nobody is skipped. Each module purges its own rows behind a port declared in
  the shared application layer, so Onboarding names no Indexing or Changes type and the
  module-boundary tests hold without a new documented exception.
- **A real sign-in in the integration harness.** A test drives the challenge, the
  authorization request and the callback through the application's own OpenID Connect handler,
  carrying the correlation cookie and repeating the nonce in an id token signed by a key the host is
  told to trust, and ends holding a session the application accepts on a later request. Two more
  prove that a callback with no correlation cookie, and one carrying a state this application never
  issued, produce no session. The cookie-minting helper stays for tests that are about something
  else. This was the prefactor the rest of M3 rested on: every later ticket's central assertion is
  "somebody really signed in, therefore ...", and the suite could not previously express it.
  Walking it also settled, by measurement, that ASP.NET Core does not compare the nonce on pure
  authorization-code flow — it mints one, sends it and reads its cookie back, and a token repeating
  another flow's nonce or carrying none still authenticates. A test pins that weaker truth rather
  than hiding it, so nobody reads the nonce in the traffic and assumes it is load-bearing; what
  protects this callback is the correlation cookie, PKCE, and a single-use code redeemed
  server-side with the client secret.
- **A privacy notice.** `PRIVACY.md` states what is stored about a person separately from what
  is stored as a consequence of using the product, gives each retention period and what triggers
  deletion, describes the erasure route somebody can take themselves, says what the Tenant Overview
  discloses to colleagues and what it deliberately does not, and separates the Hosted Service from
  Self-Host — saying plainly that CloudWerk receives and holds nothing in the second. Last in the
  milestone on purpose: a notice promising erasure should not ship before erasure does.
- **Rename, merge and casing clean-up, as one mechanism.**
  A Change carries a set of source Hashtags and one target Spelling; which of the three
  operations it is gets read off its shape rather than chosen. Previewing it shows the exact
  titles that would change and names the lists left out because they have never been read end
  to end — ADR-0003 forbids writing against a half-scanned list, and a silently short plan is
  what that sentence exists to prevent. Confirming persists the plan, so a Change covers what
  was confirmed and cannot grow while it waits. Refusals each say their own reason: a second
  Change while one is in flight, a target that does not round-trip through the extractor, a
  plan past the configurable 1,000-task ceiling, and a merge that has not been confirmed
  twice. The run re-reads every task immediately before writing it, because `todoTask` has no
  ETag and `Update todoTask` takes no `If-Match`; the rewrite is applied to what came back, and
  a task whose Hashtag is gone by then is skipped rather than failed. Every write is journaled
  with the title actually read, which is what makes undo restore what was really there. Undo
  runs as a Change in the other direction — whole-Change, one level deep, inside 30 days — and
  leaves alone any task somebody edited after TodoWerk changed it. Cancel stops after the task
  in flight; there is no pause, because a paused Change holding a lease is a wedged row waiting
  to be found. The Workbench grew the surfaces for all of it: multi-select on the inventory
  table, a confirmation dialog with room for a thousand pairs and the merge warning, and the
  change queue alongside with per-task progress, Cancel, and 30 days of history with Undo.
- **A change queue of its own**, in a `Changes` module with its own table and its own
  worker — claim, lease, renewal, release on shutdown, retention purge, deliberately the same
  shape as the scan queue rather than generalised into something both must fit. One Change per
  user at a time and never alongside a scan for the same user, both enforced inside the claim
  query rather than in a method that can be bypassed. A queued Change preempts a running scan
  at a page boundary — once, so a run of Changes cannot starve somebody's index — and the scan
  resumes having lost nothing. Its poll interval is three seconds rather than the scan
  worker's fifteen, because a confirmed Change is meant to start immediately.
- **Failure codes instead of matched sentences.** The Change, the scan queue and the
  per-list scan state each carry a code — reconnect required, throttled, unavailable,
  unknown — alongside the sentence a reader sees. The Workbench keys its "Sign in again"
  offer off the code; it used to decide by looking for a phrase in the server's prose.
  ADR-0007 makes that load-bearing rather than cosmetic: widening the scope means every
  existing user meets the reconnect path on upgrade.
- **A client test suite**, promised in CONTRIBUTING since M1 and now real: Vitest and Testing
  Library over the state the client holds before the server has confirmed it — a debounced
  preview, a preview discarded because the target moved on, the merge verdict sent back as the
  server computed it, and the entry point that stays shut below two selected rows.
- The decisions M2 rests on, settled before its first ticket rather than during it:
  [ADR-0006](adr/0006-what-a-change-is.md) defines a **Change** as a set of source Hashtags and
  one target Spelling — so rename, merge and casing clean-up are one mechanism whose name is
  read off its shape — and fixes how one reaches Microsoft To Do: only the Hashtag's own text
  is rewritten, each task is re-read immediately before it is written because `todoTask` offers
  no ETag to compare against, and a journal of what was actually written makes the whole Change
  undoable for 30 days. [ADR-0007](adr/0007-one-consent-grant.md) reverses the incremental-consent
  plan: sign-in asks once for `Tasks.ReadWrite` and nothing narrower, because the worker that
  performs a write has no browser to prompt through. `CONTEXT.md` gains `Rename`, `Change` and
  `Change Journal`, and `Normalise Casing` now says outright that two Hashtags which merely look
  alike are a Merge.
- The Hashtag Manager's first working screen: the Workbench inventory table. A Fluent
  UI v9 DataGrid over the inventory query, with tag, task count, lists, last used and flags —
  sorting and paging drive the server query rather than a client-side copy. Issue flags read
  as advice rather than alarms, index freshness and per-list scan progress live in the chrome
  with a re-scan control, and the empty, scanning and first-run states are designed rather
  than left to whatever a grid does with zero rows. Read-only: selection and the detail panel
  exist so that M2's rename and merge land into a layout that already holds them.
- The inventory read model, computed in SQL. Per Hashtag: how many tasks carry it, how
  many lists it reaches, when it was last used, and every Spelling observed. Three issue
  flags, each with its own tests — casing variants, near-duplicates, and stale, the last
  against a configurable threshold rather than a number in the code. The near-duplicate pass
  is deliberately conservative, since a false pair invites a merge of two Hashtags that were
  never the same.
- A hashtag index the background scan fills from Microsoft Graph. The first pass
  and every incremental one take the same path through Graph's delta endpoint, so the
  expensive first read leaves behind the token that makes the next one cheap. It runs off a
  queue in SQL rather than inside a request, records progress per list as pages arrive, waits
  out throttling on Graph's terms, and answers an expired delta token by reading that list in
  full without anyone intervening. A list that fails, fails alone. Freshness and a manual
  re-scan are exposed through the API, and asking for a re-scan twice queues one scan.
- The hashtag extractor and the index model: a pure extractor following the grammar
  ADR-0005 settled, the folded key that decides identity, and four tables — indexed tasks,
  Hashtag Occurrences, per-list sync state, and the scan queue. The extractor's unit tests
  carry the probe script's case table probe for probe, including the cases the ADR marks
  uncertain, so a filled-in probe result can be walked against them line by line.
- The server-side MSAL token cache is now durable: a distributed cache backed by the
  ADR-0003 database replaces the memory-backed one, so a deploy no longer costs every
  signed-in user a fresh sign-in, and M1's background scan can hold a refresh token with
  no browser attached. The `dbo.TokenCache` table arrives by hand-written migration
  in exactly the schema the SQL cache expects — deliberately outside the EF model, since
  its rows are the cache implementation's, not the domain's. Entries expire 90 days after
  their last use, Entra ID's refresh-token inactivity window, and sign-out evicts the
  user's entry. An integration test proves the whole claim against real SQL Server: a
  refresh token seeded through the application's own cache provider, the host disposed,
  and a second host redeeming that token for a Graph read against a faked cloud.
- A definition of what a Hashtag is, ahead of the index that depends on it
  ([ADR-0005](adr/0005-what-a-hashtag-is.md)). Identity ignores casing, so `#Work` and
  `#work` are one Hashtag with two Spellings — which makes casing clean-up a distinct
  operation from merge, and adds `Spelling`, `Canonical Spelling`, `Occurrence`,
  `Normalise Casing` and `Merge` to the glossary. The folded key is computed in C# and
  stored under a binary collation, because SQL Server's default collation and .NET
  disagree about whether `Straße` equals `Strasse` and whether a precomposed `ü` equals a
  decomposed one; either disagreement is a duplicate-key violation on German data.
  Implementing the extractor corrected two things in that ADR: the fold runs upwards rather
  than downwards, which changes nothing in its measured table but does fold Greek final
  sigma; and a `#` directly after an opening bracket turns out to be a case the grammar
  answers by accident rather than on evidence, so it is now recorded as uncertain.
- `scripts/Probe-HashtagGrammar.ps1`, which seeds a scratch To Do list with the grammar
  cases Microsoft documents nowhere — non-ASCII letters, trailing punctuation, `C#`,
  digit-only tags, note bodies — reports what Graph stored codepoint by codepoint, and
  emits a checklist to answer against real clients.
- The persistence foundation: EF Core over SQL Server, `TodoWerkDbContext`, and a
  baseline migration that creates the database and nothing else
  ([ADR-0003](adr/0003-single-sql-store-own-index.md)). Entity configurations live with
  their vertical module rather than in one central file. Architecture tests fail the
  build for a persisted entity that does not carry a tenant id and a user id, and for a
  model change with no migration behind it.
- Applying migrations is an explicit step in every environment — never `EnsureCreated`,
  never at startup. Integration tests that need a database run against real SQL Server,
  creating and dropping their own database per run.
- Everything a public repository needs before it can accept a stranger's pull request:
  `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `CLA.md` with a signing bot,
  issue forms, a pull request template, Dependabot, `CODEOWNERS`, and this changelog.
- `.gitattributes` normalising line endings to LF, so `dotnet format` reaches the same
  verdict on a Windows workstation as on the Linux CI runner.
- CI now verifies formatting (`dotnet format --verify-no-changes`) rather than only
  asking contributors to.

### Changed

- **TodoWerk's publisher is verified on the Microsoft sign-in screen.** The consent
  dialog everybody meets the first time they sign in now names the company with a blue
  verified badge where it used to say "Unverified". Nothing in TodoWerk earned it — it is a
  Microsoft publisher verification held against the app registration — but in an organisation whose
  administrator has turned on risk-based step-up consent, it is the difference between being
  able to sign in and not.

- **The app icon follows the CloudWerk house style.** A tile with a band along the bottom and a
  single white hashtag set in Georgia Bold — in Teams, in the Microsoft 365 stores, and beside
  TodoWerk's name on the sign-in screen. `packaging/render-icons.py` draws all three sizes from one
  geometry so they cannot drift apart.

- **Org Mode is cancelled, and the documents that described it are corrected** (ADR-0008).
  App-only consent carrying `Tasks.Read.All` / `Tasks.ReadWrite.All`, an
  admin-curated Inclusion List of whose tasks were indexed, and central management of other
  people's Hashtags will not be built. The deciding finding is that Exchange App RBAC and
  Application Access Policies scope Mail, Calendars, Contacts and Mailbox settings but *not*
  Tasks — so an app-only grant could never have been narrowed to a subset of mailboxes, and the
  Inclusion List was unenforceable in principle rather than merely unbuilt. TodoWerk would have
  held a credential able to read every task title in the tenant, with a list in its own database
  as the only thing between that credential and the mailboxes of people who never asked.
  Delegated Tenant Consent has the property the Inclusion List was invented to fake: TodoWerk can
  reach exactly the people who signed in, because reaching somebody requires a token issued to
  them. ADR-0007's Org Mode paragraph, ADR-0002's two mentions, `SECURITY.md` and the M3 roadmap
  row are corrected accordingly, and `CONTEXT.md` had already dropped Org Mode, Personal Mode and
  the Inclusion List in favour of Tenant Consent, Tenant Member and Tenant Overview. There is no
  "mode" any more: one product, which a tenant may or may not have approved centrally.
- **What TodoWerk stores about a person is now a recorded decision** (ADR-0009). Two timestamps rather than a row per sign-in, because a row per sign-in is a record
  of somebody's working patterns and the questions being asked are trailing windows that two
  moments answer exactly. Anonymisation rather than deletion on cancellation, because deleting
  the row makes the cumulative count a measure of how many people have not objected, and keeping
  the object id leaves somebody who asked to be forgotten identifiable. The retention window and
  the overview's longest activity window are one setting rather than two that agree today.
- The persistence convention that every row carries a tenant and a user now allows an entity to
  be tenant-scoped, named one at a time in the test's own list. The Tenant Consent grant is the
  only member: an administrator approves for the organisation, TodoWerk deliberately does not
  record which administrator clicked, and inventing a user id for that row would be exactly the
  invented value the convention exists to prevent. The comment that justified the rule by "M3
  makes the index tenant-wide" is corrected too — M3 did not.
- The architecture tests now fail when a module named in the boundary check holds no types in a
  layer. A boundary check over an empty namespace passes for the wrong reason, and without this
  the Onboarding module would have been reported clean whether or not it reached into Indexing.
- **Sign-in now asks for `Tasks.ReadWrite` and nothing narrower** (ADR-0007). One prompt,
  once, covering everything TodoWerk does — including for a user who only ever reads their
  inventory. The incremental-consent plan `GraphScopes` carried through M0 and M1 is deleted
  rather than implemented: the worker that performs a write has no browser to prompt through,
  so "ask when we need it" would have meant asking at the confirmation click on a destructive
  operation. Cache entries issued under the old narrower scope cannot silently widen — MSAL
  raises `MsalUiRequiredException`, the gateway maps it to reconnect-required, and affected
  users sign in once more with their background syncs stopped until they do. The app
  registration steps in CONTRIBUTING and the permission list in `SECURITY.md` say so plainly.
- Three things left the `Indexing` module so a second module could use them without reaching
  into it: the Hashtag grammar (`TodoWerk.Domain.Hashtags`), the Graph gateway
  (`TodoWerk.Infrastructure.Graph`), and `IIndexScanScheduler` with the user it acts for
  (`TodoWerk.Application.Abstractions`). An architecture test now fails the build if any of
  those shared namespaces grows a dependency on a module — which would put every module back
  in touch with every other through the back door. The gateway's throttle budget moved with it,
  from the `Indexing` configuration section to a `Graph` one, because a Change waits out the
  same throttling and a retry budget filed under `Indexing` would read as though it did not
  apply to writes.

### Fixed

- **The CLA check had never worked.** The action records signatures on a `cla-signatures`
  branch and does not create it; the branch had never existed, so the check failed with "Branch
  cla-signatures not found" and told the contributor they had to sign — which reads like their
  problem and was not. Nothing had exercised it before: every earlier pull request was
  Dependabot's, and the allowlist skips those, so the repository's first human pull request was
  also the first to meet a gate that could not have passed. The branch exists now, the workflow
  carries the instructions for recreating it, and the check passes.
- **A rename could silently mangle a Hashtag in a way undo could never take back.** Microsoft
  To Do keeps 255 characters of a task title. It does not refuse a longer one: it answers success
  and stores the first 252 followed by three full stops. Observed live — a 279-character rewrite
  came back 255 characters long with `#ZzExtendedTagNameForLengthProbe` cut down to `#ZzEx...` —
  while TodoWerk reported the write as done. The journal then held a title that never existed in
  the mailbox, so undo compared, found no match, and skipped: the corruption outlived the only
  thing that could reverse it. A Change whose rewritten title would pass 255 characters now skips
  that task with a reason rather than sending it, which is the same rule the 512-character columns
  already imposed, at the lower bound the service actually enforces. The fake Graph the tests run
  against truncates the same way now — it accepted whatever it was handed before, which is why only
  a real mailbox could show this.
- **A declined Tenant Consent was recorded as an approval.** The first live walk against a
  real tenant observed what no invented test parameter had guessed: Microsoft's admin-consent
  endpoint redirects a decline with `admin_consent=True` *alongside* `error=consent_required`
  (AADSTS65004) and no `tenant` parameter at all — the flag names which flow this was, not what
  the administrator decided. The callback checked only the flag and the state, so an administrator
  clicking Cancel was recorded as having approved, and the recorded note then hid the approval
  invitation from the whole tenant with only the "approve again" route as a way back. The callback
  now records nothing when the redirect carries any `error`, and no longer believes a success that
  omits the tenant — Microsoft's real success names it, also observed byte-exact. The invented
  decline parameters in the tests are replaced by the captured shapes.
- Five more from a second adversarial pass over the finished milestone. The statistics floor was
  measured against the cumulative member count — which by design keeps the forgotten — so a tenant
  of six where five had erased themselves passed a floor of five while every figure on screen
  described the one identifiable colleague left; the floor and the average's denominator now use
  the rows that still name somebody. A tenant with no membership rows at all (reachable: the
  sign-in write is logged rather than fatal) could answer its overview with a 500, because a
  non-nullable MIN materialised from an empty aggregate throws — projected nullable now, with the
  empty tenant answered like any under-floor one. The retention sweep cleared a backlog at one
  batch per hourly tick — fifty people an hour against a deadline measured in months — and now
  drains batches back to back while full ones keep coming, up to a per-tick bound, continuing on
  found rather than forgotten so one unfinishable person cannot stall the queue behind them. And two tests could not fail: a last-sign-in assertion using
  `>=` under one clock, satisfied even by a no-op update, and a payload-leak assertion hunting the
  word "colleague" in a response whose seeds only ever wrote "seeded-". Two things the same pass
  found were documented rather than fixed, in status.md and ADR-0009: a guest's token-cache entry
  survives their erasure (unusable, and gone within 90 days), and a person who returns after
  erasure is counted anew, because recognising them would take the identifier erasure removes.
- Eight defects found by the review pass over M3, before any of it was committed. A signed-in member
  of a tenant could record a Tenant Consent grant nobody made and permanently hide the approval
  invitation from their whole organisation — the admin-consent flow returns no signed response, so
  the record is now presented as TodoWerk's own note with an "approve again" route beside it rather
  than as a settled state. Two would have left data behind after an erasure: the scan queue was
  purged once rather than on every pass, so a row the sync scheduler wrote back could outlive the
  purge and then be unreachable once the membership record was anonymised; and a purge that exhausted
  its retry bound while still deleting reported success, so the eraser anonymised on top of a residue
  and told the person they had been forgotten — it now refuses to anonymise and leaves the erasure
  retryable. The rest: a blanket `catch (DbUpdateException)` in two stores that read a deadlock as
  "somebody else won the race" and reported a write that never happened; a `SweepInterval` that
  validation allowed up to a year while `PeriodicTimer` throws past about 49 days, so a setting that
  passed startup validation crashed the host at boot; a browser navigation behind the API
  authorization policy, which answers a lapsed session with problem-JSON in the tab; and a nav link
  styled on a wrapper rather than on the anchor `NavLink` renders, so the brand header would have
  shown browser-blue links.
- The test that guards "a misconfigured deployment fails at startup" no longer decides its
  own verdict by a race. It booted the whole application and waited for
  `CreateClient` to throw, but under `WebApplicationFactory` the application's entry point
  runs on a thread of its own and startup validation runs there, after the built host has
  already been handed back — and `RunAsync` disposes that host on its way out, while the
  test thread is reaching into the same container. What `CreateClient` raises is settled by
  which thread got there first: 300 boots produced one `ObjectDisposedException` from inside
  the framework's own wait, in place of the validation failure the test exists to see. The
  reported symptom is a different one — a host that starts clean, far more often than that —
  and it did not reproduce on this workstation, so whether the two share a cause is open.
  The test no longer depends on the handover either way: it asks the startup validator the
  question the host asks it, on one thread. What that costs is written down in
  [docs/status.md](status.md), because it is not nothing — no test now watches the
  application itself refuse to start.
- A review pass over the whole of M1 — three reviewers by lens, then two adversarial
  passes over the fixes themselves — and everything it turned up. The ones worth naming:
  the sign-in return URL accepted `/\host`, which browsers read as protocol-relative, so
  a genuine sign-in could land on somebody else's page; no column header in the inventory
  table was ever clickable, because Fluent decides that from the comparator's arity and
  every one of them took no arguments; and a hashtag longer than its column poisoned the
  page it arrived in, whose failure handler then replayed the same doomed write, wedging
  that account's scan permanently.
- The scan now holds a lease it renews as it works. Completing, failing and releasing are
  all conditional on still holding it, so a process that lost its row to the abandonment
  timeout finds out rather than writing over the process that took it. Shutdown hands the
  row straight back instead of leaving it frozen for an hour — an hour in which every
  re-scan the user asked for was answered with "already running". A scan that fails before
  it reads a single list no longer re-queues itself on every poll, and finished rows are
  swept after a retention window rather than kept forever.
- A task moved between lists survives the source list's late tombstone, a task mentioned
  twice in one page keeps only its last title's Hashtags, and a delta pass no longer
  reports the number of tasks it touched as the size of the whole index.
- Graph is reached only at Graph's own host, checked after the address is resolved rather
  than before — a stored continuation link that is protocol-relative would otherwise have
  been resolved into another origin with the delegated token attached. A timeout, which
  arrives as a cancellation nobody asked for, now fails one list instead of the scan.
- The inventory answers in one aggregate pass rather than two, over an index that covers
  what it groups; near-duplicate detection is memoised per user and bounded by length, so
  the cost of a page view no longer scales with how long someone's tags are.
- The Workbench says so when a scan request fails — the one button that bootstraps a new
  account — keeps the last good data on screen through a failed poll, debounces search,
  abandons superseded requests, and clamps a page number that a shrinking result set left
  past the end.
- A scan that fails before it reaches a single list — no token, no list of lists — now says so
  on screen, with a way back to sign-in when that is what it needs. Those failures have no
  per-list row to live on, so the Workbench used to report an account that had simply never
  been indexed: the same thing it says to somebody who has never pressed the button, shown to
  somebody who pressed it and got nothing.
- A database missing its migrations is now named as such, shortly after startup, along with
  the command that applies them. It used to announce that the application had started and
  then bury the cause in a background worker's stack trace every fifteen seconds. Nothing
  applies migrations on its own — that stays an explicit step (ADR-0003) — and the check sits
  beside the startup path rather than on it, so a database that is briefly unreachable delays
  nothing.
- Every column holding an id Microsoft Graph issued is now compared byte for byte. Graph
  writes task and list ids as case-sensitive base64, and the database's default collation is
  case-insensitive, so two legitimately different tasks whose ids differed in one letter's case
  were one value to the unique index over them: the second was rejected as a duplicate key, and
  because a delta page is deterministic, every retry of that list failed identically and the
  list never finished indexing again. Found on an ordinary mailbox at some six hundred tasks, which is what
  it looks like structurally — Exchange item ids share long prefixes and vary at the tail. The
  migration re-collates all four columns and puts every existing index back in line for a full
  read, because the tasks a collision swallowed were never written and no delta pass would
  mention them again.
- A list whose scan died on a write is no longer reported as a list that could not be read.
  One catch covers everything that can go wrong per list, and it was recording a fixed
  sentence that named a cause the code cannot know there — so a duplicate key on the insert
  arrived on screen as a read failure and sent an hour of investigation to Graph, to
  permissions, and to the list's well-known kind, none of which were involved. Failures that
  really are Graph's still say so in Graph's own words; this branch, the one where the cause
  is genuinely unknown, now says that instead of inventing one. The Workbench no longer wraps
  the server's reason in "could not be read" either, which asserted the same thing a second
  time and on the client's own authority.
- A connection string the driver cannot parse now fails at startup rather than fifteen seconds
  later. The validator checked that `ConnectionStrings:TodoWerk` was present, which is not
  the same as usable: a mistyped setup step left the word `Delegated` in it, the app announced
  its ports and reported itself healthy, and the only sign of trouble was a background worker
  repeating a stack trace on every tick. It is now parsed by the same builder the provider will
  use later, so "unparseable" fails with the same shape and in the same place as "missing" —
  which is what CONTRIBUTING has been promising all along. Reachability is untouched and stays
  a deliberate non-gate: a well-formed connection string to a server that is down still starts.

### Security

- **There is now one path on which an unauthenticated caller decides something is written to the
  log**, and it is ceilinged by hand because nothing else covers it. The application's rate
  limiter partitions on the signed-in user and runs after authorization — which short-circuits an
  unauthenticated request before it — so a 401 costs an anonymous caller nothing. Sixty diagnostics
  a minute, a truncated user agent, and a path bounded by the route table rather than by the caller;
  a test pins the last of those, because the day somebody adds a route with an unconstrained
  parameter is the day it stops being true. `SECURITY.md` names it as an area where a report is
  worth priority.
- **All three cookies moved to `SameSite=None; Secure`, and framing is relaxed on the Teams tab's
  own path** ([ADR-0010](adr/0010-teams-tab-session-and-framing.md)). Both look like
  regressions and both are decisions. A cookie inside somebody else's iframe is a third-party
  cookie, and one that is not `SameSite=None` is simply absent there — so `todowerk.session`,
  `todowerk.antiforgery` and the JS-readable `XSRF-TOKEN` all changed, and stay **unpartitioned**,
  because the consent popup sets the session cookie in a popped-out window and a partitioned one
  would be invisible to the tab that opened it. The consequence is that cross-site request forgery
  protection no longer has `SameSite` behind it and rests entirely on the antiforgery double-submit
  pair; it still holds — a hostile page can make a browser *send* those cookies, never *read* them —
  but a change that weakens that pair is now a security change rather than a defence-in-depth one.
  Framing is relaxed on `/teams` and nowhere else: every other document, `/` included, still sends
  `frame-ancestors 'none'` and `X-Frame-Options: DENY`, so the Workbench is exactly as unframable as
  it was.
- **The tab's document actively strips `X-Frame-Options`, rather than merely not setting one.**
  ASP.NET Core's own antiforgery adds `X-Frame-Options: SAMEORIGIN` whenever it issues a token and
  finds the header absent, and this application issues one on every safe extensionless GET — the
  tab's own document included. `SAMEORIGIN` refuses Teams exactly as `DENY` would, so without this
  the tab would have rendered as an empty frame with the reason only in a browser console. Found by
  the test written for the header shapes, not by reading the code.
- Approving TodoWerk for a whole organisation grants it no standing access to anybody's mailbox
  (ADR-0008). It is admin consent to the delegated grant each person already makes, so
  TodoWerk still acts as each person with that person's own token; the app registration gains no
  application permission and the deployment gains no app-only credential. The alternative this
  replaced would have put a read of every task title in the tenant behind a single administrator's
  click.
- The Tenant Consent callback records nothing on the strength of its own query string. Microsoft's
  documentation warns that the `tenant` parameter can be updated and sent by bad actors to
  impersonate a response, so the flow carries a state remembered in a Data-Protection-protected,
  path-scoped, 15-minute cookie, and the redirect is only believed when the state matches and names
  the same tenant it was started for. Without the check, anybody could switch off another
  organisation's invitation to approve by fetching one URL.
- Everything TodoWerk holds about somebody now has an end (ADR-0009). Before this
  milestone the index and the journals had no retention rule and no erasure route: indexed task
  titles, and journal entries holding a title twice, stayed indefinitely. Erasure evicts the token
  cache entry first, so nothing can act as somebody while the rest of their data is being deleted,
  and the purges are idempotent so a crash part-way can simply be repeated.
- The Tenant Overview discloses counts and nothing else — no name, no per-person row, no Hashtag
  compared across people — and withholds even those from the response while too few people have
  signed in for a total to be uninformative about individuals. Asserted against the raw response
  body, because a field nobody deserialises is still a field somebody received.
- Token cache entries are encrypted at rest through Data Protection, under the same
  persisted key ring the session cookies already require. A test reads the stored rows
  raw and fails the moment a refresh token appears in them in plaintext.
- Outside Development the key ring must itself be encrypted by a configured certificate.
  It protects the session cookies and the Graph token cache, so a key ring written as
  plain XML — the default on Linux — is every user's refresh token sitting on a disk.
- Security headers on every response, including a content security policy that keeps scripts
  and connections to this origin, forbids framing outright, and allows form posts only here
  and to the identity provider sign-out lands on. A per-user request ceiling on the API.
- Every mutating endpoint is proven to validate its antiforgery token by a test that walks
  the endpoint table, rather than by each one remembering to ask.
- The CLA workflow pins `contributor-assistant/github-action` to a commit SHA. It runs
  with a write-scoped token on every external pull request, where a mutable tag is a
  supply-chain vector.
- Signatures are stored in this repository rather than a remote one, so the workflow
  needs no personal access token.

## M0 — Walking skeleton — 2026-08-08

The whole path from sign-in through Microsoft Graph and back out to the browser,
carrying exactly one read. None of the hashtag features exist.

### Added

- Clean Architecture solution across `Domain`, `Application`, `Infrastructure`, and
  `Web` over a `SharedKernel`, with features cut as vertical modules repeated per
  layer. Architecture tests fail the build when a layer or a module reaches
  somewhere it shouldn't.
- Sign-in with a work or school account through the authorization-code flow against
  a confidential client. Graph tokens and the token cache stay on the server; the
  browser holds a session cookie and nothing else
  ([ADR-0002](adr/0002-backend-held-tokens.md)).
- React and Fluent UI v9 single-page app on the CloudWerk brand theme
  ([ADR-0004](adr/0004-fluent-ui-v9.md)), served from the same host, talking to the
  app's own API through a typed fetch wrapper that turns every error into RFC 7807
  problem details.
- The first real Microsoft Graph read: the signed-in user's To Do task lists.
- Sign-in prompt, sign-out, and reconnect surfaces in the SPA.
- Pull request validation in GitHub Actions: build, unit tests, architecture tests,
  and integration tests that boot the real application and exercise the real
  authentication pipeline without contacting a live tenant.
- Central Package Management, `global.json` pinning the .NET 10 SDK, and `.nvmrc`
  pinning Node 22.

### Fixed

- Antiforgery middleware ran in the wrong order in the pipeline.
- Task lists sorted by culture rather than by ordinal, so ordering varied by
  server locale.
- Missing Entra ID configuration let the application start and then fail on every
  request. It now fails at startup instead.

### Security

- The application refuses to start outside development without a persisted Data
  Protection key ring.
- The checked-in `appsettings.json` ships empty credential values; real values come
  from user-secrets or environment variables only.

## Repository baseline — 2026-08-07

### Added

- AGPL-3.0 license, README, `CONTEXT.md` glossary, and the first four architecture
  decision records
  ([ADR-0001](adr/0001-single-entitlement-authority.md),
  [ADR-0002](adr/0002-backend-held-tokens.md),
  [ADR-0003](adr/0003-single-sql-store-own-index.md),
  [ADR-0004](adr/0004-fluent-ui-v9.md)).

<!-- Keep a Changelog compares the last release tag with HEAD. There is no tag yet, and
     `compare/main...HEAD` would compare main with itself, so this points at the history
     instead. Switch to `compare/v0.1.0...HEAD` once the first release is tagged. -->
[Unreleased]: https://github.com/Cloudwerk/todowerk/commits/main
