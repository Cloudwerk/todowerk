# Deploying TodoWerk to a host of your own

What a server of your own needs before TodoWerk runs on it, in the order the steps depend on each
other. A failure in one step usually stays hidden until the step before it is right, so follow the
steps in order.

Two of the failures below have the same cause, and it is the fact to remember:
**the container runs as UID 1654, and every piece of host-provided storage it touches must be owned
by 1654.**

Sections 8 and 9 fail differently from the sections before them. A mistake in an earlier section
stops the deployment: it does not start, or it answers 500, and you find out within a minute. A
mistake in section 8 or 9 leaves a deployment that looks healthy. A wrong `EntraId:TenantId` lets
your own tenant sign in while every other tenant is refused.

## What you need

- A host with a public DNS name you control. Not an auto-generated hosting hostname: the name goes
  into the Teams Application ID URI, and Teams single sign-on allows one domain per app
  registration ([ADR-0011](../adr/0011-two-app-packages-and-a-template.md)), so changing it later
  means every tenant that installed the App Package reinstalls.
- A reverse proxy that terminates TLS with a publicly trusted certificate. Teams refuses a
  self-signed one and says nothing useful about why.
- SQL Server. Not SQLite, not EF InMemory. Express is the free edition licensed for production and
  caps a database at 10 GB; Developer is unrestricted and licensed for development and test only,
  which a customer trial is not.
- The container image, and credentials to pull it if the package is private.

## 1. Open the ports — check for two firewalls, not one

A cloud host usually has a firewall of its own *and* one inside the machine, and a control panel
shows you only the first. Both must permit 80 and 443.

The panel's firewall may also distinguish **configured** from **applied**: rules that read correctly
and have never been pushed to the machine look identical to rules that are in force. Look for a
sync or activate action, and do not trust the rule list alone.

Inside the machine:

```bash
ufw status verbose
ufw allow 80/tcp && ufw allow 443/tcp
```

Test from another machine: run `curl` from your own computer, not from the server. From localhost,
a proxy that is unreachable from outside looks the same as one that is running and correctly
configured.

## 2. Give the container a key ring and a certificate it can read

Outside Development the application refuses to start without a persisted Data Protection key ring
*and* a certificate to encrypt it with. Persisting the key ring without encrypting it would leave
the keys that protect every session cookie and the whole Graph token cache sitting on disk as plain
XML.

The certificate never faces the network (the reverse proxy owns TLS), so generate your own and keep
it private:

```bash
mkdir -p <secrets-dir>
openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
  -subj "/CN=TodoWerk Data Protection" -keyout /tmp/dp.key -out /tmp/dp.crt
openssl pkcs12 -export -out <secrets-dir>/dataprotection.pfx \
  -inkey /tmp/dp.key -in /tmp/dp.crt -passout pass:'<a password you keep>'
rm -f /tmp/dp.key /tmp/dp.crt
```

Then give both the certificate and the key ring to UID 1654, the non-root user the .NET image runs
as. Do not use `chmod 600` here: it means *root only*, and the application does not run as root.

```bash
# Confirm the uid rather than trusting this document
docker inspect --format '{{.Config.User}}' ghcr.io/cloudwerk/todowerk:sha-<commit>

chown 1654:1654 <secrets-dir>/dataprotection.pfx
chmod 640 <secrets-dir>/dataprotection.pfx

# A named volume is created root-owned and mode 755, so the key ring needs the same treatment.
# Data Protection's first act is to *write* a key, and it cannot.
docker run --rm -v <project>_todowerk-keyring:/keyring alpine chown -R 1654:1654 /keyring
```

If the certificate's ownership is wrong, the container stops before it listens, with a
`CryptographicException` whose inner exception is `Permission denied`. That message points you at
the password, which is not the cause. If the key ring's ownership is wrong, the result is worse: the
application starts, reports itself healthy, and answers **500 to every request including
`/health`**, because antiforgery needs a key on every safe GET.

## 3. Run it as something other than Development

In a deployment, `ASPNETCORE_ENVIRONMENT=Development` breaks three things, and nothing reports any
of them:

