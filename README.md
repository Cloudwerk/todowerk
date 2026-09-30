# TodoWerk

[![PR validation](https://github.com/Cloudwerk/todowerk/actions/workflows/pr-validation.yml/badge.svg?branch=main)](https://github.com/Cloudwerk/todowerk/actions/workflows/pr-validation.yml)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](global.json)
[![Node 22](https://img.shields.io/badge/Node-22-339933.svg)](.nvmrc)

Open-source tooling that adds missing capabilities to Microsoft To Do. Built by [CloudWerk GmbH](https://cloudwerk.com).

> **Status: early development.** You can sign in with a work or school account, have your task lists scanned, work through the hashtag inventory — usage counts, casing variants, near-duplicates, stale tags — and change what you find: rename a hashtag, settle one written several ways onto a single spelling, fold several into one, or give a hashtag an emoji and put it at the front of every task that carries the tag, and take that emoji off again once the tag has gone — previewed before it runs and undoable for 30 days. All of it also runs as a personal Microsoft Teams tab. There is no release and nothing supported to install. See [docs/status.md](docs/status.md) for what works today and what is still missing.

## Why

Microsoft To Do understands hashtags in task titles, but offers no way to manage them: no list of the tags you use, no rename, no merge. Anyone who tags seriously ends up hand-maintaining a "TAGS" dummy list as a makeshift index — and still can't fix a typo'd tag across 40 tasks without editing each one.

## What v1 will do

The **Hashtag Manager**: a workbench for every hashtag across your To Do tasks.

- Inventory of all your hashtags — task counts, lists touched, last used — with flags for near-duplicates, stale tags, and hashtags spelled inconsistently across your tasks
- Rename, merge, and one-click casing clean-up, always with a dry-run preview of the exact title changes before anything is written
- Changes run as background jobs with progress, pause, and undo
- Marker rules give a hashtag an emoji, applied as a change like any other — previewed, queued and undoable — and an emoji left behind by a tag you removed can be taken off the same way
- An administrator can approve TodoWerk once for the whole organisation, so nobody else is asked at sign-in. It grants TodoWerk no access to anybody's tasks: it still acts as each person with that person's own sign-in ([ADR-0008](docs/adr/0008-tenant-consent-is-delegated.md))
- A tenant can see how many colleagues use it and how much — counts only, never a name, never a per-person row, never one person's hashtags shown to another
- All of it as a Microsoft Teams tab, signed in as your Teams identity

B2B only: TodoWerk works with work and school (Entra ID) accounts. Consumer Microsoft accounts are out of scope.

## How it is built

A .NET 10 backend in a Clean Architecture layout (`Domain`, `Application`, `Infrastructure`, `Web` over a `SharedKernel`), with a React and Fluent UI v9 single-page app served from the same host. Features are cut as vertical modules — `Changes`, `Indexing`, `Licensing`, `Markers`, `Onboarding` — repeated as folders in each layer, and architecture tests fail the build if a layer or a module reaches somewhere it shouldn't. A sixth name, `Jobs`, is reserved rather than built: the folders exist in three layers and hold nothing, and `ModuleBoundaryTests` keeps the two lists apart for that reason — a boundary check over an empty namespace passes for the wrong reason.

Every Microsoft Graph token stays on the server. The browser only ever holds a session cookie, because renames run as background jobs that have to keep calling Graph long after the tab is closed. That constraint, and the others that shaped the design, are written up in [docs/adr/](docs/adr/).

## Getting started

There is no release and nothing supported to install — the only way to run TodoWerk today is from source. CI does push a container image to GitHub's registry on every commit to `main`, but it is a build artefact rather than a release: undocumented, carrying no version number anybody has promised anything about, and changing without notice until M6 packages self-hosting properly. Each image is at least identifiable — it is tagged `sha-<commit>` as well as `latest`, and a running deployment names the commit it was built from at `/version`, which is what a deployment should be pinned to rather than `latest`. Putting that image on a server of your own is possible today and written down in [docs/runbooks/deploying-a-self-host.md](docs/runbooks/deploying-a-self-host.md), but you would be deploying a build artefact, not a release.

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node 22](https://nodejs.org), a SQL Server you can reach, and an Entra ID app registration in a tenant you can consent in. The exact versions, the app registration settings, and the user-secrets to set are in [CONTRIBUTING.md § Development setup](CONTRIBUTING.md#development-setup) — kept in one place so the two files cannot drift. Once configured:

```bash
# from the repository root
(cd src/TodoWerk.Web/ClientApp && npm ci && npm run build)   # build the SPA once
dotnet test TodoWerk.slnx                                    # no live tenant needed; a SQL Server is
dotnet run --project src/TodoWerk.Web                        # https://localhost:7080
```

The suites run on Microsoft.Testing.Platform, so pass it no VSTest-era flags: `dotnet test` forwards
anything it does not recognise to the test host, and a rejected option surfaces as "Zero tests ran"
rather than as an error. CONTRIBUTING § Testing has the detail.

Creating the database is one `dotnet ef database update`, written out in CONTRIBUTING alongside everything else you need before the first run.

## Getting the Teams tab

TodoWerk ships as a **personal Teams tab**: the same Workbench, inside Teams, signed in as your Teams identity. There is no channel or group tab — the Hashtag Manager is one person's hashtags and has nothing to show a team.

The tab has been tested on Teams desktop, Teams on the web and Teams on Android. There is still nothing *supported* to install: the only way in is an administrator uploading the package to their own organisation's catalog.

A Teams administrator uploads an App Package to the organisation's own catalog: **Teams admin center → Teams apps → Manage apps → Upload new app**. The full procedure, including what a Self-Host has to build for itself, is in [docs/runbooks/teams-app-install.md](docs/runbooks/teams-app-install.md); what the Entra ID app registration needs first is in [docs/runbooks/teams-app-registration.md](docs/runbooks/teams-app-registration.md).

```bash
# Build a package for your own deployment
cp packaging/teams/values.self-host.json ./my-values.json   # then fill in your own values
pwsh ./scripts/New-TodoWerkTeamsAppPackage.ps1 -ValuesPath ./my-values.json
```

One known limitation before anybody rolls it out: **Teams on the web in Safari is expected not to hold a TodoWerk session**, because Safari refuses the unpartitioned third-party cookie that the session is inside a frame. The tab is built to detect exactly that and offer to open TodoWerk in a browser tab instead, where everything works. Said plainly: **that has never been observed** — TodoWerk has not been verified in Safari, on macOS or on iOS, so the behaviour and the card are a prediction rather than a report. Teams desktop, Teams on Android and Teams on the web in Edge were tested and are unaffected. The reasoning is in [ADR-0010](docs/adr/0010-teams-tab-session-and-framing.md).

## Usage

Sign in with a work or school account and TodoWerk reads the hashtags out of your Microsoft To Do tasks into an index of its own, keeps that index current in the background, and shows you what you actually have: how often each hashtag is used, across how many lists, when it was last touched, and which ones look like mistakes — written more than one way, nearly identical to another, or not edited in months.

Then you can fix them. Select a hashtag — or several, to fold them together — and TodoWerk shows you the exact task titles it would rewrite before anything happens, names any list it left out because it has not read that one end to end, and asks a second time if the change would merge two hashtags into one. Confirming queues it; it runs in the background, one task at a time, rewriting that hashtag and nothing else in the title. Every task it writes is journaled, so the whole change can be undone for 30 days — and a task somebody edited in the meantime is left alone, because their edit wins.

You can also tell TodoWerk that a hashtag carries an emoji — `#bread` carries 🍞 — and it will put that emoji at the front of every task with the tag, or take it off again once the tag has gone. Those two are the changes that write outside a hashtag, and they are the same kind of change as the others: previewed as the exact titles, queued, undoable. Neither runs on its own. Applying adds and reorders emoji and never takes one away, so an emoji whose hashtag you have since removed stays put; removing is the separate change that takes those back off. It only ever touches emoji TodoWerk put there itself, never one you typed. Renaming a hashtag carries its emoji along.

[docs/status.md](docs/status.md) is the honest account of what works, what does not, and where the code looks more finished than it is — including the two cases of the Teams tab that have not been verified: Safari, and the first run of a tenant that has not granted Tenant Consent.

## Project structure

```
src/
  TodoWerk.SharedKernel/   Result, Error, Entity — depends on nothing
  TodoWerk.Domain/         The hashtag grammar, the index entities, and their rules
  TodoWerk.Application/    CQRS commands, queries, handlers
  TodoWerk.Infrastructure/ Microsoft Graph, Entra ID authentication, EF Core persistence
  TodoWerk.Web/            ASP.NET Core host, API endpoints, middleware
    ClientApp/             React + Fluent UI v9 SPA (Vite)
tests/
  TodoWerk.UnitTests/          Domain and application logic
  TodoWerk.ArchitectureTests/  Layer and module boundaries — these fail the build
  TodoWerk.IntegrationTests/   The real application, booted end to end
docs/
  adr/                     Architecture decision records
  runbooks/                Procedures that need a real tenant or a real server, written down
                           so they are repeated rather than rediscovered
  status.md                What works today
  CHANGELOG.md             Keep a Changelog
handbook/                  The About Page and the Administrator's Guide, served by the application
                           at /about and /administrators (ADR-0013)
packaging/
  teams/                   One source manifest and its icons; the built packages are not committed
scripts/                   PowerShell 7 tools — build a Teams package, publish one, verify a flow
```

Technology: .NET 10, ASP.NET Core, Microsoft.Identity.Web, Microsoft Graph, EF Core over SQL Server, React 19, Fluent UI v9, Vite, TypeScript, xUnit v3, NetArchTest.

## Contributing

Work is cut into milestones that each leave something demoable; the roadmap is in [docs/status.md](docs/status.md). Issues are filed per milestone as it approaches.

Start with [CONTRIBUTING.md](CONTRIBUTING.md). Two other things are worth reading before opening a pull request: [CONTEXT.md](CONTEXT.md), which fixes the vocabulary the code and the UI both use, and [docs/adr/](docs/adr/), which records the decisions that are already settled and why. Pull requests run build, unit, architecture, and integration tests.

Because CloudWerk also operates a hosted service on this code, contributions require a [Contributor License Agreement](CLA.md). You keep your copyright; a bot handles the signing on your first pull request.

- [Report a bug](https://github.com/Cloudwerk/todowerk/issues/new?template=bug_report.yml) · [Request a feature](https://github.com/Cloudwerk/todowerk/issues/new?template=feature_request.yml)
- [Code of Conduct](CODE_OF_CONDUCT.md)
- [Security policy](SECURITY.md) — vulnerabilities go through private reporting, never the issue tracker
- [Privacy notice](PRIVACY.md) — what TodoWerk stores, what reaches a server log, for how long, and
  how to have it deleted
- [Terms of use](TERMS.md) — the terms every deployment serves, under its own operator's name

Both are served by the application itself, at `/legal/privacy` and `/legal/terms`, so the document a
consent dialog links to and the document in this repository are the same one. So are the two pages in
[handbook/](handbook/): `/about`, which the Teams admin center shows as the app's support link, and
`/administrators`, written for the administrator deciding whether to allow TodoWerk
([ADR-0013](docs/adr/0013-the-handbook-is-split-by-kinship.md)).

## License

[AGPL-3.0](LICENSE). CloudWerk also operates a hosted service on this code. Running the same stack yourself is free and always will be: a Self-Host has no Licence, asks nobody, and is never refused.
