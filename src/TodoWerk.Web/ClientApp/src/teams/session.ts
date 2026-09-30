import { ApiError } from '../api/http';

/**
 * The one problem code the tab is allowed to raise a consent popup on. Mirrors
 * `TeamsSsoErrors.ConsentRequired` on the server; every other failure there carries a different
 * code on purpose, because a tenant sent through a popup that cannot fix its problem is a tenant
 * sent through it forever.
 */
export const consentRequiredCode = 'TeamsSso.ConsentRequired';

export type TabSession =
  /** The exchange succeeded and the session cookie is being sent back. */
  | { kind: 'ready' }
  /**
   * The tenant has not approved TodoWerk and this person has not either, so the on-behalf-of
   * exchange had nothing to exchange for. The expected first run of every such tenant.
   */
  | { kind: 'consentRequired' }
  /**
   * The exchange succeeded and the very next request came back 401 — which means the cookie the
   * exchange set did not survive the frame. Nothing else produces that pair.
   */
  | { kind: 'cookiesBlocked' }
  /**
   * Over, and deliberately not coming back: somebody has had their data erased. The Teams identity
   * is still perfectly good, so re-running the bootstrap would sign them straight back in and
   * record them as arriving again — which is the opposite of what they asked for.
   */
  | { kind: 'erased' }
  /** Anything else, with the sentence to put on screen. */
  | { kind: 'failed'; message: string };

/**
 * Everything the bootstrap does that talks to somebody else, behind three methods — so the
 * decisions below can be tested against responses without a Teams client, a browser or a server.
 */
export interface TabSessionGateway {
  /** The Teams SSO token, from `getAuthToken()`. */
  acquireToken(): Promise<string>;

  /** Trades it for the session cookie. Throws `ApiError` carrying the problem's code. */
  exchange(token: string): Promise<void>;

  /** One ordinary authenticated request, made immediately after the exchange. */
  confirmSession(): Promise<void>;
}

/**
 * The tab's whole bootstrap decision, and the reason it is a function rather than three `await`s
 * inside a component: the three outcomes below are what the tab renders, and two of them are
 * conditions no fake server and no unit test of a component could otherwise reach.
 *
 * The confirming request is not belt and braces. Safari refuses to store TodoWerk's unpartitioned
 * cookie inside the Teams frame at all
 * ([ADR-0010](../../../../../docs/adr/0010-teams-tab-session-and-framing.md)), and the only visible
 * trace of that is this exact pair: an exchange that succeeded, followed by a 401. Without the
 * second request the tab would sit signed out with nothing to say.
 */
export async function establishSession(gateway: TabSessionGateway): Promise<TabSession> {
  let token: string;

  try {
    token = await gateway.acquireToken();
  } catch {
    return {
      kind: 'failed',
      message:
        'Microsoft Teams did not give TodoWerk a sign-in token for you. Try reloading this tab.',
    };
  }

  try {
    await gateway.exchange(token);
  } catch (error) {
    if (error instanceof ApiError && error.problem.code === consentRequiredCode) {
      return { kind: 'consentRequired' };
    }

    return { kind: 'failed', message: describe(error) };
  }

  try {
    await gateway.confirmSession();
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return { kind: 'cookiesBlocked' };
    }

    return { kind: 'failed', message: describe(error) };
  }

  return { kind: 'ready' };
}

function describe(error: unknown): string {
  if (error instanceof ApiError) {
    return (
      error.problem.detail ?? 'TodoWerk could not sign you in. Try again in a moment.'
    );
  }

  return 'TodoWerk could not be reached. Try again in a moment.';
}
