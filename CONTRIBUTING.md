# Contributing to TodoWerk

Thanks for considering it. TodoWerk is early — the Hashtag Manager works, and there is
no release yet — so the most useful contributions right now are bug reports against
what is there, and discussion on the milestones that are coming.

Read [CONTEXT.md](CONTEXT.md) before you write anything. It fixes the vocabulary
that the code, the UI, and the issue tracker all use, and a pull request that
invents its own words for existing concepts will be asked to rename them.

## Before you open a pull request

- **Check the milestone.** Work is cut into vertical slices, listed in the
  [roadmap](docs/status.md#roadmap) and filed as issues per milestone as it approaches. A large change that belongs to a milestone
  nobody has started yet is better raised as an issue first.
- **Read the ADRs.** [docs/adr/](docs/adr/) records decisions that are settled.
  A pull request that contradicts one should argue with it — open an issue and make
  the case — rather than route around it silently.
- **Sign the CLA.** See [CLA.md](CLA.md). The bot will comment on your first pull
  request with a one-line signing instruction. Nothing can be merged before that.

## Reporting bugs and proposing features

Use the issue forms —
[bug report](https://github.com/Cloudwerk/todowerk/issues/new?template=bug_report.yml) or
[feature request](https://github.com/Cloudwerk/todowerk/issues/new?template=feature_request.yml).
Security vulnerabilities do **not** go in the issue tracker; see
[SECURITY.md](SECURITY.md).

A bug report is useful in proportion to how precisely someone else can reproduce it.
Say which build, which browser, whether you were in a work or school tenant, and what
the server logged.

## Development setup

This section is the single source for how to build and run TodoWerk; the README links
here rather than repeating it, so the port, the redirect URI, and the Graph scope are
only ever written down once.

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (`global.json`
pins the SDK with `latestFeature` roll-forward), [Node 22](https://nodejs.org)
(`.nvmrc` pins the exact version), a SQL Server you can reach, and an Entra ID app
registration in a tenant you can consent in.

Any SQL Server will do: LocalDB, a local install, or the official container —

```bash
docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='<a-strong-password>' \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2025-latest
```

— but never SQLite and never EF InMemory. TodoWerk keeps its own index
([ADR-0003](docs/adr/0003-single-sql-store-own-index.md)), and a provider that only
resembles SQL Server accepts migrations and answers queries the real one would not.

Register the app as a web application, add a client secret, and grant the delegated
`Tasks.ReadWrite` permission. In the Entra admin center the path is **Entra ID → App
registrations → your app**, and from there **Manage → Authentication**, **Certificates &
secrets** and **API permissions** in turn. There is no "Identity" node in that navigation,
whatever an older walkthrough tells you.

If you are working on the Teams tab, the same registration needs three more things — an
Application ID URI of the form `api://<host>/<clientId>`, an exposed `access_as_user` scope, and
Microsoft's two Teams client ids pre-authorized against it. That procedure is its own document:
[docs/runbooks/teams-app-registration.md](docs/runbooks/teams-app-registration.md). It adds no
permission: the scope surface stays `Tasks.ReadWrite` plus the OpenID scopes.

Three redirect URIs, all under the **Web** platform: `https://localhost:7080/signin-oidc`
for sign-in, `https://localhost:7080/signout-callback-oidc`, which is where
`SignedOutCallbackPath` in `appsettings.json` points, and
`https://localhost:7080/auth/tenant-consent/callback`, which is where Microsoft returns an
administrator who has approved TodoWerk for the whole tenant. Whether Entra ID insists on the
second is untested — the registration this was verified against already carried it, and
nobody has yet signed out without it — so it stays in these instructions until somebody
proves it redundant. The third is not optional: Microsoft matches it exactly, and a deployment
that forgets it gets a redirect-URI mismatch at the moment an administrator tries to approve —
each deployed origin needs its own equivalent of all three. To verify a registration's consent
flow end to end against a real tenant, walk
[docs/runbooks/tenant-consent-verification.md](docs/runbooks/tenant-consent-verification.md)
(`scripts/Verify-TenantConsent.ps1` guides it).

The portal lists the permission as `Tasks.ReadWrite`; the app asks for it fully qualified, as
`https://graph.microsoft.com/Tasks.ReadWrite`, so the resource is stated rather than inferred.
Same permission, spelled for a different reader. It carries **Admin consent required: No**,
so an ordinary member consents for themselves at first sign-in — administering the tenant
is not a prerequisite for running TodoWerk against it.

It is the write scope, and it is the only one: `Tasks.ReadWrite` subsumes `Tasks.Read`, so
asking for both would only make the grant look wider than it is. One prompt, once, covering
everything TodoWerk does — including for a user who only ever looks at their inventory. That
is a real cost, taken deliberately: the thing that performs a write is a background worker
with no browser to prompt through ([ADR-0002](docs/adr/0002-backend-held-tokens.md)), so
asking later would mean asking at the confirmation click on a destructive operation. See
[ADR-0007](docs/adr/0007-one-consent-grant.md).

Upgrading a registration that already granted `Tasks.Read` alone? Existing token-cache
entries cannot silently widen. MSAL raises `MsalUiRequiredException`, the gateway maps it to
reconnect-required, and the user signs in once more — with their background syncs stopped
until they do.

Then:

```bash
cd src/TodoWerk.Web
dotnet user-secrets set "EntraId:TenantId" "<your-tenant-id>"
dotnet user-secrets set "EntraId:ClientId" "<your-client-id>"
dotnet user-secrets set "EntraId:ClientSecret" "<your-client-secret>"
# Only if you are working on the Teams tab. Absent means the tab's exchange endpoint accepts nothing.
dotnet user-secrets set "EntraId:ApplicationIdUri" "api://<your-host>/<your-client-id>"
dotnet user-secrets set "ConnectionStrings:TodoWerk" "Server=(localdb)\MSSQLLocalDB;Database=TodoWerk;Trusted_Connection=True;TrustServerCertificate=True"
```

Never put those values in `appsettings.json`. The checked-in file ships empty
strings on purpose, and the app now fails at startup rather than at first request
when they are missing — the connection string included, which is also checked for being
a connection string rather than merely for being there. A typo in that setting used to
let the app announce its ports and then fail every fifteen seconds in a background
worker instead. What startup still does not do is connect: a well-formed
string pointing at a database that is down starts the app, and the readiness check says
so in the log.

Create the database by applying the checked-in migrations. This is an explicit step
in every environment; nothing migrates itself at startup, because a web host that
migrates as it boots turns a rolling deploy into several instances altering the same
tables at once:

```bash
dotnet ef database update --project src/TodoWerk.Infrastructure --startup-project src/TodoWerk.Infrastructure
```

`dotnet ef` needs the tool (`dotnet tool install --global dotnet-ef`) and reads the
connection string from `ConnectionStrings__TodoWerk` in the environment, falling back
to LocalDB — user-secrets belong to the web app and are not visible at design time.

Build the client once, then run the backend:

```bash
cd src/TodoWerk.Web/ClientApp && npm ci && npm run build
cd .. && dotnet run
```

While working on the SPA, run `npm run dev` alongside the backend for hot reload —
the dev server proxies `/api` and `/auth`. Sign-in redirects land on the backend's
origin, so finish the flow there and reload the dev server afterwards.

### Changing the schema

Entity configurations live with their vertical module, in
`src/TodoWerk.Infrastructure/<Module>/Persistence`, and are discovered automatically —
there is no central file listing them. Every persisted row carries a tenant id, and every
per-person row carries a user id too; an architecture test fails the build for an entity that
omits either, because a migration adding those columns to a populated table has no correct
value to backfill. A row that genuinely belongs to a tenant and to nobody in particular — today
only the Tenant Consent grant, which deliberately does not record which administrator
approved — is named in that test's own short list rather than given an invented user id.
The Tenant Member's user id is nullable for a related reason: erasure removes it in place,
so the row survives naming nobody.

Two kinds of column are collated explicitly rather than left to the database, and both
carry an architecture test that fails the build when a new one forgets. A column holding
an id Microsoft Graph issued — a task id, a list id — takes
`IndexingSchema.GraphIdCollation`, because Graph writes them as case-sensitive base64
and the default collation is case-insensitive: two different tasks whose ids differ by
one letter's case become one value, which the unique index over them reports as a
duplicate key and the affected list then never finishes indexing. The folded
Hashtag key and the Spelling take `IndexingSchema.KeyCollation` for the reasons
[ADR-0005](docs/adr/0005-what-a-hashtag-is.md) gives. Either way the rule is the same: if
C# decides what "equal" means for a column, the column has to compare the bytes C#
produced.

The one exception is a table owned by an infrastructure library rather than by the
domain — today that is only `dbo.TokenCache`, behind the distributed token cache. Such
a table takes exactly the schema its library dictates, stays out of the EF model (which
is why the architecture test does not see it), arrives by hand-written migration, and
must carry tenant and user some other way: the token cache keys its rows by MSAL's
`uid.utid` and everything else in them is ciphertext.

```bash
dotnet ef migrations add <Name> --project src/TodoWerk.Infrastructure \
  --startup-project src/TodoWerk.Infrastructure --output-dir Persistence/Migrations
dotnet format TodoWerk.slnx
```

Run `dotnet format` afterwards, always: `dotnet ef` writes CRLF, a byte-order mark, and
block-scoped namespaces, all three of which the format gate rejects. Read the generated
migration before committing it — CI comments on every pull request that adds one, and
the question it is asking is whether the change is reversible and safe against a
database that already has rows in it.

Pulling a branch that adds one? Apply it before running the app:

```bash
dotnet ef database update --project src/TodoWerk.Infrastructure
```

Nothing applies migrations for you — not the app at startup, not the tests against your
own database. What the app does do is say so: it logs the missing migrations by name and
this exact command shortly after startup, rather than leaving you to read
`Invalid object name` out of a background worker's stack trace every fifteen seconds.

### Configuration outside Development

Development needs none of this — the app falls back to a per-user key ring and talks to
you directly. Anywhere else, three settings decide whether it starts and whether it is
safe, so they refuse to be forgotten:

| Setting | Why the app insists |
| --- | --- |
| `DataProtection:KeyRingPath` | Startup fails without it. The keys encrypt session cookies, so an instance-local key ring signs everyone out on every deploy. |
| `DataProtection:CertificatePath` (and `:CertificatePassword`) | Startup fails without it. The key ring itself is written encrypted — those keys also protect the Graph token cache, and on Linux the default is plain XML on disk. Rotating? Name the outgoing one as `PreviousCertificatePath` so existing keys stay readable. |
| `ForwardedHeaders:KnownProxies` / `:KnownNetworks` | Only needed behind a TLS-terminating proxy, and then it is needed: without it the app sees plain HTTP and redirects a request that already arrived over TLS, and every caller looks like the proxy — one rate-limit bucket for the whole deployment. Deliberately not "trust any proxy", which would let a caller choose their own address. |

Two settings in the `Onboarding` section have defaults but are worth knowing about before
changing them, because both are promises rather than tuning:

| Setting | What changing it means |
| --- | --- |
| `Onboarding:StatisticsFloor` | How many identifiable people must be on record before the Tenant Overview shows any statistics at all — somebody who has been forgotten counts toward the total shown, never toward the floor. Five by default. Lowering it means a total across fewer people, and in a small tenant a total plus one reader's own knowledge is an inference about identifiable colleagues ([ADR-0009](docs/adr/0009-what-todowerk-stores-about-a-person.md)). |
| `Onboarding:DormancyWindow` | How long somebody may go without signing in before TodoWerk destroys everything it holds about them. Twelve months by default, and the same value the overview's longest activity window is computed from — so the two cannot drift apart. Shortening it shortens the retention promise in [PRIVACY.md](PRIVACY.md), which is a document rather than a setting. |

## Branching and commits

Branch from `main`. There is no long-lived develop branch and no release branches.

Name branches for what they do, with the same prefix you will use in the commit:
`feat/hashtag-inventory`, `fix/antiforgery-ordering`, `docs/adr-0005`.

Commits follow [Conventional Commits](https://www.conventionalcommits.org):

```text
feat: rename a hashtag across every list
fix: sort task lists by ordinal rather than by culture
docs: record M0 as shipped
chore: bump Fluent UI to 9.75
```

Keep the subject in the imperative and under about 72 characters. The body is for
why, not what — the diff already says what.

## Code style

The build is strict by design: `TreatWarningsAsErrors`, `AnalysisMode=All`, and
`EnforceCodeStyleInBuild` are on for every project, so style violations fail the
build rather than the review. Before pushing:

```bash
dotnet format TodoWerk.slnx --verify-no-changes
cd src/TodoWerk.Web/ClientApp && npm run typecheck
```

CI runs that same `dotnet format` check, so it is a gate rather than a suggestion.
`.gitattributes` normalises line endings to LF everywhere, which is what stops the check
from disagreeing between a Windows workstation and the Linux runner.

`.editorconfig` is the authority for C#. Do not add per-file suppressions to get a
build green — either fix the code or make the case for changing the rule.

For TypeScript: strict mode is on, `any` needs a justification in review, and
components use Fluent UI v9 primitives on the CloudWerk brand theme rather than
hand-rolled styling.

### What a failure is allowed to say

Text a user reads must not name a cause the code does not have. A message written at a
catch-all is read as a diagnosis by whoever gets it, and a wrong one is worse than a
vague one: "This list could not be read" sat on a handler that also caught write
failures, and cost an hour spent on Graph, on permissions and on a list's well-known
kind for what was a duplicate key on an insert. Where the cause is known —
Graph said so, a token expired — word it there and let it travel; the messages for
everything TodoWerk and Graph do to each other live together in `GraphErrors`. Where it
is not known, say that. The client adds nothing to a reason it was handed: wrapping the
server's sentence in one of its own asserts the same thing twice, the second time on the
authority of code that knows even less.

## Testing

Every behavioural change needs a test. The suites, and what belongs in each:

| Suite | What goes in it |
| --- | --- |
| `tests/TodoWerk.UnitTests` | Domain and application logic, in isolation |
| `tests/TodoWerk.ArchitectureTests` | Layer and module boundaries — these fail the build when the structure erodes |
| `tests/TodoWerk.IntegrationTests` | The real application booted end to end, including the real authentication pipeline |
| `src/TodoWerk.Web/ClientApp/src/**/*.test.ts(x)` | Client state the server has not confirmed yet — see below |

```bash
dotnet test TodoWerk.slnx

# One class, when you are iterating on it
dotnet test tests/TodoWerk.IntegrationTests --filter-class TodoWerk.IntegrationTests.VersionEndpointTests
```

**Pass no VSTest-era flags.** This repository runs on Microsoft.Testing.Platform, and `dotnet test`
forwards any argument it does not recognise to the test host, which rejects it. The host's own error
never reaches you: what you see instead is `Zero tests ran` and exit code 5, for every project, as
though the repository contained no tests. `dotnet test TodoWerk.slnx --nologo` reports zero, and the
same command without `--nologo` runs every test in the repository. If a run reports zero tests, take
your options off before you suspect anything else.

**Do not boot the application to watch it refuse to start.** A test that expects startup validation
to reject a setting asks `IStartupValidator` directly — `ConfigurationValidationTests.ApplicationServicesWith`
is the pattern — and never wraps `factory.CreateClient` in a throw assertion. Under
`WebApplicationFactory` the entry point runs on its own thread and startup validation runs there,
inside `RunAsync`, which disposes the host in its `finally`; whether `CreateClient` throws, and what
it throws, is then decided by scheduling rather than by the setting under test, so a test written
that way passes on a quiet machine and fails on a busy one. `TestConventionTests` fails the build on the shape, so the rule does not rest on anybody remembering
it.

The .NET suites are xunit.v3, which runs on Microsoft.Testing.Platform rather than VSTest,
and the .NET 10 SDK will not run such a project through VSTest at all. The `test` section
in `global.json` is what selects the platform for the whole repository — the file takes no
comments, so the reason lives here. Test-side arguments are the platform's own rather than
VSTest's, so an option carried over from an older pipeline may need translating.

Integration tests supply the OpenID Connect discovery document themselves, so no
test contacts a live tenant and none ever should. Integration tests that need a
database run against real SQL Server — never SQLite, never EF InMemory. They take the
server from `ConnectionStrings__TodoWerk` in the environment (CI points it at an
ephemeral container) and fall back to LocalDB, then create and drop their own database
per run, so a test never touches the one you develop against. Tests that do not need a
database are pointed at an unreachable server on purpose: needing one is something a
test asks for explicitly.

`npm run build` also checks the built bundles, which is a correctness check rather than a size
one: the browser SPA must contain no TeamsJS — one shared component reaching for `app.openLink()`
would put a Teams handshake into every browser page load — and the consent popup's landing document
must contain no framework, because Teams gives an authentication popup a bounded time to report
back and a document that spends it parsing React reports a completed flow as cancelled. It reads
the emitted chunks rather than the import graph, deliberately.

The client has had its own suite since M2 — Vitest and Testing Library, run by `npm test`
and by CI:

```bash
cd src/TodoWerk.Web/ClientApp && npm test
```

It arrived with the change queue rather than with the M1 Workbench, and that difference is
the whole rule. Everything the inventory table decides — the sort, the filter, the page,
which flags a row carries — is decided by the query behind it and tested there; the grid
renders what it is handed. What M2 added is state the server has not confirmed yet: a target
being typed, a preview one keystroke out of date, a Change queued but not started, a merge
warning that has to be the server's verdict rather than the browser's guess. That is what
the suite is for.

So the bar for a client change is still `npm run typecheck`, plus a test when the change
touches that held state. A test that mostly exercises Fluent UI is not wanted; when the
thing worth asserting is what the API returned, assert it in the integration tests instead.

**Two rules about waiting.** Wait for the thing you
are about to assert, not for something that arrives earlier — `await findByTestId('status')`
returns the moment that element exists, which is while the read behind it is still in flight, so
the assertion after it is a coin toss rather than a test. And do not reach inside a Fluent *modal*
by role after an `await`. Fluent hands a modal to tabster, which decides what is exposed to
assistive technology from where the focus is; tabster measures focusability with
`getBoundingClientRect`, jsdom lays nothing out, so nothing is ever focusable and a dialog opened
from a menu never becomes the active modal. A quarter of a second later tabster marks it
`aria-hidden` and leaves it that way, and every `*ByRole` query into it fails for good — as a
missing element, on a machine busy enough to have spent that quarter second. Text queries do not
consult the accessibility tree and are unaffected; a dialog rendered already open, with no menu
before it, is unaffected too, which is why `EraseMeDialog.test.tsx` may ask by role and
`App.test.tsx` may not.

## Pull request review

Open the pull request against `main` and fill in the template. CI runs build, unit,
architecture, and integration tests on every pull request; a red build will not be
reviewed.

Expect review to be direct. Comments are about the code, and a maintainer
disagreeing with an approach is not a rejection of the work — make the counter-case
if you have one. Pull requests are squash-merged, so the pull request title becomes
the commit message: make it a valid Conventional Commit.

## License

By contributing you agree that your contribution is licensed under
[AGPL-3.0](LICENSE), and you grant CloudWerk GmbH the additional rights set out in
[CLA.md](CLA.md).
