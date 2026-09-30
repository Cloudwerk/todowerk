# Making an app registration a Teams app

Everything the Teams tab needs from Microsoft Entra ID, and nothing that is code. The tab reuses
the app registration TodoWerk already has: the same client id, the same client secret, and the same
`Tasks.ReadWrite` permission, with no other. The registration gains an identity that a Teams client
can request a token for, and a list of the Teams clients pre-authorized to request one.

No new permission is requested here. The requested scopes stay exactly `Tasks.ReadWrite` plus the
OpenID scopes ([ADR-0007](../adr/0007-one-consent-grant.md)), and the Teams path adds no
application permission, no directory permission and no app-only credential
([ADR-0008](../adr/0008-tenant-consent-is-delegated.md)).

**One registration, one host.** Teams single sign-on does not support several domains per app: the
manifest's `webApplicationInfo` names one Application ID URI, that URI names one host, and the
token a Teams client acquires is acquired for that host. A Self-Host is a different host, so it
needs its own registration and its own App Package
([ADR-0011](../adr/0011-two-app-packages-and-a-template.md)).

In the steps below, replace the placeholders with your own tenant, client id and host.

## What you need

- An account able to edit the app registration (Application Administrator, or an owner of it).
- The host TodoWerk answers on, with a certificate a Teams client will accept. Teams will not load
  a tab over plain HTTP and will not load one over a certificate it does not trust, including a
  self-signed development certificate.

## Before anything else: decide who the registration is for

**Authentication → Supported account types** is the one setting here that cannot be corrected
quietly later, and it has to agree with the deployment's `EntraId:TenantId`:

| Who the deployment serves | Supported account types | `EntraId:TenantId` |
| --- | --- | --- |
| One tenant — the usual Self-Host | Accounts in this organizational directory only | that tenant's id |
| More than one tenant — a multi-tenant deployment | Accounts in any organizational directory | `organizations` |

Never select an option that includes personal Microsoft accounts. TodoWerk is B2B only
([CONTEXT.md](../../CONTEXT.md)), and the application refuses to start with `EntraId:TenantId` set
to `common` for exactly that reason.

The two settings are checked in different places, and neither reports the other, so a mismatch is
expensive to find. **The authority decides, not the registration.** A registration set to multiple
tenants, deployed with a single tenant's id, serves that tenant and refuses every other one with
`AADSTS50020`. The error is shown to the person signing in and logged nowhere on the operator's
side. Everybody who administers the deployment is in the tenant that works, so none of them sees
it. §8 of [deploying-a-self-host.md](deploying-a-self-host.md) describes the symptom in full and
gives a one-line check that settles it.

Changing this later is not free: a registration that moves from one tenant to many needs consent
granted afresh in every tenant, and one moving the other way locks out everybody already using it.

## 1. Set the Application ID URI

Select **Entra ID → App registrations → your app → Manage → Expose an API → Application ID URI →
Add**.

The portal suggests `api://<clientId>`. **Do not accept it.** Teams requires the host-qualified
form:

```
api://<host>/<clientId>
```

for example `api://todowerk.contoso.com/1a2b3c4d-....`. The host must be the exact host the tab is
served from, with no scheme, no port unless the tab is served on one, and no trailing slash.

The failure does not explain itself, so know the reason: the Teams client compares the origin of
the framed document with the domain in this URI before it issues a token. If the URI is wrong,
`getAuthToken()` fails with a resource-disabled or invalid-resource error that says nothing about
domains.

## 2. Expose `access_as_user`

Still under **Expose an API**, select **Add a scope** and enter these values:

| Field | Value |
| --- | --- |
| Scope name | `access_as_user` |
| Who can consent | Admins and users |
| Admin consent display name | Access TodoWerk as you |
| Admin consent description | Allows Teams to sign a person in to TodoWerk as themselves. TodoWerk then reads and changes that person's own Microsoft To Do tasks with their own account, and reaches nobody else's. |
| User consent display name | Access TodoWerk as you |
| User consent description | Lets TodoWerk sign you in from Microsoft Teams. TodoWerk works with your own tasks, as you, and never with anybody else's. |
| State | Enabled |

