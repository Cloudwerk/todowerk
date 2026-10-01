# TodoWerk

[![PR validation](https://github.com/Cloudwerk/todowerk/actions/workflows/pr-validation.yml/badge.svg?branch=main)](https://github.com/Cloudwerk/todowerk/actions/workflows/pr-validation.yml)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](global.json)
[![Node 22](https://img.shields.io/badge/Node-22-339933.svg)](.nvmrc)

Open-source tooling that adds missing capabilities to Microsoft To Do, starting with a hashtag manager. Built by [CloudWerk GmbH](https://cloudwerk.com).

> **Status: released.** TodoWerk 1.0.2 is the first release. CloudWerk runs it as a hosted service and lists it in the Microsoft Teams Store, and you can self-host it from this repository. Self-hosting is available as is: it works and is documented, but CloudWerk makes no support commitment for it. [docs/status.md](docs/status.md) lists what works today and what is still missing.
>
> What works: you can sign in with a work or school account, have your task lists scanned, and work through the hashtag inventory (usage counts, casing variants, near-duplicates, stale tags). You can rename a hashtag, settle one written several ways onto a single spelling, fold several into one, or give a hashtag an emoji that goes at the front of every task carrying the tag, and take that emoji off again once the tag has gone. Every change is previewed before it runs and can be undone for 30 days. All of it also runs as a personal Microsoft Teams tab.

## Why

Microsoft To Do understands hashtags in task titles but offers no way to manage them: no list of the tags you use, no rename, no merge. Anyone who tags seriously ends up hand-maintaining a "TAGS" dummy list as a makeshift index, and still can't fix a typo'd tag across 40 tasks without editing each one.

## What it does

The **Hashtag Manager** is a workbench for every hashtag across your To Do tasks.

- An inventory of all your hashtags (task counts, lists touched, last used), with flags for near-duplicates, stale tags and hashtags spelled inconsistently across your tasks
- Rename, merge and one-click casing clean-up, always with a dry-run preview of the exact title changes before anything is written
- Changes run as background jobs with progress, cancel and undo
- Marker rules give a hashtag an emoji. They apply as a change like any other (previewed, queued and undoable), and an emoji left behind by a tag you removed can be taken off the same way
- An administrator can approve TodoWerk once for the whole organisation, so nobody else is asked at sign-in. That grants TodoWerk no access to anybody's tasks: it still acts as each person, with that person's own sign-in ([ADR-0008](docs/adr/0008-tenant-consent-is-delegated.md))
- A tenant can see how many colleagues use it and how much: counts only, never a name, never a per-person row, never one person's hashtags shown to another
- The same Workbench runs as a Microsoft Teams tab, signed in as your Teams identity

TodoWerk is for work and school (Entra ID) accounts only. Consumer Microsoft accounts are out of scope.

## How it is built

A .NET 10 backend in a Clean Architecture layout (`Domain`, `Application`, `Infrastructure` and `Web` over a `SharedKernel`), with a React and Fluent UI v9 single-page app served from the same host. Features are vertical modules (`Changes`, `Indexing`, `Licensing`, `Markers`, `Onboarding`) repeated as folders in each layer, and architecture tests fail the build if a layer or a module reaches somewhere it shouldn't. A sixth name, `Jobs`, is reserved: its folders exist in three layers and hold nothing, and `ModuleBoundaryTests` tracks it separately because a boundary check over an empty namespace passes for the wrong reason.

Every Microsoft Graph token stays on the server. The browser holds only a session cookie, because renames run as background jobs that keep calling Graph long after the tab is closed. That constraint and the others that shaped the design are written up in [docs/adr/](docs/adr/).

## Getting started

You can run TodoWerk from source or from the container image that CI pushes to GitHub's registry (`ghcr.io/cloudwerk/todowerk`) on every commit to `main`. Images carry no version tag yet: each one is tagged `sha-<commit>` as well as `latest`, and a running deployment reports the commit it was built from at `/version`. Pin a deployment to a `sha-` tag rather than `latest`. Running the image on your own server is written down in [docs/runbooks/deploying-a-self-host.md](docs/runbooks/deploying-a-self-host.md). Self-hosting is available as is, without a support commitment.

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node 22](https://nodejs.org), a SQL Server you can reach, and an Entra ID app registration in a tenant you can consent in. The exact versions, the app registration settings and the user-secrets to set are in [CONTRIBUTING.md § Development setup](CONTRIBUTING.md#development-setup), which is the one place they are kept so the two files cannot drift. Once you have configured those:

```bash
# from the repository root
(cd src/TodoWerk.Web/ClientApp && npm ci && npm run build)   # build the SPA once
dotnet test TodoWerk.slnx                                    # no live tenant needed; a SQL Server is
dotnet run --project src/TodoWerk.Web                        # https://localhost:7080
```

The test suites run on Microsoft.Testing.Platform, so pass `dotnet test` no VSTest-era flags. It forwards anything it does not recognise to the test host, and a rejected option surfaces as "Zero tests ran" instead of as an error. CONTRIBUTING § Testing has the detail.

Creating the database takes one `dotnet ef database update`, written out in CONTRIBUTING with everything else you need before the first run.

## Getting the Teams tab

TodoWerk ships as a **personal Teams tab**: the same Workbench, inside Teams, signed in as your Teams identity. There is no channel or group tab, because the Hashtag Manager works on one person's hashtags and has nothing to show a team.

The tab has been tested on Teams desktop, Teams on the web and Teams on Android. CloudWerk's Hosted Service is listed in the Microsoft Teams Store. For a Self-Host, a Teams administrator uploads an App Package to the organisation's own catalog: **Teams admin center → Teams apps → Manage apps → Upload new app**. The full procedure, including what a Self-Host has to build for itself, is in [docs/runbooks/teams-app-install.md](docs/runbooks/teams-app-install.md). What the Entra ID app registration needs first is in [docs/runbooks/teams-app-registration.md](docs/runbooks/teams-app-registration.md).

```bash
# Build a package for your own deployment
cp packaging/teams/values.self-host.json ./my-values.json   # then fill in your own values
pwsh ./scripts/New-TodoWerkTeamsAppPackage.ps1 -ValuesPath ./my-values.json
```

One known limitation before you roll it out: Teams on the web in Safari is expected not to hold a TodoWerk session, because Safari refuses the unpartitioned third-party cookie that the session lives in when it sits inside a frame. The tab detects this and offers to open TodoWerk in a browser tab, where everything works. That behaviour has never been observed. TodoWerk has not been verified in Safari, on macOS or on iOS, so the cookie behaviour and the card are a prediction. Teams desktop, Teams on Android and Teams on the web in Edge were tested and are unaffected. The reasoning is in [ADR-0010](docs/adr/0010-teams-tab-session-and-framing.md).

## Usage

Sign in with a work or school account and TodoWerk reads the hashtags out of your Microsoft To Do tasks into an index of its own and keeps that index current in the background. It shows you what you have: how often each hashtag is used, across how many lists, when it was last touched, and which ones look like mistakes because they are written more than one way, are nearly identical to another, or have not been edited in months.

To fix them, select a hashtag, or several to fold them together. TodoWerk shows the exact task titles it would rewrite before anything happens. It names any list it left out because it has not read that one end to end, and it asks a second time if the change would merge two hashtags into one. Confirming queues the change. It runs in the background, one task at a time, rewriting that hashtag and nothing else in the title. Every task it writes is journaled, so the whole change can be undone for 30 days. A task somebody edited in the meantime is left alone, because their edit wins.

You can also tell TodoWerk that a hashtag carries an emoji (`#bread` carries 🍞). It then puts that emoji at the front of every task with the tag, or takes it off again once the tag has gone. These are the only changes that write outside a hashtag, and they work like the others: previewed as the exact titles, queued and undoable. Neither runs on its own. Applying adds and reorders emoji and never takes one away, so an emoji whose hashtag you have since removed stays put until you run the separate removal change. TodoWerk only touches emoji it put there itself, never one you typed. Renaming a hashtag carries its emoji along.

[docs/status.md](docs/status.md) states what works, what does not, and where the code looks more finished than it is. That includes the two Teams tab cases that have not been verified: Safari, and the first run of a tenant that has not granted Tenant Consent.

## Project structure

```text
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
  entra/                   Logo and notes for the Entra ID app registration
  marketplace/             The listing icon
scripts/                   PowerShell 7 tools — build a Teams package, publish one, verify a flow
```

Technology: .NET 10, ASP.NET Core, Microsoft.Identity.Web, Microsoft Graph, EF Core over SQL Server, React 19, Fluent UI v9, Vite, TypeScript, xUnit v3, NetArchTest.

## Contributing

Work is cut into milestones that each leave something demoable, and the roadmap is in [docs/status.md](docs/status.md). Issues are filed per milestone as it approaches.

Start with [CONTRIBUTING.md](CONTRIBUTING.md). Read two other documents before you open a pull request: [CONTEXT.md](CONTEXT.md), which fixes the vocabulary the code and the UI share, and [docs/adr/](docs/adr/), which records the decisions already settled and why. Every pull request runs the build, unit, architecture and integration tests, and `main` accepts it only when the `build-and-test` and `cla` checks pass.

Because CloudWerk also operates a hosted service on this code, contributions require a [Contributor License Agreement](CLA.md). You keep your copyright, and a bot handles the signing on your first pull request.

- [Report a bug](https://github.com/Cloudwerk/todowerk/issues/new?template=bug_report.yml) · [Request a feature](https://github.com/Cloudwerk/todowerk/issues/new?template=feature_request.yml)
- [Code of Conduct](CODE_OF_CONDUCT.md)
- [Security policy](SECURITY.md): report vulnerabilities through private reporting, never the issue tracker
- [Privacy notice](PRIVACY.md): what TodoWerk stores, what reaches a server log, for how long, and how to have it deleted
- [Terms of use](TERMS.md): the terms every deployment serves, under its own operator's name

The application serves the privacy notice and the terms itself, at `/legal/privacy` and `/legal/terms`, so the document a consent dialog links to and the one in this repository are the same file. The two pages in [handbook/](handbook/) work the same way: `/about`, which the Teams admin center shows as the app's support link, and `/administrators`, written for the administrator deciding whether to allow TodoWerk ([ADR-0013](docs/adr/0013-the-handbook-is-split-by-kinship.md)).

## License

[AGPL-3.0](LICENSE). CloudWerk also operates a hosted service on this code. Running the same stack yourself is free and always will be: a Self-Host has no Licence, asks nobody, and is never refused.