- The session cookies stay `SameSite=Lax` instead of `SameSite=None; Secure`
  ([ADR-0010](../adr/0010-teams-tab-session-and-framing.md)). A Lax cookie is absent inside the
  Teams frame, so the tab signs a person in and then shows the blocked-cookie card on every load.
  Safari produces the same symptom for a different reason.
- No content security policy is sent at all, so `frame-ancestors` is never exercised.
- User secrets are loaded, which is how a deployment ends up depending on a developer's machine.

## 4. Trust the reverse proxy, or nothing works over HTTPS

The proxy talks plain HTTP to the container and says so in `X-Forwarded-Proto`. TodoWerk believes
that header only from proxies named in configuration, because believing it from anyone lets a
caller choose their own address and their own rate-limit bucket.

```ini
ForwardedHeaders__KnownNetworks__0=<container-subnet>      # the container network's subnet
```

Pin the subnet in your compose file. If you accept whatever Docker allocates, the setting matches
only by chance. If the setting is missing, the application sees plain HTTP, and
`UseHttpsRedirection` redirects every request to a port the proxy is not forwarding. A quieter
failure follows: the OpenID Connect redirect URI comes out as `http://`, which fails at Entra ID,
not at your proxy.

Verify it from outside without signing in:

```bash
curl -sD - -o /dev/null https://<host>/auth/sign-in | grep -i location
```

The `redirect_uri` in that URL must read `https://`.

## 5. Create the database yourself

**TodoWerk never creates or migrates its own database.** `DatabaseReadinessCheck` reports pending
migrations and applies nothing, deliberately ([ADR-0003](../adr/0003-single-sql-store-own-index.md)):
an application that quietly migrates whatever database it happens to connect to is how a deploy
rewrites a schema nobody agreed to.

A fresh deployment therefore fails with `Cannot open database "TodoWerk" requested by the login`.
This is SQL Server error 4060: the login succeeded and the database is missing. Error 18456 means a
bad password. Do not change credentials because of a 4060.

On a machine with the source, generate an idempotent script:

```bash
dotnet ef migrations script --idempotent --project src/TodoWerk.Infrastructure -o migrate.sql
```

## 6. Apply it with `-I -b`, and stop the application first

```bash
docker compose stop todowerk        # or it reconnects and the drop cannot proceed

docker exec -i <sql-container> bash -lc \
  '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b \
   -Q "CREATE DATABASE TodoWerk"'

docker exec -i <sql-container> bash -lc \
  '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -I -b \
   -d TodoWerk -i /tmp/migrate.sql'
```

What the two flags do:

- `-I` sets `QUOTED_IDENTIFIER ON`. `sqlcmd` defaults it off, and SQL Server then refuses index
  DDL with a message about SET options that names no index you recognise.
- `-b` aborts on the first error. Without it, `sqlcmd` continues, and a migration can be recorded
  in `__EFMigrationsHistory` while its objects were never created. Re-running the idempotent script
  then *skips* that migration and leaves a schema that is silently incomplete. That is worse than a
  crash, because it looks like it worked.

If anything goes wrong, drop the database and start again; do not repair it. It holds no data yet,
and a half-applied schema is not worth diagnosing.

## 7. Say who you are in the terms of use

TodoWerk serves its own terms of use and privacy notice, at `/legal/terms` and `/legal/privacy`.
Both answer anonymously: people read them to decide whether to sign in at all, and Microsoft
reviewers read them without ever signing in. Both are the documents in the repository, not copies:
`TERMS.md` and `PRIVACY.md` are compiled into the application and rendered.

The terms are the same for every deployment; who stands behind them is not. You are the Operator of
your installation, not CloudWerk. Three settings name you:

```ini
Legal__Operator=Contoso Ltd
Legal__OperatorContact=legal@contoso.example
Legal__GoverningLaw=England and Wales
```

`GoverningLaw` completes the sentence "governed by the law of —", so name the place rather than the
law: `Germany`, not `German law`.

The same operator name and contact appear on two more pages that the deployment serves, and the App
Package names both: `/about`, which the Teams admin centre shows as the app's support link, and
`/administrators`, the page for an administrator deciding whether to allow TodoWerk
([ADR-0013](../adr/0013-the-handbook-is-split-by-kinship.md)). The About Page can also link to pages
of your own. It shows a link only if you set its address; leave a setting unset and that link is
absent:

