# Giving TodoWerk to an organisation as a Teams app

How a Teams administrator makes the TodoWerk tab available to the people in their organisation.
This runbook documents two routes, both manual, and one convenience script that neither route
requires.

First, configure the deployment's Entra ID app registration as a Teams app:
[teams-app-registration.md](teams-app-registration.md). A package installed against a registration
that has not been configured installs without error and then fails at the first `getAuthToken()`,
with an error that says nothing about app registrations.

## Which package

| You are | Take |
| --- | --- |
| A tenant using CloudWerk's Hosted Service | The Microsoft Store listing where there is one, otherwise the package your deployment's operator provides. |
| A tenant whose policy declines Store apps | The package your deployment's operator provides, uploaded to your own catalog. |
| Running TodoWerk on your own infrastructure | Build your own — see [Building a Self-Host package](#building-a-self-host-package). CloudWerk's package names CloudWerk's host and cannot authenticate against yours ([ADR-0011](../adr/0011-two-app-packages-and-a-template.md)). |

## Route 1 — install from the Microsoft Store

Where a deployment's operator has published TodoWerk to the Microsoft Store, this is the
recommended route for tenants using that deployment, and it needs nothing from this page. A Store
listing requires the publisher to be verified in Microsoft Entra ID first.

## Route 2 — upload the package to your organisation's catalog

This route always works. Prefer it to any script.

1. Sign in to the [Teams admin center](https://admin.teams.microsoft.com) as a **Teams
   Administrator** or **Global Administrator**.
2. Select **Teams apps → Manage apps → Upload new app → Upload**.
3. Select the `.zip` file.
4. The app appears in **Manage apps** as an app your organisation published. There you can allow or
   block it, and set up a policy that installs it for people so that they do not have to find it.

To publish a new version later, upload the new `.zip` the same way. Teams matches it to the existing
app by the manifest id inside it and treats it as an update, provided the manifest's `version` has
gone up.

Then have an administrator approve TodoWerk for the whole organisation, once, from inside the
product: select **Your organisation → Approve for the organisation**. This step decides whether
sign-in in the tab is silent. Without it, every person sees a consent popup the first time they open
the tab. The tab works either way; the popup is the difference
([ADR-0008](../adr/0008-tenant-consent-is-delegated.md)).

## The optional script

`scripts/Publish-TodoWerkTeamsApp.ps1` does the same upload with direct Microsoft Graph requests. It
is for the administrator who would rather not use the admin center, and it depends on no PowerShell
module by design: having to install `MicrosoftTeams` or `Microsoft.Graph.Teams` before evaluating a
product is the barrier the script removes.

```powershell
pwsh ./scripts/Publish-TodoWerkTeamsApp.ps1 -PackagePath ./artifacts/teams/todowerk-teams-contoso.zip
```

It signs an administrator in interactively with a device code, every time. **There is no unattended
version of this and none can be added**: publishing to an organisation's app catalog needs
`AppCatalog.ReadWrite.All` as a delegated permission. Microsoft Graph's publish operation supports no
application permission, so no client credential could publish from a pipeline
([Publish teamsApp](https://learn.microsoft.com/graph/api/teamsapp-publish)).

Nothing in this runbook requires it. If it fails for a reason you would rather not debug, Route 2
does the same thing and always works.

## Building a Self-Host package

```powershell
# Once: your own values. The file in the repository holds placeholders only.
cp packaging/teams/values.self-host.json ./my-values.json
# Fill in ManifestId (a GUID you generate and never change), Host, ClientId, ApplicationIdUri
# and DeveloperName - your organisation's legal name, which is what the installing tenant's
# admin centre shows as the publisher. The build refuses to run while it still reads
# "Your organisation".

pwsh ./scripts/New-TodoWerkTeamsAppPackage.ps1 -ValuesPath ./my-values.json
```

Every tenant that installs the package records the manifest id as the app's identity. Generate it
once, keep it, and never change it: a changed id forces an uninstall in every tenant, one tenant at
a time.

Then run the package through the
[Teams app validation tool](https://dev.teams.microsoft.com/tools/store-validation) before you
distribute it. Its verdict covers both distribution channels: a package that passes is as good for a
manual upload as it is for the Store.

## What people see afterwards

The tab is **personal-scoped**: each person adds it to themselves, or an app setup policy adds it
for them. There is no channel or group tab and there will not be one — the Hashtag Manager is one
person's hashtags and has nothing to show a team.

The tab has no sign-out control, by design: the identity there is the Teams identity, and signing
out of TodoWerk while staying signed in to Teams is a state that the next tab load silently undoes
([ADR-0010](../adr/0010-teams-tab-session-and-framing.md)). "Delete my data" is offered in the tab
exactly as it is in the browser.

## Known limitation before you roll it out

**Teams on the web in Safari cannot hold a TodoWerk session.** Safari refuses unpartitioned
third-party cookies outright, and inside the Teams frame that is what TodoWerk's session cookie is.
The tab detects it and says so, and offers to open TodoWerk in a browser tab of its own, where
everything works. Teams desktop, Teams mobile and Teams on the web in Edge or Chrome are unaffected.
This is mitigated, not fixed; [ADR-0010](../adr/0010-teams-tab-session-and-framing.md) gives the
reasoning.