The display names and descriptions are written for people on purpose: a person reads this wording
on a consent screen, and "access_as_user" is not a sentence.

The scope grants nothing on its own: it is the audience the SSO token is minted for. What actually
reaches Microsoft To Do is the on-behalf-of exchange the backend performs afterwards, against
`Tasks.ReadWrite`.

## 3. Pre-authorize the Microsoft 365 clients

Still under **Expose an API**, select **Add a client application** once for each id below, and
select the `access_as_user` scope for each:

| Client id | Which client |
| --- | --- |
| `1fec8e78-bce4-4aaf-ab1b-5451cc387264` | Teams desktop and mobile |
| `5e3ce6c0-2b1f-4285-8d4b-75ee78787346` | Teams web |
| `4765445b-32c6-49b0-83e6-1d93765276ca` | Microsoft 365 app, web |
| `0ec893e0-5785-4de6-99da-4ed124e5296c` | Microsoft 365 app, desktop |
| `d3590ed6-52b3-4102-aeff-aad2292ab01c` | Microsoft 365 app, mobile — and Outlook desktop |
| `bc59ab01-8403-45c6-8796-ac3ef710b3e3` | Outlook web |
| `27922004-5251-4030-b22d-91ecd9a37ea4` | Outlook mobile |

The five beyond Teams are there because a manifest at schema 1.13 or later offers a personal tab
in Outlook and the Microsoft 365 app as well, and single sign-on in those hosts fails or prompts
without them ([ADR-0010](../adr/0010-teams-tab-session-and-framing.md), amended 2026-09-09).

Without these, every `getAuthToken()` raises its own consent prompt: an extra dialog on top of the
one TodoWerk already asks for, saying something a person cannot act on. With them, a tenant that has
granted Tenant Consent sees no prompt at all, which is the purpose of this step.

All seven ids are Microsoft's own and are the same in every tenant.

## 4. Register the redirect URIs the tab needs

