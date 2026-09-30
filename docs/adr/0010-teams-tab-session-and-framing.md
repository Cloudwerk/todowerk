# The Teams tab keeps the cookie session, and relaxes framing on its own path only

The Teams tab authenticates with Teams SSO and exchanges that token on-behalf-of server-side, as
[ADR-0002](0002-backend-held-tokens.md) always said it would. What it produces is the same
`todowerk.session` cookie the browser gets — not a bearer token, not a second session scheme. A
cookie inside somebody else's iframe is a third-party cookie, so all three of TodoWerk's cookies
move to `SameSite=None; Secure` and stay **unpartitioned**. Framing is relaxed on the tab's own
document path and nowhere else: `/teams` names the Teams and Microsoft 365 hosts in
`frame-ancestors` and sends no `X-Frame-Options`; every other document keeps `frame-ancestors 'none'`
and `DENY`.

Two of those look like security regressions and are recorded here so that they read as decisions
rather than as somebody's oversight, because the obvious instinct on meeting either is to revert it.

## Why the session is a cookie and not a bearer

The tab could send its SSO token on every request and let the server exchange it per call. That is
the shape most Teams samples take, and it has one genuine advantage this ADR gives up: there is no
cookie in the frame at all, so `SameSite` never arises, cross-site request forgery is structurally
impossible on those calls, and Safari's refusal to carry unpartitioned third-party cookies never
bites.

It was rejected because it makes TodoWerk an application with two ways of knowing who is calling.
Every endpoint would accept either, every future endpoint would have to remember to, and the
authorization policy that today names one scheme would name two forever. ADR-0002's consequence —
"API endpoints authenticate the cookie session, never a bearer token" — was written about Graph
tokens rather than this one, so it does not forbid the bearer; the argument against it is that one
session concept is worth more than the Safari case is worth.

## Why the cookies are unpartitioned

CHIPS is the modern answer to third-party cookies and it is the wrong one here. Microsoft's guidance
is explicit that only `SameSite=None`, secure and **unpartitioned** cookies are readable inside the
Teams iframe, and that a cookie set outside the frame but read within it must not be partitioned. The
consent fallback sets its cookie in a popped-out window; a partitioned cookie would land in that
window's partition and be invisible to the tab. Choosing CHIPS would break exactly the flow it looks
like it protects.

## What the framing relaxation costs, and why the path branch bounds it

`X-Frame-Options` has no allow-list, so a tab that renders at all is a tab whose document sent no
`X-Frame-Options`. Applying that to the whole origin would put the Workbench — which drives renames,
merges and erasure — one clickjacking frame away from anybody. `frame-ancestors` does not apply to
subresources, only to documents, so branching on the request path is not a partial measure that
leaks through scripts and stylesheets: it is complete. The Workbench at `/` is exactly as protected
as it was before this ADR.

Outlook and the Microsoft 365 app are deliberately left out of the host list, though adding them is
one string away. Every host named is a host the milestone has to be walked in. *(Amended
2026-09-09 — see below: both are in the list now, because the manifest offers the tab there whether
the list does or not.)*

## Considered options

- **Bearer token on every request** — rejected above. The better answer for Safari and the worse
  answer for the shape of the application.
- **A second session cookie for the Teams surface** — rejected: two cookies, two expiries, and every
  endpoint accepting both, to avoid changing one attribute on the cookie that already exists.
- **Partitioned cookies (CHIPS)** — rejected: contradicted by Microsoft's own guidance, and breaks
  the popped-out consent flow.
- **A separate origin or deployment for the tab** — rejected: header isolation bought with a second
  app registration, a second secret to rotate, a second token cache partition and a second thing
  every Self-Host operator configures. The path branch buys the same isolation for the cost of an
  `if`.
- **Relaxing framing on the whole origin** — rejected: it is the same product, but the Workbench is
  not the thing being embedded and gains nothing from being embeddable.

## Consequences

