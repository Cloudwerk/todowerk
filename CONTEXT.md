# TodoWerk

Open-source tooling by CloudWerk GmbH that adds missing capabilities to Microsoft To Do. v1 ships exactly one module: the Hashtag Manager. Work or school accounts only — consumer Microsoft accounts are never supported.

## Language

**Hashtag**:
A `#word` token TodoWerk recognises in a Microsoft To Do task title. It has no entity, no id, and no API representation in To Do — it exists only as text. Identity ignores casing, so `#Work` and `#work` are one Hashtag with two Spellings, never two Hashtags.
_Avoid_: tag (ambiguous with Outlook categories), label

**Spelling**:
The literal text of a Hashtag as it appears in one title. A Hashtag has as many Spellings as its authors used casings.
_Avoid_: variant, alias, name

**Canonical Spelling**:
The one Spelling of a Hashtag that TodoWerk displays and normalises the others toward. Chosen from usage, always overridable.
_Avoid_: preferred name, primary tag, normalised form (that is the internal key, not a Spelling)

**Occurrence**:
One appearance of one Spelling in one task. The unit the index counts and the unit a rename rewrites.
_Avoid_: usage, instance, hit

**Stale**:
A Hashtag none of whose tasks has been modified within the configured window (`Indexing:StaleAfter`, six months by default). Its tasks still exist and still carry it: the flag says nothing has happened to them lately, not that there are none. Measured from Graph's `lastModifiedDateTime`, so completing a task, or a Change rewriting it, restarts the clock.
_Avoid_: unused (its tasks are still there), dormant (a person the sweep will erase — ADR-0009), out of date (that is the index — ADR-0003)

**Rename**:
Giving one Hashtag a different name, so its Occurrences come to spell a Hashtag that did not exist before: `#Prio1` becomes `#Priority1`. Renaming onto a name already in use is a Merge, not a Rename.
_Avoid_: edit, relabel

**Normalise Casing**:
Rewriting a Hashtag's Occurrences so every one uses its Canonical Spelling. Acts within a single Hashtag, and is not a Merge. Two Hashtags that merely look alike are not a casing question: `#Work` and `#Works` fold to different keys, so combining them is a Merge however similar they read.
_Avoid_: merge casing variants, deduplicate

**Merge**:
Folding one Hashtag into a different Hashtag, so the Occurrences of the first come to spell the second. Always spans distinct Hashtags, and may fold several at once.
_Avoid_: combine, normalise (that is Normalise Casing)

**Change**:
One instruction to rewrite task titles, in one of two shapes: a set of source Hashtags and the one Spelling they are all to take, or a set of Markers and the tasks they are to be written into or taken out of. The unit that is previewed, confirmed, queued, run and undone as a whole, whichever shape it has. For the first three, the operation it performs is read off its shape: one source spelled differently is Normalise Casing, one source with a new name is a Rename, and several sources are a Merge. The two about Markers state their operation, because they carry no target Spelling for anything to be read from. Only the first three touch a Hashtag; Apply Markers and Remove Markers write outside one.
_Avoid_: job (that is the machinery that runs a Change), operation, edit

**Apply Markers**:
The Change that brings titles into line with a user's Marker Rules. For each task in its scope, the block at the front is rewritten to carry every Marker whose Hashtag the task has, in rule order, and any Marker already there whose Hashtag is gone is kept after them. Markers are added and reordered, never removed, with one exception: a Marker that a rule has since retired for a new one is swapped for the new one, which is the rule's own Marker changing, not a Hashtag going away. Scoped to all rules or to one, and writing the whole block either way, so a one-rule Apply never leaves a block in the wrong order. The Markers it applies are fixed when it is confirmed; a rule edited while it runs waits for the next one.
_Avoid_: sync, enforce, auto-apply, marker run

**Remove Markers**:
The Change that takes stale Markers off the front of tasks: for each task in its scope, any Marker in the block that no standing rule asks for on that task is taken out, and everything else is left exactly where it is. The mirror of Apply Markers, sharing its plan, journal, queue, cancel and undo, and the only operation in the product that removes text. Scoped by Marker, not by Hashtag: a Marker is stale precisely because its Hashtag has gone, and one that a deleted rule left behind never had one. So it covers one emoji or every stale one. It never adds a Marker, never reorders what is left, and never swaps a retired Marker; an emoji nobody made a rule about was never in the block and so is never touched.
_Avoid_: strip, clean up, reset

