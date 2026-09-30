# The Handbook is split by kinship

TodoWerk documents itself for two readers who are not contributors. The **Handbook** is a Guide for
the person using it and an Administrator's Guide for the person deciding whether to allow it.
TodoWerk also serves an About Page, because a manifest has to name something that does not ask for
a sign-in. These live in two places, and the line between them is kinship rather than convenience. The About
Page and the Administrator's Guide live **with the application**: markdown in `handbook/`, embedded
and rendered by the machinery that already serves the terms of use and the privacy notice, at
`/about` and `/administrators`, on every deployment, Self-Host included. The Guide is published
separately, for the Hosted Service alone, and is not part of this repository. Decided 2026-09-03.

## Why the Administrator's Guide lives with the application

It is the privacy notice's sibling in every respect that matters: the same reader, read at the same
moment (a consent decision), no pictures, exact, and at an address that stays the same for the life
of a listing, because `publisherDocsUrl` is submitted once and re-validated long afterwards. Everything
the privacy notice needed — an anonymous page on the deployment's own domain, an operator named from
configuration, a startup warning when nobody is named — the Administrator's Guide needs identically,
and it is one more embedded resource on machinery that exists. And a Self-Host's administrator is
asked the same consent question as anybody else's; a gate that withheld the page from them would cost
more than the page.

The About Page follows for a plainer reason. The Teams admin centre republishes `developer.websiteUrl`
as the app's support link, a support link that requires a sign-in is a must-fix in the Store
guidelines, and `/` is the application. The page describes the software, names the operator the
deployment is configured with, and links onward — imprint, terms of business, ordering, the Landing
Page — only where the `Handbook` configuration names a target. A Self-Host that names nothing serves
a page that is still true.

## Why the Guide does not

It is a product page's sibling: screenshots, prose, and a cadence of its own — a corrected sentence
should not be a container publish. CloudWerk already renders its product pages with tooling of its
own and a house template; the Guide takes the same, and its screenshots stay out of this
repository's history, where every change to the Workbench would leave stale pictures behind.
Self-Host gets none of it by decision: documentation of a Self-Host is its operator's to write, as
its terms and its App Package are its operator's to name.

The application learns the Guide's address from configuration (`Handbook:GuideUrl`) and tells the
browser through one anonymous endpoint; the Workbench shows a Help link when there is an address and
nothing when there is not. *(Amended in M7 — see below. The link is now one entry in a help menu
that carries the whole Handbook, and the condition on it is unchanged.)* Nothing in the public
repository knows where the Guide is, or whether there is one.

## A second telling, with the defaults pinned

The obvious request is that the Handbook have "one source rather than being duplicated from
`PRIVACY.md` and the ADRs by hand". Taken literally that is the wrong constraint: the Administrator's
Guide exists because [ADR-0008](0008-tenant-consent-is-delegated.md)'s telling is right for a
contributor and wrong for an administrator — the same facts, a different reader, different words.
Sharing the text, by transclusion or fragment includes, would produce a decision record that reads
like a brochure or a guide that reads like a decision record, and would break the property the legal
documents have of being readable as plain files on GitHub.

So the prose is deliberately a second telling, and what is tied mechanically is only what can
drift silently: the numbers. The floor of five, the thirty days of undo and the twelve months of
dormancy are configuration with defaults; `PRIVACY.md` states the defaults and says they can be
changed. The Administrator's Guide does better, because it can: it is rendered by the deployment it
describes and reads those three from the same options the code enforces, so it states this
installation's own settings rather than a document's memory of them. A test starts a deployment with
three non-default values and checks that the page says each of them. The privacy notice stays the
single source of itself; the Administrator's Guide links to it rather than restating it.

## Considered options

- **Everything with the application** — rejected: screenshots and Hosted-Service-only prose in the
  public repository, a gate to keep the Guide off Self-Hosts, and every Guide edit a deploy.
- **Everything with the Guide's tooling, the About Page aside** — rejected: the page
  `publisherDocsUrl` names would be served by a different process from the page `privacyUrl` names;
  the Administrator's Guide could not be tested against the constants it describes; and a Self-Host
  administrator would have no consent explanation at all.
