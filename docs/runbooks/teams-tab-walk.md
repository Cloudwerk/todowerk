# Walking the Teams tab against a real tenant

M4's entire surface is integration with somebody else's client, on three platforms, and a fake
proves nothing about any of it. Every automated test of the tab stops at TodoWerk's own two ends:
the token it would accept and the session it would issue. Everything in between belongs to
Microsoft: `app.initialize()`, `getAuthToken()`, the theme placeholders, the popup and the frame.

The tab has been walked on Teams desktop, Teams web and a real Android device. Two cells were not
reachable and are recorded as gaps in [docs/status.md](../status.md): Safari, and the first-ever
user of a tenant without Tenant Consent. What follows is the procedure, kept for those two cells,
for a Self-Host walking its own deployment, and for the next milestone that changes the tab.

The milestone does not close until this has been walked. The record supports the rule: M1 shipped
without a walk and left the id-collation defect unfound for weeks. M2's walk found a silent title
truncation that undo could not recover, and M3's found an administrator's Cancel being recorded as
an approval. No fake could have shown either defect.

**Write down what was observed, not what was expected.** Record a cell that passed as carefully as
one that failed: a pass that nobody wrote down is a cell that nobody knows was walked.

## Before you start

- The app registration is configured: [teams-app-registration.md](teams-app-registration.md).
- The package is built and passes the
  [Teams app validation tool](https://dev.teams.microsoft.com/tools/store-validation) with no
  must-fix findings. Keep the report with the walk's findings.
- The package is uploaded: [teams-app-install.md](teams-app-install.md).
- TodoWerk answers on a host with a certificate a Teams client will accept. A self-signed
  development certificate is not one, so `localhost` cannot host the tab at all — see
  [Getting a host Teams will load](#getting-a-host-teams-will-load) below.
- **Two tenants**, or one tenant used twice in the right order. The unconsented case cannot be
  re-created once a tenant has consented, so walk it first.
- **A user who has never signed in to TodoWerk at all.** The case being tested in an unconsented
  tenant is the *first-ever* user of that tenant, not the second — the second inherits nothing, but
  the tenant may already carry a grant from the first.

## Getting a host Teams will load

The host needs three things at once, and the third is the easiest to miss:

1. **Public HTTPS.** Teams mobile is a phone on somebody else's network, so a LAN address is not
   enough.
2. **A publicly trusted certificate.** Teams refuses a self-signed one and says nothing useful about
   why.
3. **A hostname that does not change.** The Application ID URI is host-qualified
   (`api://<host>/<clientId>`) and the App Package bakes the host into `contentUrl`, `validDomains`
   and `webApplicationInfo`. A tunnel handing out a fresh subdomain per restart means re-editing the
   registration, re-setting `EntraId:ApplicationIdUri` and rebuilding the package every time.

### Microsoft Dev Tunnels

Dev Tunnels is Microsoft's own tunnel service and the one its Teams documentation uses. Install it
from winget so that endpoint protection does not treat it as a remote-access tool:

```powershell
winget install Microsoft.devtunnel
devtunnel user login
devtunnel create todowerk-dev --allow-anonymous   # persistent: the URL survives a restart
devtunnel port create -p 7080 --protocol https
devtunnel host todowerk-dev
```

The host is then `todowerk-dev-7080.<region>.devtunnels.ms`, and it stays the same across
restarts. `devtunnel create` makes a persistent tunnel; `devtunnel host` without a tunnel id makes a
temporary tunnel that is deleted when the process exits
([Dev tunnels FAQ](https://learn.microsoft.com/azure/developer/dev-tunnels/faq#how-can-i-create-a-persistent-tunnel)).
Tunnels expire after 30 days of inactivity unless `--expiration` sets another period.

**Test this before anything else:** dev tunnels answer the first `GET` for `text/html` from each new
browser with an anti-phishing interstitial, skipped only once somebody has selected Continue *in
that browser*
([Dev tunnels security](https://learn.microsoft.com/azure/developer/dev-tunnels/security#anti-phishing-protection)).
A tab load is exactly that request, and the Teams desktop client's webview, Teams mobile's webview
and a desktop browser keep three separate cookie stores. Selecting Continue in Edge does not clear
the interstitial for the other two. If the interstitial appears where the Workbench should be, the
tunnel is the cause, not TodoWerk. Switch to the fallback instead of filing defects.

Group Policy can also turn off dev tunnels tenant-wide, including anonymous access.

### Fallback: a hostname of your own

A named [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/)
bound to a subdomain of a domain you control, such as `todowerk-dev.example.com`, has no
interstitial, a certificate every client trusts, and a hostname that never changes. It costs one DNS
record and is closer to what production will be.

Beyond that is not tunnelling at all: CI publishes `ghcr.io/cloudwerk/todowerk` from `main`, so
deploying that image somewhere with a real hostname walks the tab against something shaped like
a production deployment.

## The matrix

Walk every cell; do not reason about it instead.

### Platforms

| Cell | What to do | Observed |
| --- | --- | --- |
| Teams desktop | Open the tab. The Workbench renders. Preview a Change and run it end to end. | |
| Teams web (Edge or Chrome) | The same. | |
| Teams mobile, on a real device | The same. An emulator alone does not count — the mobile webview is the thing being tested. | |

### Hosts beyond Teams

A manifest at schema 1.13 or later offers the tab in Outlook and the Microsoft 365 app whether or
not anybody meant it to, and Store validation tests every host the package targets
([ADR-0010](../adr/0010-teams-tab-session-and-framing.md), amended 2026-09-09). In each host, check
that the tab renders with no `frame-ancestors` error in the console; sign-in is silent on the
consented tenant and the consent popup appears on the unconsented account; one rename runs end to
end; *Open in browser* leaves the host; and the theme follows the host's. In Outlook on the web,
note the origin in the address bar (`outlook.cloud.microsoft` or `outlook.office.com`), because it
decides which entry in the policy did the work.

| Cell | What to do | Observed |
| --- | --- | --- |
| New Outlook on the web | Select Apps → TodoWerk, then make the checks above. | |
| Classic Outlook on the web (`outlook.office.com`) | The same. "Refused to connect" here means the policy's `frame-ancestors` is missing one of Microsoft's hosts ([ADR-0010](../adr/0010-teams-tab-session-and-framing.md)). | |
| New Outlook on Windows | The same, inside the desktop app's webview. | |
| Microsoft 365 app on the web (m365.cloud.microsoft) | The same. | |
| Microsoft 365 app on Windows | The same. | |
| Microsoft 365 app on Android, real device | The same; the small-screen behaviour, if built, must show here as in Teams. | |

### Consent

| Cell | What to do | Observed |
| --- | --- | --- |
| Tenant **with** Tenant Consent granted | Open the tab as somebody who has never used TodoWerk. Sign-in is silent; no popup appears at any point. | |
| Tenant **without** it, first-ever user | The consent card appears, not a spinner. Selecting the button opens a popup, the popup completes, and the tab renders the Workbench without a reload prompt. | |
| The same, popup closed early | The tab says so in a sentence, and the button can be selected again. | |

### Safari

| Cell | What to do | Observed |
| --- | --- | --- |
| Teams on the web, in Safari | Expected to fail: Safari blocks the unpartitioned third-party cookie ([ADR-0010](../adr/0010-teams-tab-session-and-framing.md)). Confirm the blocked-cookie card appears, that it says something true, and that it offers the browser. Confirm it does **not** retry in a loop. | |
| The server log, for the same visit | One warning, saying the session did not survive the frame and naming `/api/me` and the user agent. Copy the user agent into the Observed column — it is the evidence that decides the row below, and the only record of it. Confirm no such line is written by an ordinary signed-out visit to the browser Workbench. **Read it as evidence from a walk you are performing, not as proof about an unknown visitor**: the header that produces the line is asserted by the client, so anybody can write one by hand. It grants no access, and no more than 60 a minute are written — a warning says so when that ceiling is reached. | |
| Teams mobile's webview | Unknown until this walk. If the same card appears there, file it as an issue of its own: the mitigation was designed for Safari on the desktop. The log line says which webview it was. | |

### Theme

| Cell | What to do | Observed |
| --- | --- | --- |
| Light | The tab matches the Teams window. | |
| Dark | The same, with no flash of light on load — which is what the manifest's theme placeholders are for. | |
| High contrast | Readable throughout, including the freshness chrome and the change queue. | |
| Changed while open | Switch the theme in Teams with the tab open. It re-themes without a reload. | |
| Mobile | A usable theme arrives through the v1 placeholder, which is the only one mobile substitutes. | |

`glass`, on Apple Vision Pro, cannot be walked here. It falls back rather than failing, and that
stays untested by design.

### The four surfaces that end at the identity provider

| Cell | What to do | Observed |
| --- | --- | --- |
| Sign-out is absent | There is no sign-out control anywhere in the tab. "Open in browser" is in its place. | |
| Erasure | Run it end to end against a real account you are willing to lose. The data is destroyed, a popup ends the Microsoft session, and the tab is signed out by the end of it. | |
| Tenant Consent | Start it from **Your organisation** inside the tab. It runs in a popup, and afterwards the tab shows the recorded grant without a reload. | |
| Reconnect | Provoke it if you can — it should almost never fire under SSO, because every tab load re-mints from a fresh token. If it cannot be provoked, say so. | |
| The escape hatch | "Open TodoWerk in your browser" opens a real browser window outside Teams, not a Teams window. | |

### Worth watching while you are there

These are not acceptance criteria. They are questions that no fake can answer:

- **Does a background scan still work after a Teams-only sign-in?** The tab's session comes from an
  on-behalf-of exchange, not from an authorization code, and MSAL files the two in different
  partitions of the token cache. This once broke the tab outright. It is fixed and covered by tests,
  but the tests run against a fake Entra ID: they prove that MSAL's partitioning behaves as
  expected, not that a real token survives a real hour. Sign in through the tab only, wait for the
  next scheduled sync, and see whether the index moves.
- **Does the first paint match the Teams theme, or flash?** Watch a cold load in dark mode.
- **What does a slow network do to the popup?** Teams gives it a bounded time to report back.

## When you are done

- Write the findings down before the milestone closes.
- File every defect as its own issue.
- Anything not fixed goes into the known-gaps table in [docs/status.md](../status.md).
- Update the roadmap there with the walk date and what it found.