**Stale Marker**:
A Marker in a task's block that no standing Marker Rule asks for on that task: its Hashtag has left the task, or the rule that put it there was deleted. Not an emoji TodoWerk never wrote: an emoji outside every rule is not in the block at all, and is ordinary text. What Remove Markers takes away, and what the count beside each rule and each left-behind Marker is counting.

**Change Journal**:
What a Change actually did, one entry per task it wrote: the title found immediately before the write and the title written. What undo reads back, and the only record of a task's previous title.
_Avoid_: history, audit log (it exists to be reversed, not to be inspected)

**Marker**:
An emoji a task title carries at its very start because a Marker Rule put it there. Exactly one emoji, however many code points it takes: a flag, a skin-toned hand and a family are each one Marker. Several Markers on one title form a single block at the front, in the order of the user's Marker Rules, followed by one space and then the rest of the title. A Marker is only a Marker in that block; the same emoji elsewhere in a title is text.
_Avoid_: badge (a Fluent UI component), icon (every UI glyph), prefix (where it sits, not what it is), emoji (the glyph before it became a Marker)

**Marker Rule**:
One user's standing instruction that a Hashtag carries a Marker: `#bread` carries 🍞. One rule per Hashtag, matched by Hashtag identity so every Spelling qualifies. A user's rules form an ordered list, and that order is the order of the block. A rule declares; it never writes on its own. Titles are brought into line only when the user applies their rules, previewed and undoable like any Change, and applying only adds — a Marker whose Hashtag has since gone is left where it is. A Rename carries the rule with it to the new name. A Marker whose Hashtag has since gone is taken off only by an explicit Remove Markers.
_Avoid_: emoji rule, tag icon, decoration, auto-prefix (nothing is automatic), setting

**Hashtag Manager**:
The v1 TodoWerk module: inventory, rename/merge, Marker Rules, and usage reporting of Hashtags across one user's To Do tasks. Never across several users': no surface in TodoWerk compares one person's Hashtags with another's.
_Avoid_: tag manager

**Workbench**:
The Hashtag Manager's main screen: the hashtag inventory table as the product, with a detail/preview panel and the change queue alongside. Selected over a guided-cleanup or report-first layout.
_Avoid_: dashboard (implies report-first), main view

**Teams Tab**:
The Microsoft Teams surface in which the Workbench is shown. A host, not a second screen: what it contains is the Workbench, minus the controls that cannot mean in Teams what they mean in a browser. Personal only: TodoWerk never places one in a team, because it has nothing to show a team.
_Avoid_: Teams app (that is the App Package), Teams channel tab, Teams integration

**Tenant Consent**:
An administrator approving TodoWerk's delegated permission grant on behalf of everyone in the tenant, so nobody is asked to consent at sign-in. It changes who is prompted and nothing else: TodoWerk still acts as each user with that user's own token, and still reaches no mailbox whose owner has not signed in.
_Avoid_: admin consent (ambiguous with consent to app-only permissions, which TodoWerk does not use), Org Mode, tenant mode

**Tenant Member**:
The record that a person has used TodoWerk: their tenant, their Entra object id, when they first signed in and when they last did. Written only when a human signs in, because a scan running on a timer is not somebody using the product. Holds no name, no address, no history: a first and a last, and nothing between them.
_Avoid_: user (TodoWerk keeps no user profile), sign-in log, activity record (it records no activity, only two moments)

**Idle**:
A person with no interactive sign-in inside `Indexing:IdleAfter` (fourteen days by default), read off their Tenant Member record. Their index is not kept fresh on a timer and, under the Hosted Service, their Licence is not re-checked; both resume on their next sign-in, which the next poll notices. Only the scheduled sync is withheld: a scan they request, and the follow-up scan a Change queues, run as for anybody. An anonymised Tenant Member, and an index with no Tenant Member at all, are Idle by construction.
_Avoid_: dormant (a person the sweep will erase — ADR-0009), stale (a Hashtag, or the index — ADR-0003), inactive, away

