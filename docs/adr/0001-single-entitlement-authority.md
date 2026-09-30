# A single licensing authority resolves every Licence

The Hosted Service asks one external licensing authority whether a person may use it, and TodoWerk
keeps no licensing logic of its own. The authority is ManagementPortal, a separate CloudWerk
product, and the question travels server-side over a defined contract, the First-Party Path: the
person's tenant id and Entra object id go out, and a Licence — its kind and its end date — or a
refusal comes back. A Licence is resolved **per person**; what kinds there are and whom they cover
is [ADR-0012](0012-three-kinds-of-licence-resolved-per-person.md).

TodoWerk holds the most recent answer for one person in one tenant and nothing else: no Licence
record, no key and no trial clock. Every rule about who is issued what, and for how long, lives in
the authority, so changing one is not a TodoWerk release.

The client is public AGPL code and carries no key and no licensing logic. It is the same for
everybody however they arrived; the channel somebody bought through is data on the Licence, never a
build flavour.

A Self-Host has no `Licensing` section. It registers with no authority, asks nobody, shows no
Licence Banner and is never refused. The section is complete or absent: a half-filled one fails
startup validation rather than becoming a deployment that cannot reach its authority.

## When the authority cannot be reached

Enforcement is server-side and exists only where a `Licensing` section does: a confirmed refusal
closes the product for that person. An unreachable authority is not a refusal. TodoWerk keeps
serving a person on their last positive answer for a fail-open window, `Licensing:FailOpenWindow`,
and past it denies as well, with a card that says the Licence could not be verified rather than
that it ended, retrying on the next request. The window is the operator's setting rather than the
authority's, because nothing a customer can do makes the authority unreachable: a short window buys
no enforcement, it only decides how much of an outage takes the product down with it.

How fresh an answer has to be is the authority's to say. TodoWerk honours the recheck interval the
authority sends, bounded above by `Licensing:MaximumCacheLifetime`, so that a purchase or a
cancellation lands in minutes whatever the authority asks for.

A person TodoWerk has never resolved, arriving while the authority is down, is denied with "could
not be verified" rather than served: there is no positive answer to fail open onto, and the card
clears itself the moment the authority answers.

## Considered options

- **Licence state inside TodoWerk** — a trial clock, a table of Licences. Rejected: it makes a
  second authority, and two authorities disagree.
- **A key built into the client or the App Package** — rejected: it publishes a key in an
  open-source repository, puts everybody who installs that package on one Licence, and makes the
  software differ by channel.
- **Asking each store whether a person has bought** — rejected: it puts marketplace code and a new
  Graph permission inside TodoWerk. Purchases through any channel become ordinary Licences at the
  authority, so TodoWerk asks one authority one question.

## Consequences

- TodoWerk contains no marketplace code, permanently.
- The contract is the boundary. What TodoWerk relies on is what the First-Party Path answers; how
  the authority reaches that answer is the authority's business.
- A deployment without a `Licensing` section behaves as a Self-Host, whoever runs it.
