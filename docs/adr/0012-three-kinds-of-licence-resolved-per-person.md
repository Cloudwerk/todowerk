# Three kinds of Licence, resolved per person

The Hosted Service is used under a Licence, and a Licence comes in three kinds. All three are
records held by the licensing authority of [ADR-0001](0001-single-entitlement-authority.md), which
TodoWerk asks about and never holds.

- A **Tenant Licence** covers everybody in a tenant, with no cap on how many.
- A **Personal Licence** covers one person, keyed by their tenant and their Entra object id.
- A **Trial** is issued to a person automatically, once and never again, the first time they arrive
  with no Licence of any kind. It runs for a time-limited term set by the licensing service.

A person is licensed by their tenant's Tenant Licence if there is one, otherwise by their own
Personal Licence or Trial, otherwise not at all. The Hosted Service resolves this **per person** —
tenant id and object id together — and a person with none is refused while their colleagues carry on. The object id is what crosses to the authority to make that possible; TodoWerk stores
nothing new to do it ([ADR-0009](0009-what-todowerk-stores-about-a-person.md) is untouched).

## Considered options

- **One Licence per tenant, metered by seats** — rejected: the product is bought by individuals as
  often as by organisations, and a per-tenant record leaves an individual's purchase with nobody to
  bind it to.
- **A Trial per tenant** — rejected: a colleague invited late would get what was left of somebody
  else's, and what is on offer afterwards is a Personal Licence, which is per person.
- **Deny the tenant when one person is unlicensed** — rejected as the obvious mistake the per-person
  model exists to avoid.
- **A Trial length in TodoWerk's configuration** — rejected: ADR-0001 allows one authority, and a
  length here would be a second.

## Consequences

- The First-Party Path resolves by tenant id **and** object id, and its answer carries the Licence
  kind and end date as typed fields.
- The Trial belongs to the authority: TodoWerk carries no trial clock, no trial length and no trial
  flag. A person's first arrival is a resolution like any other; the authority's answer is a Trial.
- Denial is per person and so is the cache: the fail-open window of ADR-0001 is counted per person,
  and the worker check inside each scan and Change claim is per person too. Erasure, the legal pages
  and the version endpoint survive denial; a denied person's data is left alone until
  [ADR-0009](0009-what-todowerk-stores-about-a-person.md)'s dormancy takes it.
- Tenant Consent is offered under a Tenant Licence and during a Trial, because whether to buy for
  the organisation is part of what a Trial is trying, and never under a Personal Licence: paying for
  one seat is not standing to approve TodoWerk for an organisation. Hiding the invitation hides
  TodoWerk's route only — an administrator can still grant consent in the Entra portal, and TodoWerk
  still cannot see that they did ([ADR-0008](0008-tenant-consent-is-delegated.md)).
- The Licence Banner talks to one person about their own Trial. Inside the Teams Tab it carries no
  link and the denied card names no contact, on every device, because the Teams Store forbids a tab
  on a phone from alluding to a paid upgrade and the tab does not know which device it is on. The
  browser carries the links. That is the tab-versus-browser distinction the client already has, and
  deliberately not a device check.
- The Purchase Link is a field on the authority's answer, delivered with a refusal as well, so the
  card for an ended Licence can carry it in the browser. The card for a Licence that could not be
  verified never does: it is met by people who have paid, during an outage, and nothing answered.
- Seat usage is reported per person on interactive sign-in, as a figure and never as a gate: a
  Tenant Licence is unlimited and a Personal Licence is one person.
- A Self-Host has none of this. It has no Licence, asks nobody, and is never refused.
