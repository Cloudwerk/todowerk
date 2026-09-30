# Verifying Tenant Consent against a real tenant

Every automated test of Tenant Consent stops at TodoWerk's own two ends of the round trip: the
redirect it builds and the callback it will believe. The middle belongs to Microsoft, and the first
time it was observed, on 2026-08-25, it did something no invented test parameter had guessed — the decline redirect carried `admin_consent=True` *alongside*
`error=consent_required`, and an administrator clicking Cancel was recorded as an approval. This
runbook exists so that observation is repeated, not re-derived, whenever the flow meets a new
environment.

## When to run this

- A new deployment origin goes live (a deployed instance's callback URI must join the app
  registration, and nobody has watched that environment's round trip).
- The app registration, the requested scopes ([ADR-0007](../adr/0007-one-consent-grant.md)), or the
  consent flow's code change.
- Microsoft changes the admin-consent endpoint's behaviour — the findings below pin what it did in
  2026, not what it will always do.

## What you need

- An account able to edit the app registration's redirect URIs (owner or Application
  Administrator).
- An account able to grant tenant-wide admin consent (Global Administrator, or a role Entra ID
  accepts for this app).
- A second member account in the tenant that has **never** consented to TodoWerk individually —
  the no-prompt check proves nothing with an account that once consented. A fresh test user is the
  clean choice.
- TodoWerk answering at the origin under test
  ([CONTRIBUTING § Development setup](../../CONTRIBUTING.md#development-setup) for running from
  source).

## How to run it

```powershell
pwsh ./scripts/Verify-TenantConsent.ps1                                   # local, from source
pwsh ./scripts/Verify-TenantConsent.ps1 -BaseUrl https://your-origin      # a deployed instance
```

The wizard performs nothing itself — every action is yours, in a browser, as the administrator. It
opens the right pages, says what to click, asks what you observed, and walks the checks in a
deliberate order: **decline before approve**, so the records-nothing path is observed against a
genuinely empty state.

## What it produces

A dated markdown results file **outside the repository** (default: your user profile;
`-ResultsPath` overrides). It names your tenant — this repository is public, so findings travel as
generalised issue comments, never as the raw file.

## What the real round trip looks like

Observed against a live tenant on 2026-08-25; shapes generalised:

- **Decline** — `?error=consent_required&error_description=AADSTS65004: User declined…`
  `&error_uri=…&admin_consent=True&state=<the state TodoWerk issued>`, with **no `tenant`
  parameter**. `admin_consent=True` names which flow this was, not what the administrator decided.
- **Success** — `?admin_consent=True&tenant=<tenant id>&state=<ours>&scope=openid profile
  https://graph.microsoft.com/Tasks.ReadWrite`. The tenant is named; `offline_access` is not
  echoed back.

The callback therefore records a grant only when the redirect carries no `error`, the state
matches, and the tenant is named and matches — anything else records nothing
([ADR-0008](../adr/0008-tenant-consent-is-delegated.md) for why the callback can never be treated
as proof).

## Capturing redirects byte-exact

TodoWerk neither stores nor logs what a failed callback carried, so the browser is the only
witness. Either tick **Preserve log** in
the DevTools Network tab before clicking Cancel/Accept — or stop the TodoWerk process at the
consent screen, click, and read the full query string from the failed navigation, then restart and
replay the URL. The second method is how the shapes above were captured.
