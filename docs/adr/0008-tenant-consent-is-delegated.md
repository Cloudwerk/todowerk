# Tenant Consent is delegated, and TodoWerk takes no application permission

Tenant Consent is an administrator approving the **delegated** grant TodoWerk already requests —
`Tasks.ReadWrite`, plus the OpenID Connect scopes — on behalf of everybody in the tenant. It changes
who is asked at sign-in and nothing else. TodoWerk takes no application permission, no directory
permission, and holds no app-only credential. It still acts as each person with that person's own
token, and still reaches no mailbox whose owner has not signed in.

This reverses the Org Mode described in ADR-0007, SECURITY.md and the M3 roadmap row through M2,
which was app-only consent carrying `Tasks.Read.All` / `Tasks.ReadWrite.All`, an admin-curated
Inclusion List of whose tasks were indexed, and central management of other people's Hashtags. None
of that will be built. It continues ADR-0007's one-grant rule rather than departing from it: one
prompt, covering everything TodoWerk does, asked of one administrator instead of of everybody.

## Why the app-only version does not survive contact with Exchange

The Inclusion List was the whole safety story of app-only Org Mode. An application permission of the
form `Tasks.ReadWrite.All` reads every mailbox in the tenant, and the answer to "so scope it to the
people who opted in" was going to be Exchange's own mechanisms for exactly that — Application Access
Policies, and their successor, Exchange App RBAC.

Neither covers Tasks. Both scope **Mail**, **Calendars**, **Contacts** and **Mailbox settings**;
`Tasks.*` is not among the resource scopes they can restrict. So an app-only grant could never have
been narrowed to a subset of mailboxes at all. The Inclusion List was not merely unbuilt — it was
unenforceable in principle. TodoWerk would have held a credential able to read every task title in
the tenant, with a list inside TodoWerk's own database as the only thing standing between that
credential and the mailboxes of people who never asked for it. A filter in the application that
holds the keys is not a boundary; it is a promise.

Delegated consent has the property the Inclusion List was invented to fake, for free and
unforgeably: TodoWerk can reach exactly the people who signed in, because reaching somebody requires
a token issued to them. Somebody who never signs in is not "excluded by configuration" — they are
unreachable.

## What is given up

Tenant-wide management of other people's Hashtags, which was the headline of the old Org Mode. An
administrator cannot see the organisation's tags, cannot rename across colleagues' tasks, and cannot
clean up on somebody's behalf. That is a real feature and it is the one being traded away.

It is worth less than it looks. The value of a tenant-wide rename is highest exactly where it is
least defensible — rewriting task titles in mailboxes whose owners did not ask — and the product
this ADR leaves is one where nothing reaches a person's tasks without that person having signed in
and consented. The reporting half of the ambition survives in a form that discloses nothing: the
Tenant Overview reports counts.

## Considered options

- **App-only consent with an Inclusion List** — rejected above. Exchange App RBAC and Application
  Access Policies do not scope Tasks, so the list could not be enforced anywhere but inside
  TodoWerk, and the credential it was meant to bound reads every mailbox in the tenant.
- **App-only consent with no Inclusion List** — rejected as the same trade without the fig leaf: a
  tenant-wide read of every task title, taken once by one administrator, for a hashtag manager.
- **Delegated Tenant Consent, no tenant-wide reporting at all** — rejected as insufficient. The
  problem that starts this milestone is an employee in a tenant where user consent is disabled by
  policy, and an administrator with no way to tell whether approving was worth it. Consent alone
  solves the first; the second wants the numbers.
- **Reading the service principal's grants from Graph to know whether consent was given** —
  rejected. It is the accurate answer and it costs a directory permission (`Application.Read.All` or
  similar) to resolve one boolean, which is the trade ADR-0007 declined in the other direction.
  TodoWerk records grants made through its own redirect instead, and every surface says "through
  TodoWerk" rather than claiming to know what an administrator did in the Entra portal.
- **Cross-person Hashtag aggregation without any write** — deferred, not rejected. A tenant-wide
  distinct-Hashtag count requires deciding whether two people's `#work` is one Hashtag, which is a
  domain decision about identity across people rather than a reporting change. It also discloses a
  person's private task vocabulary to their colleagues, which the suppression floor cannot fix
  because the disclosure is not statistical. Nothing in this milestone counts Hashtags across people.

## Consequences

- ADR-0007 stands unchanged in substance and is corrected where it described the app-only version.
  SECURITY.md loses its `Tasks.ReadWrite.All` sentence; the M3 roadmap row loses all three of its
  terms.
- `CONTEXT.md` drops Org Mode, Personal Mode and the Inclusion List, and gains **Tenant Consent**,
  **Tenant Member** and **Tenant Overview**. There is no "mode" any more: there is one product, and
  a tenant may or may not have approved it centrally.
- The admin-consent redirect URI joins the app registration's list, alongside sign-in and sign-out.
  A deployment that forgets it gets a redirect-URI mismatch from Microsoft at the moment an
  administrator tries to approve.
- The consent callback's `tenant` parameter is not believed on its own. Microsoft's own documentation
  warns it can be forged to impersonate a response, so TodoWerk starts the flow with a state it
  remembers in a protected short-lived cookie and records nothing unless the state matches and names
  the same tenant. Without that check, anybody could switch off another organisation's invitation to
  approve.
- Within one tenant, that check is all there is. The admin-consent flow returns no signed response —
  nothing in the redirect proves it came from Microsoft — so a signed-in member of the tenant can
  start the flow and then reach the callback themselves, and TodoWerk will record a grant nobody
  made. That cannot be closed without asking Graph whether the grant exists, which is the directory
  permission this ADR declines. So the consequence is bounded instead: a recorded grant hides the
  invitation and nothing else, and the screen keeps a route to approve again, worded as TodoWerk's own
  note rather than as proof. A wrong note then costs a sentence on a screen rather than an
  organisation's only way in.
- TodoWerk cannot detect consent granted outside itself, and cannot detect its revocation. The first
  is worded around; the second is handled by the dormancy sweep in
  [ADR-0009](0009-what-todowerk-stores-about-a-person.md) — nobody can sign in after a revocation, so
  everybody goes dormant and the tenant clears itself.
- There is no TodoWerk-side notion of an administrator. No role check, no app role, no permission-
  denied path. The person who clicks the consent link is whoever Entra ID will let approve, and the
  Tenant Overview is visible to every signed-in user of the tenant.