Under **Manage → Authentication → Web**, add the URI below alongside the three that are already
there ([CONTRIBUTING § Development setup](../../CONTRIBUTING.md#development-setup)).

The consent popup runs TodoWerk's existing OpenID Connect flow, so it returns to the redirect URI
that flow already uses (`https://<host>/signin-oidc`) and needs nothing new. What is new is where
the popup is sent afterwards, and one of those destinations follows a sign-out:

| URI | Under | Why |
| --- | --- | --- |
| `https://<host>/teams/auth-end` | **Web** → Redirect URIs, beside the other three | Where erasure's last leg returns after Entra ID ends the session. Without it the popup is left on Microsoft's "you have signed out" page and never reports back, and Teams eventually calls the flow cancelled. |

**In the Redirect URIs list, not the Front-channel logout URL box.** The two look interchangeable in
the portal and are not: the front-channel logout URL is where Entra ID *pushes* a notification when
a session ends somewhere else, and it does not authorize anything. What erasure needs authorized is
a `post_logout_redirect_uri`, and Microsoft's documentation is explicit that the value "must match
one of the redirect URIs registered for your application"
([OpenID Connect on the Microsoft identity platform](https://learn.microsoft.com/entra/identity-platform/v2-protocols-oidc#send-a-sign-out-request)).
If you put it in the wrong box, nothing reports an error until somebody erases themselves from
inside a tab.

No new *type* of redirect URI is registered. There is no SPA platform, no implicit grant, and no
public client: the tab holds no token of any kind, so nothing in the browser ever redeems anything
([ADR-0002](../adr/0002-backend-held-tokens.md), [ADR-0010](../adr/0010-teams-tab-session-and-framing.md)).

## 5. Tell TodoWerk its own Application ID URI

The backend has to accept tokens minted for it, and it cannot guess the host-qualified form:

```bash
cd src/TodoWerk.Web
dotnet user-secrets set "EntraId:ApplicationIdUri" "api://<host>/<clientId>"
```

Outside Development, it is an ordinary setting in the `EntraId` section. On a deployment with no
Teams App Package, leave it empty: the tab's exchange endpoint then refuses every Teams token, which
is the right answer for a deployment that has no tab.

## 6. Point the consent dialog at the terms and the privacy notice

The **Terms of service** and **Privacy statement** links on the Microsoft consent dialog come from
the app registration's **Branding & properties**, not from the Teams manifest. Everybody who signs
in sees them, whether or not there is a Store listing, and a verified publisher badge above a dead
legal link is worse than no badge at all.

TodoWerk serves both documents itself, so they are paths on the same host the tab runs on:

| Field | Value |
| --- | --- |
| **Terms of service URL** | `https://<host>/legal/terms` |
| **Privacy statement URL** | `https://<host>/legal/privacy` |

Set the same two addresses in the Teams manifest's `developer` block and in any Partner Center
submission. The package script fills in the manifest's addresses from `Host`, so there is nothing to
type there. All three have to name the same documents: a mismatch between the manifest and the
submission is a documented cause of validation failure, and the listing is re-validated against
these addresses long after anybody remembers setting them.

The script fills two more addresses from `Host`, and the deployment serves both itself.
`developer.websiteUrl` becomes `https://<host>/about`: the Teams admin center shows it as the app's
support link, and a support link that requires a sign-in is a *must fix*. `https://<host>/`, the
Workbench, requires one. The root-level `publisherDocsUrl` becomes `https://<host>/administrators`,
the page written for the administrator deciding whether to allow the app
([ADR-0013](../adr/0013-the-handbook-is-split-by-kinship.md)). Neither has a counterpart on the app
registration.

The fourth field in that block is governed by the same rule and is easier to get wrong, because the
build cannot derive it from `Host`: you supply it as `DeveloperName` in the values file.
`developer.name` has to read exactly what Partner Center and AppSource call the publisher:
"developer name must be the same in the app manifest and AppSource" is a *must fix* in its own
right.

Both pages answer anonymously and neither sells anything, which is what the Store validation
guidelines require of them. Before you point anything at them, set the operator that the terms name:
`Legal:Operator`, `Legal:OperatorContact` and `Legal:GoverningLaw`, described in
[deploying-a-self-host.md](deploying-a-self-host.md). If they are unset, the terms still render but
name nobody, and the application says so once at startup.

## 7. Check it

- **Supported account types** matches what the deployment's `EntraId:TenantId` says, per the table
  at the top. Check both at the same time: neither one reports the other, and a mismatch is
  invisible until somebody outside the operator's own tenant tries to sign in.
- The Application ID URI reads `api://<host>/<clientId>` and the host matches the tab's host
  exactly.
- `access_as_user` is **Enabled** and set to **Admins and users**.
- All seven Microsoft client ids appear under **Authorized client applications** with that scope selected.
- **Authentication → Web → Redirect URIs** lists `https://<host>/teams/auth-end` alongside
  `signin-oidc`, `signout-callback-oidc` and `auth/tenant-consent/callback`. Step 4 adds only this
  URI, and its absence stays invisible until somebody tries erasure from inside a tab.
- **Authentication → Settings** has both implicit grant boxes clear and **Allow public client
  flows** disabled, and there is no SPA platform in the redirect list. The tab holds no token of any
  kind; if any of those three is on, something is configured for a flow TodoWerk does not run.
- **Branding & properties** carries `https://<host>/legal/terms` and `https://<host>/legal/privacy`,
  both of which resolve when fetched from outside with no session, and both of which name the
  organisation actually operating the deployment.
- **API permissions** still shows `Tasks.ReadWrite` and nothing else. If anything else appeared,
  something other than this procedure added it. `Tasks.Read` in particular is a leftover from before
  M1 moved to the write scope, and [ADR-0007](../adr/0007-one-consent-grant.md) rejected requesting
  both — it makes the grant look wider than it is.

None of this can be proved without a Teams client. The walk proves it:
[teams-tab-walk.md](teams-tab-walk.md).
