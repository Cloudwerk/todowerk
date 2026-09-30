# Security policy

## Supported versions

TodoWerk is in early development and has no release. Only `main` is supported.

| Version | Supported          |
| ------- | ------------------ |
| `main`  | :white_check_mark: |

## Reporting a vulnerability

Please do **not** open a public GitHub issue for security vulnerabilities.

Preferred channel: **GitHub private vulnerability reporting** — use the
"Report a vulnerability" button under this repository's **Security** tab.

Fallback: email your findings to security@cloudwerk.com.

### What to include

- Type of vulnerability and its impact
- Affected source files (path, branch/commit, or direct URL)
- Step-by-step instructions to reproduce
- Proof-of-concept code, if possible

### What to expect

- Acknowledgment within 48 hours
- Regular updates on progress
- Credit in the advisory, if desired
- Notification when the issue is fixed

## What matters most in this codebase

TodoWerk holds Microsoft Graph tokens on the server on purpose — see
[ADR-0002](docs/adr/0002-backend-held-tokens.md). The browser only ever holds a
session cookie.

### What TodoWerk asks for, and what it does with it

One delegated permission, requested once at sign-in:

| Permission | Why |
| --- | --- |
| `Tasks.ReadWrite` | Read task titles to build the hashtag index, and write them back when you confirm a rename or a merge. |

There is no read-only mode. Every user grants write access to their tasks, including one
who only ever looks at their inventory — a deliberate trade recorded in
[ADR-0007](docs/adr/0007-one-consent-grant.md), because the component that performs a write
is a background worker with no browser to prompt through, so the grant has to exist before
a change is ever queued. `Tasks.ReadWrite` subsumes `Tasks.Read`, and both are never
requested together.

What a write can do is bounded by design: only the hashtag's own text in a task title is
ever rewritten, every task is re-read immediately before it is written, and what was written
is journaled for 30 days so a whole change can be undone ([ADR-0006](docs/adr/0006-what-a-change-is.md)).
TodoWerk never creates, completes or deletes a task.

An administrator can approve TodoWerk for a whole tenant, and that changes who is prompted
and nothing else. Tenant Consent is admin consent to this same delegated permission
([ADR-0008](docs/adr/0008-tenant-consent-is-delegated.md)) — no application permission, no
directory permission, no app-only credential. TodoWerk still acts as each person with that
person's own token, and still reaches no mailbox whose owner has not signed in. Approving for
the organisation is therefore not a tenant-wide data exposure; it is the same decision every
individual was making, made once.

### What TodoWerk stores, and for how long

A tenant id, a pseudonymous Entra ID object id and two timestamps per person — first and last
sign-in — plus what using the product leaves behind: the hashtag index built from task titles, and
change journals holding titles for 30 days. Twelve months without a sign-in destroys all of it,
and anybody may ask for the same at any time from inside the app. See
[ADR-0009](docs/adr/0009-what-todowerk-stores-about-a-person.md) and
[PRIVACY.md](PRIVACY.md).

### Where a report matters most

That token position makes a handful of areas disproportionately security-relevant,
and reports touching them get priority:

- Anything that would expose a Graph token, a refresh token, or the token cache to
  the browser, to logs, or to another tenant's user
- Session handling: cookie flags, the Data Protection key ring, sign-out, fixation
- Cross-tenant data leakage in the hashtag index once it exists
- Antiforgery and CORS on the app's own API. Since M4 this is load-bearing rather than
  defence in depth: the Teams tab needs TodoWerk's cookies inside a third-party frame, so all three
  are `SameSite=None; Secure`, and the antiforgery double-submit pair is now the whole of the
  cross-site request forgery defence ([ADR-0010](docs/adr/0010-teams-tab-session-and-framing.md))
- Framing. `/teams` and its auth-end page are the only documents TodoWerk allows to be framed, and
  only by the Microsoft Teams hosts. Anything that widens that, or that reaches the Workbench at
  `/`, is a clickjacking report against renames, merges and erasure
- Anything an unauthenticated caller can make the application write to its log. Since M4 there is
  one such path — a header the Teams tab puts on one request, which turns a 401 into a diagnostic
  naming the requesting user agent. The application's rate limiter does
  not reach it, because authorization short-circuits an unauthenticated request first, so the
  ceiling on it is hand-rolled and lives in that middleware. A report that gets past the ceiling,
  or that gets anything other than a truncated user agent into the line, is a report about this
- Over-broad Graph permission scopes being requested or used
- Anything that lets a change be queued, run or undone against tasks that are not the
  requesting user's, or without their confirmation

## Security expectations for contributors

- Never commit secrets, connection strings, API keys, certificates, or tenant
  URLs — use user-secrets, environment variables, or Key Vault references
- The checked-in `appsettings.json` ships empty credential values on purpose;
  keep it that way
- Report any security concern immediately rather than filing it as a bug