- Three cookies change, in three files: `todowerk.session`, `todowerk.antiforgery`, and the
  JS-readable `XSRF-TOKEN`. The latter two are `Strict` today and would simply be absent in the frame.
- Cross-site request forgery protection now rests entirely on the double-submit pair rather than on
  `SameSite` as well. It still holds — `SameSite=None` lets an attacker's page *send* the cookie, never
  *read* it — but the antiforgery token has stopped being defence in depth and become the defence.
  A change that weakens it is now a security change.
- Safari blocks unpartitioned third-party cookies outright, so the tab cannot hold a session in Teams
  web on Safari. The tab detects the pattern — the on-behalf-of exchange succeeded but the following
  request came back 401 — and offers the browser instead. This is mitigated, not fixed.
- Sign-in pages refuse to be framed, so every surface ending in a navigation to the identity provider
  runs through `authentication.authenticate()`: erasure, starting Tenant Consent, and reconnecting
  after the refresh token expires. The popup runs TodoWerk's existing server-side OpenID Connect
  flow, so no token reaches the client and nothing is passed back through `notifySuccess`.
- Sign-out is not offered in the tab at all. The identity is the Teams identity; signing out of
  TodoWerk while remaining signed into Teams is a state the next tab load silently undoes.
- The popup's start and end must share TodoWerk's own domain, which joins `validDomains` in the app
  manifest alongside the identity provider.
- Self-Host inherits every one of these with its own app registration; nothing about the tab's
  session handling differs between deployments, exactly as ADR-0002 promised for sign-in.

## Amendment (2026-09-09): Outlook and the Microsoft 365 app are named

The host list above was written as if naming a host were the act that offers the tab there. It is
not: Microsoft lists Outlook and the Microsoft 365 app as hosts of a personal tab by default for
manifest schema 1.13 and later. Leaving them out of `frame-ancestors` would not have kept the tab
out of those hosts; it would have shown an empty frame there, with the reason visible only in the
console. And the
`*.cloud.microsoft` wildcard, added for the Teams hosts' own move, had already admitted the new
Outlook (`outlook.cloud.microsoft`) and the Microsoft 365 app (`m365.cloud.microsoft`) without
anybody deciding it.

So the list now names them on purpose, plus the two classic Outlook origins
(`outlook.office.com`, `outlook.office365.com`) for tenants that still land there, and the Entra
registration pre-authorises the five further Microsoft client ids those hosts use for single
sign-on ([teams-app-registration.md](../runbooks/teams-app-registration.md) § 3). Nothing about
the session model changes: the same document, the same cookie-less frame, the same popup for the
round trip to the identity provider. What stands unchanged is the rule that every host named is a
host the tab has to be walked in — [teams-tab-walk.md](../runbooks/teams-tab-walk.md) gained a
section for them.

## Amendment (2026-09-22): the host list is Microsoft's, whole

`frame-ancestors` is checked against every ancestor frame, so the list names every host Microsoft
documents for tabs. Naming the origin in the address bar is not enough: a host that nests the tab
inside an intermediate frame of another origin needs that origin named too, and a missing one shows
an empty frame rather than an error anybody sees.

Taken whole
([Requirements for building tabs](https://learn.microsoft.com/microsoftteams/platform/tabs/how-to/tab-requirements)),
that list adds `*.microsoft365.com` and `*.office.com` for the classic Microsoft 365 app hosts and
any `office.com` frame Outlook on the web puts between itself and the tab, and
`outlook-sdf.office.com` and `outlook-sdf.office365.com` for Microsoft's own early-ring tenants.
Nothing else changes: the same document, the same cookie-less frame, the same popup, and `/` stays
at `frame-ancestors 'none'` and `X-Frame-Options: DENY`. The rule that every host named is a host
the tab has to be walked in stands, and [teams-tab-walk.md](../runbooks/teams-tab-walk.md) has
cells for classic Outlook on the web and Outlook on Windows.
