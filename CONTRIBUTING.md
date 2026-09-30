# Contributing to TodoWerk

Thanks for considering it. TodoWerk 1.0.2 is the first release and the Hashtag Manager
works, so the most useful contributions right now are bug reports against what is
there and proposals for the milestones that are coming.

Read [CONTEXT.md](CONTEXT.md) before you write anything. It fixes the vocabulary
that the code, the UI, and the issue tracker all use, and a pull request that
invents its own words for existing concepts will be asked to rename them.

## Before you open a pull request

- **Check the milestone.** Work is cut into vertical slices and listed in the
  [roadmap](docs/status.md#roadmap). Issues for a milestone are filed as it approaches.
  Raise a large change that belongs to a milestone nobody has started yet as an issue first.
- **Read the ADRs.** [docs/adr/](docs/adr/) records decisions that are settled.
  A pull request that contradicts one must not route around it silently: open an issue
  and make the case.
- **Sign the CLA.** See [CLA.md](CLA.md). The bot comments on your first pull request
  with the sentence to post, and nothing can be merged until you have posted it
  ([Pull request review](#pull-request-review) has the details).

## Reporting bugs and proposing features

Use the issue forms:
[bug report](https://github.com/Cloudwerk/todowerk/issues/new?template=bug_report.yml) or
[feature request](https://github.com/Cloudwerk/todowerk/issues/new?template=feature_request.yml).
Security vulnerabilities do **not** go in the issue tracker; see
[SECURITY.md](SECURITY.md).

A bug report is useful in proportion to how precisely someone else can reproduce it.
Say which build, which browser, whether you were in a work or school tenant, and what
the server logged.

## Development setup

This section is the one place that says how to build and run TodoWerk. The README links
here, so the port, the redirect URI and the Graph scope are written down only once.

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (`global.json`
pins the SDK with `latestFeature` roll-forward), [Node 22](https://nodejs.org)
(`.nvmrc` pins the exact version), a SQL Server you can reach, and an Entra ID app
registration in a tenant you can consent in.

Any SQL Server will do: LocalDB, a local install, or the official container.

```bash
docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='<a-strong-password>' \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2025-latest
```

Never use SQLite or EF InMemory. TodoWerk keeps its own index
([ADR-0003](docs/adr/0003-single-sql-store-own-index.md)), and a provider that only
resembles SQL Server accepts migrations and answers queries the real one would not.

Register the app as a web application, add a client secret, and grant the delegated
`Tasks.ReadWrite` permission. In the Entra admin center the path is **Entra ID → App
registrations → your app**, and from there **Manage → Authentication**, **Certificates &
secrets** and **API permissions** in turn. That navigation has no "Identity" node, even
if an older walkthrough mentions one.

If you are working on the Teams tab, the same registration needs three more things: an
Application ID URI of the form `api://<host>/<clientId>`, an exposed `access_as_user` scope, and
Microsoft's two Teams client ids pre-authorized against it. That procedure has its own document:
[docs/runbooks/teams-app-registration.md](docs/runbooks/teams-app-registration.md). It adds no
permission: the app still asks only for `Tasks.ReadWrite` and the OpenID scopes.

Add three redirect URIs, all under the **Web** platform:

- `https://localhost:7080/signin-oidc` for sign-in.
- `https://localhost:7080/signout-callback-oidc`, which is where `SignedOutCallbackPath` in
  `appsettings.json` points.
- `https://localhost:7080/auth/tenant-consent/callback`, which is where Microsoft returns an
  administrator who has approved TodoWerk for the whole tenant.

Nobody has tested whether Entra ID requires the second. The registration this was verified
against already had it, and nobody has signed out without it yet, so it stays in these
instructions until somebody proves it redundant. The third is required: Microsoft matches it
exactly, and a deployment without it gets a redirect-URI mismatch when an administrator tries
to approve. Each deployed origin needs its own equivalent of all three. To verify a
registration's consent flow end to end against a real tenant, walk
[docs/runbooks/tenant-consent-verification.md](docs/runbooks/tenant-consent-verification.md)
(`scripts/Verify-TenantConsent.ps1` guides it).

The portal lists the permission as `Tasks.ReadWrite`. The app asks for the same permission
fully qualified, as `https://graph.microsoft.com/Tasks.ReadWrite`, so that the resource is
stated and not inferred. It carries **Admin consent required: No**, so an ordinary member
consents for themselves at first sign-in. You do not need to administer the tenant to run
TodoWerk against it.

It is the write scope, and it is the only one: `Tasks.ReadWrite` includes `Tasks.Read`, so
asking for both would only make the grant look wider than it is. One prompt covers everything
TodoWerk does, including for a user who only ever looks at their inventory. That cost is
deliberate. Writes are performed by a background worker with no browser to prompt through
([ADR-0002](docs/adr/0002-backend-held-tokens.md)), so asking later would mean asking at the
moment someone confirms a destructive operation. See
[ADR-0007](docs/adr/0007-one-consent-grant.md).

If you upgrade a registration that already granted `Tasks.Read` alone, existing token-cache
entries cannot widen silently. MSAL raises `MsalUiRequiredException`, the gateway maps it to
reconnect-required, and the user signs in once more. Their background syncs stay stopped
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
strings on purpose, and the app fails at startup, not at the first request, when one is
missing. That includes the connection string, which startup also parses, so a mistyped one
stops the app too. Startup does not connect, though: a well-formed string that points at a
database that is down still starts the app, and the readiness check reports it in the log.

Create the database by applying the checked-in migrations. This is an explicit step
in every environment; nothing migrates itself at startup, because a web host that
migrates as it boots turns a rolling deploy into several instances altering the same
tables at once:

```bash
dotnet ef database update --project src/TodoWerk.Infrastructure --startup-project src/TodoWerk.Infrastructure
```

`dotnet ef` needs the tool (`dotnet tool install --global dotnet-ef`) and reads the
connection string from `ConnectionStrings__TodoWerk` in the environment, falling back
to LocalDB. It cannot read user-secrets: they belong to the web app and are not visible at
design time.

Build the client once, then run the backend:

```bash
cd src/TodoWerk.Web/ClientApp && npm ci && npm run build
cd .. && dotnet run
```

While working on the SPA, run `npm run dev` alongside the backend for hot reload. The
dev server proxies `/api` and `/auth`. Sign-in redirects land on the backend's
origin, so finish the flow there and reload the dev server afterwards.

### Changing the schema

Entity configurations live with their vertical module, in
`src/TodoWerk.Infrastructure/<Module>/Persistence`, and are discovered automatically. No
central file lists them. Every persisted row carries a tenant id, and every per-person row
also carries a user id. An architecture test fails the build for an entity that omits either,
because a migration that adds those columns to a populated table has no correct value to
backfill. A row that belongs to a tenant and to no particular person is named in that test's
own short list instead of getting an invented user id. Today that is only the Tenant Consent
grant, which deliberately does not record which administrator approved. The Tenant Member's
user id is nullable for a related reason: erasure removes it in place, so the row survives
and names nobody.

Two kinds of column set their collation explicitly instead of taking the database default.
A column that holds an id issued by Microsoft Graph, such as a task id or a list id, takes
`StorageConventions.GraphIdCollation`. Graph writes these ids as case-sensitive base64, and
the default collation is case-insensitive, so two tasks whose ids differ only in the case of
one letter become one value. The unique index over them reports a duplicate key, and the
affected list never finishes indexing. An architecture test fails the build when a Graph id
column lacks the collation. It finds those columns by the property names `GraphTaskId` and
`TaskListId`, so a new Graph id under another name needs adding to it.

The folded Hashtag key and the Spelling take `StorageConventions.KeyCollation`, for the
reasons [ADR-0005](docs/adr/0005-what-a-hashtag-is.md) gives. No architecture test watches
those columns; an integration test,
`HashtagInventoryTests.EszettAndDoubleS_AreTwoRows`, checks against real SQL Server that
`#Straße` and `#Strasse` stay two Hashtags. The rule is the same for both kinds: if C# decides
what "equal" means for a column, the column has to compare the bytes C# produced.

The one exception is a table owned by an infrastructure library, not by the domain. Today
that is only `dbo.TokenCache`, behind the distributed token cache. Such a table takes exactly
the schema its library dictates, stays out of the EF model (which is why the architecture
test does not see it) and arrives by a hand-written migration. It must carry tenant and user
some other way: the token cache keys its rows by MSAL's `uid.utid`, and everything else in
them is ciphertext.

```bash
dotnet ef migrations add <Name> --project src/TodoWerk.Infrastructure \
  --startup-project src/TodoWerk.Infrastructure --output-dir Persistence/Migrations
dotnet format TodoWerk.slnx
```

Always run `dotnet format` afterwards: `dotnet ef` writes CRLF line endings, a byte-order
mark and block-scoped namespaces, and the format check rejects all three. Read the generated
migration before you commit it. CI puts a warning on every pull request that adds one, asking
you to confirm that the change is reversible and safe to apply to a database that already
has rows in it.

Pulling a branch that adds one? Apply it before running the app:

```bash
dotnet ef database update --project src/TodoWerk.Infrastructure
```

Nothing applies migrations for you: not the app at startup, and not the tests against your
own database. The app does tell you. Shortly after startup it logs the missing migrations by
name, with this exact command, so you do not have to find `Invalid object name` in a
background worker's stack trace every fifteen seconds.

### Configuration outside Development

Development needs none of this: the app falls back to a per-user key ring, and no proxy
sits in front of it. Anywhere else, three settings decide whether it starts and whether it
is safe:

| Setting | Why the app insists |
| --- | --- |
| `DataProtection:KeyRingPath` | Startup fails without it. The keys encrypt session cookies, so an instance-local key ring signs everyone out on every deploy. |
| `DataProtection:CertificatePath` (and `:CertificatePassword`) | Startup fails without it. The key ring is written encrypted, because those keys also protect the Graph token cache and on Linux the default is plain XML on disk. When you rotate the certificate, name the outgoing one as `PreviousCertificatePath` so existing keys stay readable. |
| `ForwardedHeaders:KnownProxies` / `:KnownNetworks` | Needed only behind a TLS-terminating proxy, and required there. Without it the app sees plain HTTP and redirects a request that already arrived over TLS, and every caller looks like the proxy, so the whole deployment shares one rate-limit bucket. It names the proxies on purpose: trusting any proxy would let a caller choose their own address. |

Two settings in the `Onboarding` section have defaults. Both are promises, not tuning, so
read what they mean before you change them:

| Setting | What changing it means |
| --- | --- |
| `Onboarding:StatisticsFloor` | How many identifiable people must be on record before the Tenant Overview shows any statistics. A person who has been forgotten counts toward the total shown, never toward the floor. Five by default. Lowering it means a total across fewer people, and in a small tenant a total combined with what one reader already knows is an inference about identifiable colleagues ([ADR-0009](docs/adr/0009-what-todowerk-stores-about-a-person.md)). |
| `Onboarding:DormancyWindow` | How long somebody may go without signing in before TodoWerk destroys everything it holds about them. Twelve months by default. The overview's longest activity window is computed from the same value, so the two cannot drift apart. Shortening it shortens the retention promise in [PRIVACY.md](PRIVACY.md), and that document does not change when the setting does. |

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
`EnforceCodeStyleInBuild` are on for every project, so a style violation fails the
build before anyone reviews it. Before pushing:

```bash
dotnet format TodoWerk.slnx --verify-no-changes
cd src/TodoWerk.Web/ClientApp && npm run typecheck
```

CI runs the same `dotnet format` check and fails the pull request on it.
`.gitattributes` normalises line endings to LF everywhere, which is what stops the check
from disagreeing between a Windows workstation and the Linux runner.

`.editorconfig` is the authority for C#. Do not add per-file suppressions to get a
build green — either fix the code or make the case for changing the rule.

For TypeScript: strict mode is on, `any` needs a justification in review, and
components use Fluent UI v9 primitives on the CloudWerk brand theme rather than
hand-rolled styling.

### What a failure is allowed to say

Text a user reads must not name a cause the code does not know. Whoever gets a message
written at a catch-all reads it as a diagnosis, and a wrong diagnosis is worse than a vague
one. "This list could not be read" once sat on a handler that also caught write failures. It
cost an hour spent on Graph, on permissions and on a list's well-known kind, when the cause
was a duplicate key on an insert. Where the cause is known (Graph said so, a token expired),
word the message there and let it travel; the messages for everything TodoWerk and Graph do
to each other live together in `GraphErrors`. Where the cause is not known, say that. The
client adds nothing to a reason it was handed: wrapping the server's sentence in one of its
own says the same thing twice, the second time on the authority of code that knows even less.

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

**Do not boot the application to watch it refuse to start.** A test that expects startup
validation to reject a setting asks `IStartupValidator` directly
(`ConfigurationValidationTests.ApplicationServicesWith` is the pattern) and never wraps
`factory.CreateClient` in a throw assertion. Under `WebApplicationFactory` the entry point runs
on its own thread, and startup validation runs there, inside `RunAsync`, which disposes the host
in its `finally`. Whether `CreateClient` throws, and what it throws, then depends on thread
scheduling and not on the setting under test, so such a test passes on a quiet machine and
fails on a busy one. `TestConventionTests` fails the build on a test of that shape, so the rule
does not depend on anybody remembering it.

The .NET suites use xunit.v3, which runs on Microsoft.Testing.Platform, and the .NET 10 SDK
will not run such a project through VSTest at all. The `test` section in `global.json` selects
the platform for the whole repository; that file takes no comments, so the reason is written
here. Test arguments belong to the platform, not to VSTest, so an option copied from an older
pipeline may need translating.

Integration tests supply the OpenID Connect discovery document themselves, so no
test contacts a live tenant and none ever should. Integration tests that need a
database run against real SQL Server, never SQLite or EF InMemory. They take the
server from `ConnectionStrings__TodoWerk` in the environment (CI points it at an
ephemeral container) and fall back to LocalDB, then create and drop their own database
per run, so a test never touches the one you develop against. Tests that do not need a
database are pointed at an unreachable server on purpose: needing one is something a
test asks for explicitly.

`npm run build` also checks the built bundles. This is a check of correctness, not of size.
The browser SPA must contain no TeamsJS: one shared component that calls `app.openLink()` would
put a Teams handshake into every browser page load. The consent popup's landing document must
contain no framework, because Teams gives an authentication popup a limited time to report back,
and a document that spends it parsing React reports a completed flow as cancelled. The check
reads the emitted chunks, not the import graph, on purpose.

Since M2 the client has had its own suite (Vitest and Testing Library), run by `npm test`
and by CI:

```bash
cd src/TodoWerk.Web/ClientApp && npm test
```

The suite is for state the server has not confirmed yet: a target being typed, a preview one
keystroke out of date, a Change queued but not started, a merge warning that has to be the
server's verdict and not the browser's guess. That state arrived with the M2 change queue, so
the suite did too, and not with the M1 Workbench. Everything the inventory table decides (the
sort, the filter, the page, which flags a row carries) is decided by the query behind it and
tested there; the grid renders what it is handed.

The bar for a client change is `npm run typecheck`, plus a test when the change touches that
held state. Do not write a test that mostly exercises Fluent UI. When what you want to assert
is what the API returned, assert it in the integration tests.

**Wait for the thing you are about to assert**, not for something that arrives earlier.
`await findByTestId('status')` returns as soon as that element exists, while the read behind it
is still in flight, so the assertion after it passes or fails by chance.

**Do not query inside a Fluent *modal* by role after an `await`.** Fluent hands a modal to
tabster, which decides what assistive technology can see based on where the focus is. Tabster
measures focusability with `getBoundingClientRect`, and jsdom lays nothing out, so nothing is
ever focusable and a dialog opened from a menu never becomes the active modal. A quarter of a
second later tabster marks the dialog `aria-hidden` and leaves it that way. From then on every
`*ByRole` query into it fails as a missing element, on any machine busy enough to have spent
that quarter second. Text queries do not consult the accessibility tree and are unaffected. So
is a dialog rendered already open, with no menu before it, which is why
`EraseMeDialog.test.tsx` may query by role and `App.test.tsx` may not.

## Pull request review

Open the pull request against `main` and fill in the template. `main` is protected: a pull
request can merge only when two required checks pass.

- `build-and-test` runs the PR validation workflow: the `dotnet format` check, the client
  build and client tests, the .NET build, and the unit, architecture and integration tests.
  It needs no secrets, so it runs the same on a pull request from a fork.
- `cla` passes when everyone with a commit in the pull request has signed the CLA.

A red build will not be reviewed. A pull request that adds an EF migration also gets a
warning from the `migration-check` job ([Changing the schema](#changing-the-schema) says why);
the warning does not block the merge. Nobody can force-push to `main` or delete it, and only
repository administrators can push to it without a pull request.

The first time you open a pull request, the `cla` check fails and a bot comments with a link
to [CLA.md](CLA.md). To sign, reply to the pull request with this sentence:

```text
I have read the CLA Document and I hereby sign the CLA
```

The bot records your GitHub username as a signature on this repository's `cla-signatures`
branch and re-runs the `cla` check, so you do not need to push again. You sign once, and the
check passes on your later pull requests. CloudWerk maintainers and the repository's bots are
on an allowlist and do not sign.

Expect direct review. Comments are about the code. A maintainer who disagrees with an
approach is not rejecting the work; if you have a counter-argument, make it. Pull requests
are squash-merged, so the pull request title becomes the commit message: make it a valid
Conventional Commit.

## License

By contributing you agree that your contribution is licensed under
[AGPL-3.0](LICENSE), and you grant CloudWerk GmbH the additional rights set out in
[CLA.md](CLA.md).
