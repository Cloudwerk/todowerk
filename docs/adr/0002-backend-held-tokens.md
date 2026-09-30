# All Microsoft Graph tokens live in the backend; the browser only ever holds a session cookie

TodoWerk's SPA never acquires, stores, or forwards Graph tokens. Sign-in runs the authorization-code flow against a confidential client; refresh tokens, access tokens, and the token cache stay server-side. The Teams tab exchanges its SSO token on-behalf-of — also server-side.

The deciding constraint is background work: rename and merge run as queued jobs (one PATCH per task under Outlook throttling), and a job must be able to call Graph after the browser is long closed. Only server-held refresh tokens make that possible. The same holds for the reconnect flow after the ~90-day refresh-token expiry — the backend knows the token died and can ask the user to reconnect; a client-held token would just silently stop working between visits.

## Considered options

- **MSAL.js in the SPA, bearer tokens to the backend** — rejected: background jobs and scheduled rescans cannot run without a live browser session; tokens in browser storage widen the attack surface; and every Graph capability would have to be mirrored through the backend anyway, because the queued jobs are where the writes happen.
- **Hybrid (client tokens for reads, backend for jobs)** — rejected: two consent surfaces, two token lifetimes to reason about, no payoff.

## Consequences

- The backend is a confidential client with a server-side token cache, keyed per user and tenant. Cookie sessions require Data Protection keys persisted outside the instance, or every deploy signs everyone out.
- API endpoints authenticate the cookie session, never a bearer token. CSRF protection is therefore mandatory on all mutating endpoints. *(Amended in M4, see below: each of these two sentences now has one exception, and it is the same endpoint.)*
- Self-Host inherits the same flow with the customer's own app registration; nothing about token handling differs between deployments.

## Amendment (M4): one endpoint takes a bearer token, and it is the one that hands out the cookie

The consequence above was written about Graph tokens, and about a browser that would hold one. It
still holds for both. What M4 added is narrower, and it is recorded here because the sentence as
written is absolute and the code is not.

The Teams tab acquires a single sign-on token from its host and posts it to one endpoint,
`/api/teams/session`, which validates it, exchanges it on-behalf-of, and answers with the ordinary
`todowerk.session` cookie and no body. That endpoint authenticates a bearer token. Every other
endpoint in the application still names the cookie scheme and nothing else, and the authorization
policy they share has not changed.

[ADR-0010](0010-teams-tab-session-and-framing.md) records why the tab was not given a bearer session
outright. The objection was never to a bearer token as such. It was to the application having two
ways of knowing who is calling, on every endpoint, forever, and one endpoint whose only job is to
trade a token for the cookie does not create that.

The second sentence is amended by the same endpoint. Cross-site request forgery protection is
mandatory on every mutating endpoint *that accepts an ambient credential*, which is all of them but
this one: the only credential it takes is a token in an Authorization header, which a cross-site
page can neither obtain nor attach. The exemption is metadata on the endpoint that carries its
reason. Two tests enforce it: one checks that every mutating endpoint either validates the pair or
says why it cannot, and the other checks that the exemptions are exactly this endpoint, by name.
