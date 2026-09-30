# One consent grant, containing every permission TodoWerk needs

Sign-in requests `Tasks.ReadWrite` and nothing narrower. It subsumes `Tasks.Read`, so both are
never requested together. There is one consent prompt, once, covering everything the product
does — no per-feature escalation, no second prompt at the moment somebody first tries to change
something.

M3 keeps the rule and changes who is asked. Tenant Consent is one administrator approving this same
delegated grant for everybody, so nobody else is prompted at sign-in
([ADR-0008](0008-tenant-consent-is-delegated.md)). It is not app-only consent and it carries no
`Tasks.ReadWrite.All`: the app-only Org Mode this paragraph described through M2 will not be built,
because Exchange's mechanisms for scoping an application permission to a subset of mailboxes do not
cover Tasks at all.

This reverses the plan recorded on `GraphScopes` through M0 and M1, which was to add the write
scope in M2 "as a separate incremental-consent step rather than a wider grant up front".

## Why the incremental version does not survive contact with the design

The write scope has to be in the durable token cache *before* a Change is queued, because the
thing that performs the write is a background worker with no browser attached (ADR-0002). A
worker cannot prompt anybody. So incremental consent does not mean "ask when we need it" — it
means "ask at the last interactive moment before we need it", which is the confirmation click on
a destructive operation. That is the worst available moment to interrupt somebody with a consent
dialog they have to read.

Against that, the honest description of what the incremental version buys is: a user who only
ever reads their inventory does not grant write access. That is a real benefit and it is the one
being traded away here. It is worth less than one clear decision at sign-in, in a product whose
entire purpose is to change hashtags — a read-only TodoWerk user is a user who has not started
yet.

The admin-facing argument runs the same way. An organisation evaluating the app sees one
permission list and makes one decision, rather than discovering a second permission request
later and having to work out whether something changed.

## Considered options

- **Incremental consent at first write** — rejected above: it puts a consent prompt inside a
  destructive confirmation, and the token must be cached before the queue, not after.
- **Request `Tasks.Read` and `Tasks.ReadWrite` together** — rejected as noise. `Tasks.ReadWrite`
  subsumes the read; listing both makes the grant look wider than it is.
- **Keep read-only sign-in and offer an explicit "enable changes" step** — rejected: it is the
  incremental option with better manners and the same two prompts, and it leaves a user in a
  state where the product's headline feature is visible and refuses to work.

## Consequences

- Every TodoWerk user grants write access to their tasks, including one who only looks.
  [SECURITY.md](../../SECURITY.md) and the app-registration steps in CONTRIBUTING say so plainly.
- Token cache entries issued under the narrower scope cannot silently acquire the wider one:
  MSAL raises `MsalUiRequiredException`, which the gateway already maps to reconnect-required, so
  those users are asked to sign in once and their background syncs stop until they do. Only an
  account that signed in before the write scope joined the grant is affected.
- The reconnect path is now load-bearing for an ordinary upgrade rather than only for a 90-day
  expiry, which is part of why the Workbench must stop deciding "offer sign-in" by matching a
  sentence in the failure text.
- `GraphScopes.SignIn` is a one-element list and the comment promising incremental consent is
  deleted rather than implemented.
