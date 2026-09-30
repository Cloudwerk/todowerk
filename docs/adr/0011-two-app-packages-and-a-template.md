# App Packages are built per deployment, and Self-Host gets a template

Every deployment of TodoWerk offers the same Teams tab, and they cannot share one App Package.
CloudWerk's App Package names the Hosted Service and its app registration. A Self-Host does not get
it: it gets a *template*, whose id, host and Application ID URI a build script fills in from the
customer's own deployment.

An app package's identity is baked into every tenant that installs it. Getting this wrong is not a
deploy away from being fixed — it is a tenant-by-tenant uninstall.

## Why Self-Host cannot use CloudWerk's package

Teams single sign-on does not support multiple domains per app. The manifest's `webApplicationInfo`
names one Application ID URI, that URI names one host, and the token the Teams client acquires is
acquired for that host. A customer running TodoWerk on their own infrastructure is a different host
and a different app registration, so they need a different manifest — not as a matter of preference
but because the one CloudWerk publishes cannot authenticate against their deployment at all.

So the repository ships the template and the build script. A built package carries one
deployment's values, and built packages are not committed.

## Considered options

- **One package for every deployment** — rejected: impossible, because a Self-Host cannot
  authenticate against CloudWerk's host.
- **Shipping only built packages, no template** — rejected: Self-Host is a first-class deployment of
  this product, and a Self-Host with no way to build a package has no Teams tab.

## Consequences

- Packages must not drift from one another. Everything but id, host and Application ID URI is
  generated from one source by the build script.
- The install a tenant is *told* to perform is manual: Teams admin center, or the Store. The
  PowerShell publish script is a convenience over the same Graph call, and is documented as such.
  Publishing to an org catalog is a delegated-only permission, so no version of it runs unattended.
- The validation tool is run against a package before it goes anywhere.
- A Self-Hoster needs an Entra app registration with an Application ID URI, an `access_as_user` scope
  and the Teams clients pre-authorized before their package means anything. That script is Self-Host
  packaging and belongs with the rest of it.
