import { app, authentication } from '@microsoft/teams-js';

/**
 * Where every popup this client opens comes to rest, and the whole of what it does.
 *
 * It imports TeamsJS and nothing else — no React, no Fluent, no API client. That is a requirement
 * rather than tidiness: Teams gives an authentication popup a bounded time to report back, and a
 * document that spends it parsing a framework is a document that reports `CancelledByUser` for a
 * flow the person completed.
 *
 * `notifySuccess()` carries no payload, and there is nothing it could carry. Whatever the round
 * trip achieved — a consent grant, a fresh sign-in, an ended Microsoft session — is already on the
 * server and already in the session cookie, which is one of the reasons
 * [ADR-0010](../../../../../docs/adr/0010-teams-tab-session-and-framing.md) chose a cookie.
 *
 * The one thing it reads is the server saying a round trip ended without one. A popup that
 * reported success for a consent screen nobody approved would have the tab announce a grant that
 * does not exist and redraw the same invitation with no explanation. Server halves:
 * `SignInFailure.ReasonParameter` for a sign-in, `OnboardingEndpoints`' `ConsentParameter` for an
 * approval.
 */
const parameters = new URLSearchParams(window.location.search);

const failedWith =
  parameters.get('signin') ?? (parameters.get('consent') === 'not-granted' ? 'not-approved' : null);

app
  .initialize()
  .then(() =>
    failedWith === null
      ? authentication.notifySuccess()
      : authentication.notifyFailure(failedWith),
  )
  .catch(() => {
    // Opened outside Teams, or the handshake failed. There is no tab listening, so there is
    // nothing to notify and nothing to recover — the message below is for whoever is looking at
    // the window.
    document.body.textContent = 'You can close this window and return to Microsoft Teams.';
  });