```ini
Handbook__ImprintUrl=https://www.contoso.example/imprint
Handbook__TermsOfBusinessUrl=https://www.contoso.example/terms-of-business
Handbook__OrderUrl=
Handbook__LandingPageUrl=
Handbook__GuideUrl=
```

`GuideUrl` is where a Guide for the people using TodoWerk is served, if you write one. The help menu
in the header shows a Guide entry when it is set and none when it is not. CloudWerk's Guide is
written for the Hosted Service and is not part of what a Self-Host receives.

If you leave the three `Legal` settings unset, the pages still render, because every value has a
fallback that completes its sentence. The terms then name nobody, and the application writes one
warning at startup saying so. That is acceptable while you try TodoWerk out. It stops being
acceptable once an Entra ID app registration or a Teams App Package points at these addresses,
because strangers then read them. These documents are a starting point drafted for the software,
not legal advice. Have a qualified person read them before you publish anything that links to them.

## 7a. Leave the `Licensing` section alone

There is nothing to configure here. **A Self-Host has no Licence, asks nobody, and is never
refused.** With the `Licensing` section absent (the shipped `appsettings.json` leaves every value
empty), the application registers no client for CloudWerk's ManagementPortal, makes no outbound
call to any licensing endpoint, shows no Licence Banner and no Licence panel, and reports no seat
usage to anybody. No licensing client exists, so there is no call that could fail or go out
anonymously.

If you fill in only some of it, the application **refuses to start** and names the section at
fault. This is deliberate. A half-filled section is what a Hosted Service deployment looks like when
a secret failed to arrive. A host that started anyway would serve normally until the fail-open
window ran out, and then deny every one of its users at once. The section must be complete or
absent.

If you fill in all of it, you have configured a Hosted Service. TodoWerk then sends your people's
tenant id and Entra object id to the host you named, on every resolution, and denies anybody that
host does not recognise. The three settings that decide this are `Licensing__PortalHost`,
`Licensing__ApplicationKey` and `Licensing__SolutionSlug`. A Self-Host has no reason to set them,
and one reason not to: your people's identifiers would leave your infrastructure.

## 8. Point the authority at the audience you actually serve

`EntraId:TenantId` becomes the authority that every sign-in is sent to. It has exactly two correct
values:

- **`organizations`** — a deployment that serves more than its own tenant. This is the default in
  `appsettings.json`.
- **a tenant id** — a Self-Host that serves one tenant and wants Entra ID to refuse everybody else.

`common` is rejected at startup: it admits consumer Microsoft accounts, and TodoWerk is B2B only.

A deployment overrides the default with an environment variable (`EntraId__TenantId`). While you
set one up, the easiest value to reach for is the tenant id from your own development registration.
Nothing reports the mistake. The application starts, is healthy, serves every page, and signs in
every colleague you ask to test it, because all of you are in that tenant.

Somebody from another tenant is then told this, in their browser and nowhere else:

> Selected user account does not exist in tenant `<your tenant>` and cannot access the
> application `<your client id>` in that tenant. The account needs to be added as an external user in the tenant
> first.

That is `AADSTS50020`. Two facts about it save time. First, it is not logged anywhere on your side:
Entra ID refuses the request before your application is involved, so the container logs never show
it. Second, setting the app registration's **Supported account types** to multiple tenants does not
fix it, because Entra ID does not consult the registration here: a tenant-specific authority
rejects a foreign account whatever the registration says.

Verify from outside, without signing in and without a second tenant to test with:

```bash
curl -sD - -o /dev/null https://<host>/auth/sign-in | grep -i location
```

The authority in that `Location` header shows the setting:
`https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize?...` for a multi-tenant
deployment, and a bare tenant id in place of `organizations` where you intended one.

## 9. Deploy a tag that means something, and ask what is running

CI gives every image it publishes from `main` two tags: `latest` and `sha-<commit>`. Deploy the
`sha-` tag.

Two deployments of `latest` are not the same deployment, and a redeploy of it silently means
"whatever `main` is now". When something goes wrong, you then cannot tell whether you are looking at
a bug or at a build you did not know you had shipped.