- **A static site generator inside the public repository** (Astro, VitePress, Docusaurus) — rejected:
  a second toolchain in the build for four pages, and every page a build artefact rather than a file
  a contributor reads.
- **The Guide's generated output committed into this repository** — refused: the build output of
  tooling this repository does not contain, which no contributor could regenerate or review.
- **A page on cloudwerk.com for `websiteUrl`** — rejected: the validation tool warns when the
  manifest's addresses straddle domains, and the legal documents are on the deployment's domain for
  exactly that reason.

## Consequences

- The manifest template names two more addresses on `{{Host}}`: `developer.websiteUrl` becomes
  `/about`, and a root-level `publisherDocsUrl` names `/administrators`. Both hold for a Self-Host
  without a new value, as `privacyUrl` and `termsOfUseUrl` already do. Every built package changes,
  and the Store submission re-validates against the new addresses.
- A `Handbook` configuration section, every value optional and every unset one named at startup
  outside Development: `GuideUrl`, `ImprintUrl`, `TermsOfBusinessUrl`, `OrderUrl`, `LandingPageUrl`.
  The support contact is `Legal:OperatorContact`; the link to the issue tracker is the software's
  and is not configurable.
- The repository ships the screenshot seeder in `scripts/`, because a populated Workbench is
  something a contributor needs too.
- The Guide is English, light theme, the browser Workbench only, and pictures nothing it cannot
  stage: the *unused for months* flag is computed from Graph's `lastModifiedDateTime`, which nobody
  can backdate, so it is described rather than shown.
- The legal pages' header gains links to the About Page, the Administrator's Guide and — when one is
  configured — the Guide. It gains no commerce.

## Amendment (M7): the Handbook is reached from one control, not from three places

The decision above says where each half of the Handbook is served and who serves it. It said almost
nothing about how a person signed into TodoWerk reaches either, and without a decision three
unrelated routes grew: the Guide as a text link in the row of screen names, the Administrator's
Guide from the two surfaces that ask for a Tenant Consent approval, and the About Page from nowhere
at all — though the App Package names it as the installation's support link, so an administrator
arrives on it from the Teams admin centre and the product itself never offered it.

They are now one control in the header: a help button carrying the Guide where there is one, the
About Page, the Administrator's Guide, and below a divider the terms of use and the privacy notice.
The set and its order are the served documents' own — `DocumentNav.For` — so a reader meets the same
five in the same order whether they are inside the product or on one of the pages. The divider is
where the Handbook entry in [CONTEXT.md](../../CONTEXT.md), "beside the terms of use and the privacy
notice but not among them", lands in the menu.

Two things do not change. The Guide is still the one entry that can be absent, on the same
condition and from the same endpoint: a Self-Host draws no entry rather than a dead one. And the
Administrator's Guide is still linked from the Tenant Consent invitation, which is where an
administrator meets the decision the page is written for — named there now, and described, so the
same page is recognisable in both places.

This amendment left two things undone and said so. The amendment below closes both.

## Amendment (M7): the control is outside the sign-in, and the list has one source

The amendment above left two things open, and named them. Both are settled.

The control does not wait for a session. It sat inside the header's signed-in half, so the one
reader this ADR insists the pages are anonymous *for* — an administrator deciding whether to allow
the product, somebody evaluating it who never will sign in — met the sign-in card and no route to
either page. The screens and the session controls stay behind the sign-in, because those belong to
somebody; the documents do not, and neither does the way to them. `/api/handbook` was already
anonymous, so nothing had to be opened to do it.

The set, the order and the wording have one source. The claim above, that a reader meets the same
five in the same order inside the product and on the pages, rested on two lists that agreed by
inspection. By the time it was written they had already stopped agreeing: the menu offered the
Guide first and the served pages offered it third. Nothing failed. `GET /api/handbook`
now answers with `DocumentNav`'s own list rather than with the Guide's address alone, and the menu
renders what it is given, divider included — the divider from the group each entry carries, so a
sixth document lands on the right side of it without anybody remembering a line of code exists. The
client holds no path of its own except `/administrators`, which two surfaces link to directly for
their own reasons, and which is written once and asserted against the server's answer.

The one thing the client still decides is that the control is drawn at all: an empty list is no
control rather than a control that opens on nothing.