**Tenant Overview**:
What a tenant is shown about its own use of TodoWerk: how many of its people have signed in, how recently, and how many Occurrences they hold between them. Counts only — it never names a person, never shows a per-person row, and never compares one person's Hashtags with another's. Visible to any signed-in user of the tenant, and suppressed entirely while too few have signed in for a total to be uninformative about individuals. It also names the reader's own Licence: the kind and the end date, and never a seat count, which is the one figure the floor above would have to protect twice. The invitation to grant Tenant Consent is a separate panel with two conditions: it shows until a grant is recorded, and only where the Licence permits it (under a Tenant Licence or a Trial, never under a Personal Licence, unchanged on a Self-Host). It is silent about grants made outside TodoWerk, which it cannot see.
_Avoid_: admin dashboard (it is not restricted to admins), org inventory (it holds no Hashtags), Org Mode

**CloudWerk**:
The company. Spelled `CloudWerk` — capital C, capital W — wherever a reader meets it: the Teams manifests' `developer.name`, CLA.md, TERMS.md, PRIVACY.md, README.md and the assembly copyright. The legal entity is `CloudWerk GmbH`, HRB 11601 at Amtsgericht Kempten (Allgäu). Lowercase `cloudwerk` stays correct in code, identifiers, domains and the `Cloudwerk` GitHub organisation slug, none of which a reader takes for the company's name.
_Avoid_: Cloudwerk (lowercase w — matches neither the commercial register nor the company's own imprint), CLOUDWERK, Cloud Werk

**Hosted Service**:
The CloudWerk-operated multi-tenant SaaS deployment of TodoWerk, and the only deployment in which a Licence applies.
_Avoid_: cloud version, managed version

**Self-Host**:
Running the same open-source TodoWerk stack (client and backend) on the customer's own infrastructure, free of charge.
_Avoid_: on-premises edition, community edition (implies a feature-reduced tier, which does not exist)

**App Package**:
The file a Teams administrator installs to give their people the Teams Tab. It names one deployment and one app registration, so the one CloudWerk publishes fits only the Hosted Service, and a Self-Host builds its own from a template.
_Avoid_: manifest (that is one file inside it), Teams app, zip

**Handbook**:
The documentation TodoWerk serves about itself to its two readers who are not contributors: the Guide, for the person using it, and the Administrator's Guide, for the person deciding whether to allow it. Served on TodoWerk's own host, beside the terms of use and the privacy notice but not among them: those two answer to the Store's commerce-free rule and share nothing with the Handbook but a host. Its two halves live apart by kinship. The Administrator's Guide is the privacy notice's sibling (read at a consent decision, exact, pictureless) and is served by every deployment, Self-Host included. The Guide is a product page's sibling (pictures, prose, its own cadence) and is the Hosted Service's alone.
_Avoid_: docs (that is contributor material — the ADRs, runbooks and status page), documentation site, help centre, wiki, manual

**Guide**:
The Handbook's half for the person using TodoWerk: what the Workbench shows, what the three issue flags mean, what each of the five Changes does, what a Marker Rule is and is not, what preview and undo guarantee, and what TodoWerk refuses to do.
_Avoid_: user documentation, user guide (the Guide has no other kind of reader), help, tutorial

**Administrator's Guide**:
The Handbook's half for the person deciding whether TodoWerk may be allowed in a tenant: what Tenant Consent grants and what it does not, what is stored about a person, what the Tenant Overview shows, how erasure and retention work, and how to revoke. Told in its own words for its own reader: the same facts the ADRs decide, never the ADRs quoted. It is the page the App Package's `publisherDocsUrl` names.
_Avoid_: admin docs, administrator documentation, IT guide, tenant guide, ADR-0008 (that is the decision, written for contributors)

**About Page**:
The one page on TodoWerk's own host that tells somebody who has not signed in, and may never, what TodoWerk is, who makes it and where help is. The page the App Package's `developer.websiteUrl` names, which the Teams admin centre shows as the app's support link, so it never asks anybody to sign in. It names the operator the deployment is configured with, and points at that operator's imprint, terms of business, way to order and Landing Page only where the deployment names one. It carries none of them itself, and a Self-Host that names nothing still serves a page that is true.
_Avoid_: landing page (that is on CloudWerk's website), front door (that is `/`, the application), home page (that is CloudWerk's), marketing page, support page

**Landing Page**:
TodoWerk's product page on CloudWerk's own website — what TodoWerk is and how to get it, in CloudWerk's house style beside its other products. Not yet built. Not the About Page, which is on TodoWerk's host and exists because a manifest has to name something.
_Avoid_: about page, product site, microsite, website (ambiguous with the manifest's `websiteUrl`, which names the About Page)

**Licence**:
The right to use the Hosted Service, as ManagementPortal records it and as TodoWerk last heard it. It comes in three kinds — a Tenant Licence, a Personal Licence, or a Trial — and TodoWerk holds no copy of any of them, only ManagementPortal's most recent answer for one person in one tenant. A person is licensed by their tenant's Tenant Licence if there is one, otherwise by their own Personal Licence or Trial, otherwise not at all; and "not at all" closes the product for that person alone, never for their colleagues. A Self-Host has no Licence, asks nobody, and is never refused. Spelled the British way; the verb is "license". The AGPL is not a Licence in this sense and is always written "the source licence" or "the AGPL" so the two cannot be confused.
_Avoid_: entitlement (nobody buys an "entitlement" to software), subscription (that is what the customer pays for, not what TodoWerk resolves), license key (TodoWerk never holds one), plan, edition, free version (the software is identical everywhere; only a Licence is ever free), the AGPL unqualified

**Tenant Licence**:
A Licence bought for a whole tenant: everybody in it is licensed, with no cap on how many. The only kind under which Tenant Consent is offered, because approving TodoWerk for an organisation is what buying for an organisation means.
_Avoid_: organisation plan, tenant-wide edition, site licence, seats (there are none to count)

**Personal Licence**:
A Licence bought by one person for themselves, keyed by their tenant and their Entra object id. It licenses that person and nobody else, and never offers Tenant Consent, because a person cannot approve TodoWerk on an organisation's behalf by paying for one seat. A tenant may hold as many Personal Licences as it has people who bought one.
_Avoid_: user licence, seat, individual plan

**Trial**:
The Licence a person is issued automatically, once and never again, the first time they reach the Hosted Service with no Licence of any kind. Its length is set by ManagementPortal, and TodoWerk keeps no clock for it. Per person, not per tenant, so a colleague invited late gets a Trial of their own. During a Trial the Tenant Consent invitation is shown, because whether to buy for the organisation is part of what is being tried.
_Avoid_: evaluation (ManagementPortal's own name for the same record, kept out of TodoWerk's language), free trial, test period

**Licence Banner**:
What the Workbench says to one person about their own Licence when there is something to say, and nothing when there is not. It speaks in two cases (a Trial is running, a Trial ends within a week) and never counts days aloud. Under a Tenant Licence or a Personal Licence in term there is no banner, an unreachable ManagementPortal shows none to users, and a Self-Host never shows one. In the browser it carries the way to buy; inside the Teams Tab it carries the sentence and no link, on every device. The line is drawn between tab and browser, never by device, because the tab has no way of knowing which device it is on.
_Avoid_: upsell banner, trial banner, "days left", promotional surface

**Purchase Link**:
Where a person buys TodoWerk, as ManagementPortal supplies it; TodoWerk never composes one, stores one or configures one. It arrives on a refusal as well as on a valid answer, which is the point of it: somebody who has just been told their Trial ended is exactly who wants it. So it reaches both the Licence Banner and the ended denied card. No link is a legitimate state and not a fault: where nothing is sold for TodoWerk no address arrives, and nothing is rendered rather than something dead. Shown in the browser and never inside the Teams Tab, on any device.
_Avoid_: buy link, upgrade URL, upsell link, store link (that is a Microsoft Marketplace listing), launch URL (where a hosted solution runs, a different thing nobody has built), "the purchase URL key" (it is a field on ManagementPortal's answer, not a configuration key)

**Channel**:
The route by which a Licence was acquired, recorded as data on the Licence. Never expressed as a client build flavour or baked key.
_Avoid_: distribution flavour, edition

**First-Party Path**:
ManagementPortal's server-to-server interface for CloudWerk-hosted solutions, through which TodoWerk resolves a Licence by tenant id and Entra object id.
_Avoid_: client validation, anonymous validate (that is the package-client path)

**ManagementPortal**:
A separate CloudWerk product: the multi-tenant licensing service behind CloudWerk's solutions, and the sole authority on every Licence.
_Avoid_: management portal (generic), admin portal, deployment portal (it does not deploy)