```yaml
image: ghcr.io/cloudwerk/todowerk:sha-<commit>
```

CloudWerk's registry does not keep every `sha-` tag. A daily job deletes an image version once it is
older than 7 days and outside the 10 newest, so a pinned tag can stop pulling after a redeploy or a
host rebuild. A running host keeps its local copy. Before the tag ages out, move the pin forward or
copy the image into a registry of your own.

Whichever tag you deploy, the running application reports what it is:

```bash
curl -s https://<host>/version
```

```json
{"version":"1.0.0","commit":"<sha>","licensing":"absent"}
```

`licensing` is the other fact a redeploy can change without changing the commit: `absent` is a
Self-Host, which asks nobody and licenses everybody; `portal` means the `Licensing__*` variables
are set and every signed-in person is resolved against ManagementPortal. It names only the shape,
never the host, the slug or the key.

An image built before `/version` existed does not answer it with a 404. The SPA fallback serves
`index.html` for anything unmatched, so an old build answers `/version` with a web page and a `200`.
Read the body.

`/version` answers anonymously, like the legal pages and for the same reason: everybody who needs
it arrives without a session. That includes you after a deploy, a smoke test, and somebody filing a
bug against a deployment they do not administer. The commit is read from the assembly the compiler
stamped, so it cannot disagree with what is running, and the image built from it is tagged `sha-`
followed by exactly that value. A `commit` of `null` means the image was built outside a git
working tree and is not traceable to anything; a `version` of `unknown` means the build stamped no
version at all.

Check it after every deploy.

## 10. Check it from outside

```bash
curl -sI https://<host>/health                     # 200
curl -s  https://<host>/version                    # the commit that is running
curl -sD - -o /dev/null https://<host>/ | grep -i 'set-cookie\|x-frame-options'
curl -sD - -o /dev/null https://<host>/teams | grep -i 'content-security-policy\|x-frame-options'
curl -sD - -o /dev/null https://<host>/auth/sign-in | grep -i location
```

What each one has to say:

| Address | What must be true |
| --- | --- |
| `/health` | `200`. A 500 here means the key ring, not the database — nothing about health touches SQL. |
| `/version` | JSON naming the commit you meant to deploy. **Check the body, not the status code**: on an image built before this endpoint existed, the SPA fallback answers `/version` with `index.html` and a `200`, so a `200` alone proves nothing. A `commit` of `null` means an image built outside a git working tree, which is traceable to nothing. `licensing` is `absent` on a Self-Host; `portal` means somebody set the `Licensing__*` variables and every signed-in person is being checked against ManagementPortal. |
| `/` | Cookies carry `secure; samesite=none`; `X-Frame-Options: DENY` and `frame-ancestors 'none'`. The Workbench is not the thing being embedded. |
| `/teams` | `frame-ancestors` names the Teams hosts, and **no** `X-Frame-Options` at all — that header has no allow-list, so the tab's document strips it. |
| `/auth/sign-in` | `302` to the identity provider, with `redirect_uri` on `https://`, the authority the one section 8 chose, and the scope reading `openid profile offline_access …Tasks.ReadWrite` and nothing wider. |
| `/legal/terms` | `200` with no session, and the operator named on the page is you rather than "the organisation that runs it". |
| `/legal/privacy` | `200` with no session. |
| `/about` | `200` with no session and no `Set-Cookie`, naming you as the operator. This is what the manifest's `websiteUrl` points at, and what the Teams admin centre shows as the app's support link. |
| `/administrators` | `200` with no session. The numbers on it (the statistics floor, the undo window, the dormancy window) are this deployment's `Onboarding` and `Changes` settings, so if you changed one, the page says so. |

You can check every one of these without signing in and without a second tenant. That matters
because a wrong authority and an unexpected build are both invisible to everybody who administers
the deployment and obvious to the first person who does not.

Next, configure the app registration ([teams-app-registration.md](teams-app-registration.md)),
build and install the App Package ([teams-app-install.md](teams-app-install.md)), and walk the tab
([teams-tab-walk.md](teams-tab-walk.md)).
