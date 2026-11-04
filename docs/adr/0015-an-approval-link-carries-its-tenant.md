# An Approval Link carries its tenant

When the Teams tab's sign-in is refused for want of consent, TodoWerk gives the person an
**Approval Link**: the address of its own Tenant Consent start, carrying the tenant their Teams
sign-in named, to hand to an administrator. An approval made through it is recorded like one made
from inside the product. The link is a protected token rather than a stored invitation, it lasts a
week, and the callback's rule from [ADR-0008](0008-tenant-consent-is-delegated.md) is unchanged.

## The person who can approve is somebody else, on another account

In a tenant with user consent switched off, a person pressing **Approve and continue** in the tab
meets Microsoft's own "approval required" page, and until now TodoWerk had nothing to say after
that. The administrator who can approve is rarely that person, and the administrators who care most
about what they approve keep a separate account for administration, with no admin role on their
daily one. That account has no TodoWerk session and should never get one: signing in with it makes
the administration account a TodoWerk user, which is what a separate account exists not to be.

So the approval has to be made by somebody with no session. TodoWerk already let it: the Tenant
Consent start is anonymous, and Microsoft asks whoever opens it to sign in. What it could not do was
record the result. A flow that starts without a session knows no tenant, and the `tenant` parameter
on the callback is forgeable — Microsoft's documentation says so, and ADR-0008 built the state check
because of it. An approval made that way was reported and never recorded, and the invitation stayed
on screen for everybody in the organisation.

The ordinary sign-in prompt is worse. An administrator who signs in to approve meets Microsoft's
user consent screen, whose "Consent on behalf of your organization" checkbox is unticked by default
and is not shown at all to Application and Cloud Application Administrators. Ticking nothing
approves that one administration account and nobody else, and makes it a TodoWerk user.

## What the link holds, and where it comes from

The one moment TodoWerk holds a tenant id it can believe for somebody who cannot get a session is
the Teams exchange that just failed: the Teams single sign-on token was validated before the
on-behalf-of call was made, and its `tid` is the organisation the person is in. The consent-required
refusal carries a link minted from that and nothing else:

- The address is `/auth/tenant-consent?approval=<token>`, absolute on the deployment that minted it.
- The token is an ASP.NET Data Protection payload under a purpose of its own, holding the tenant id
  and an expiry seven days out. Only this deployment's key ring can read or write it, and no other
  payload this deployment protects — the consent state cookie included — can be presented as one.
- Nothing is stored. There is no invitation table, no migration and nothing to sweep or erase.

The start endpoint believes a valid, unexpired token and starts the flow addressed to that tenant
with its state bound to it, exactly as a session would have bound it, whether or not anybody is
signed in. When a session for another tenant is present the link wins, because the person who opens
a link made for another organisation means to approve that one. A token that cannot be believed —
expired, altered, minted elsewhere — is treated as no token: the administrator is still taken to
Microsoft and can still approve, and the flow is as unbound as it was before this decision and
records nothing. Refusing outright would turn a link a week too old into an organisation that cannot
approve from it at all.

The callback is untouched. A grant is recorded when the state matches and Microsoft names the tenant
the flow began for, and on no other answer. A flow that came by a link goes back to the front door,
which says whether the approval landed, unless whoever followed it is signed in to that same tenant;
the Tenant Overview is behind the sign-in the administrator should not be asked for.

The link is offered whatever the person's Licence. The Tenant Overview's invitation is shown only
under a Tenant Licence or a Trial, but that rule is about somebody with a session and a resolved
Licence. The person this link is for has neither yet.

The browser keeps no such link. A refused authorization comes back with no identity at all, so
there is no tenant to read, and its flow stays unbound as before.

## Considered options

- **A server-side invitation row**, keyed by an opaque id in the link — rejected. It buys revocation
  and a way to list outstanding requests, neither of which a link whose only power is to hide one
  tenant's invitation needs. What it costs is a table naming a tenant before anybody in it has
  approved anything, a migration, a sweep and an erasure path.
- **The tenant in the link in the clear**, `?tenant=<guid>` — rejected. Anybody could write one for
  any organisation and switch off its invitation, which is the forgery ADR-0008's state check exists
  to prevent.
- **Inferring a tenant grant from a later silent exchange that succeeds** — rejected. A successful
  on-behalf-of exchange proves this person has consent, and that may be their own. It is evidence
  about one person and never about the organisation, so recording a grant on it would hide the
  invitation in a tenant where only some people had consented. TodoWerk still says "not approved
  through TodoWerk" rather than "not approved".
- **Reading the service principal's grants from Graph** — rejected for the reason ADR-0007 and
  ADR-0008 give: a directory permission to answer one boolean.
- **A link on the browser's refused-sign-in card as well** — not possible on the terms above. There
  is no validated identity there to take a tenant from, and a link minted from anything less would be
  the clear-text tenant above.

## Consequences

- Anybody holding a link can start a flow bound to its tenant and answer the callback themselves,
  recording a grant nobody made. This is the forgery ADR-0008 already accepts for a signed-in member
  of the tenant, extended to whoever that member hands the link to, and it is bounded the same way: a
  forged answer hides the invitation for that one tenant and nothing else, and the Tenant Overview
  keeps a route to approve again. Nobody can forge one for a tenant they were not given a link for.
- A link cannot be withdrawn, only outlived. A week is long enough for a request to reach an
  administrator who is away and short enough that a forwarded link stops meaning anything before it
  is forgotten about. Rotating the Data Protection keys does not invalidate a link, because retired
  keys stay in the ring for reading; losing the key ring does, along with every session.
- The consent card in the tab always shows the second path, quieter than the first, including to
  people who could have approved for themselves. Which of the two a person is cannot be told before
  Microsoft has answered, and a card that guessed would hide the link from the person who needed it.
- The browser's refused-sign-in card is unchanged: its approval is reported and not recorded.
- TodoWerk still cannot see consent granted outside itself, and does not try to infer it.
- `CONTEXT.md` gains **Approval Link**, and the Administrator's Guide describes the route for
  tenants with user consent switched off and warns against approving through the ordinary sign-in
  prompt.
