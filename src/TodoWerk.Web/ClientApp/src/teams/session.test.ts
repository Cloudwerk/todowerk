import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/http';
import { consentRequiredCode, establishSession } from './session';
import type { TabSessionGateway } from './session';

/**
 * The tab's bootstrap makes three decisions that a person sees the whole difference between: sign
 * them in, offer the consent popup, or say the browser will not keep the session. Two of the three
 * are conditions no fake server reproduces on its own — one is a specific problem code, the other
 * is a *pair* of responses — so they are tested here, against the responses, rather than left to a
 * live walk to discover.
 */
function gateway(overrides: Partial<TabSessionGateway> = {}): TabSessionGateway {
  return {
    acquireToken: vi.fn().mockResolvedValue('a-teams-sso-token'),
    exchange: vi.fn().mockResolvedValue(undefined),
    confirmSession: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  };
}

const consentRequired = new ApiError({ status: 401, title: 'Not authorized', code: consentRequiredCode });

const unauthorized = new ApiError({ status: 401, title: 'Not signed in', code: 'Auth.SignInRequired' });

describe('establishSession', () => {
  it('signs in when the exchange succeeds and the session survives', async () => {
    const parts = gateway();

    expect(await establishSession(parts)).toEqual({ kind: 'ready' });
    expect(parts.exchange).toHaveBeenCalledWith('a-teams-sso-token');
  });

  it('offers the consent popup only for the code the server reserves for it', async () => {
    const result = await establishSession(
      gateway({ exchange: vi.fn().mockRejectedValue(consentRequired) }),
    );

    expect(result).toEqual({ kind: 'consentRequired' });
  });

  /**
   * The failure that looks identical from the outside — a 401 from the same endpoint — and must
   * not raise a popup, because the popup would change nothing and the person would meet it again
   * on every reload.
   */
  it('does not offer the popup for a 401 that is not the consent code', async () => {
    const result = await establishSession(
      gateway({
        exchange: vi.fn().mockRejectedValue(
          new ApiError({ status: 401, title: 'Not authorized', code: 'TeamsSso.ExchangeFailed' }),
        ),
      }),
    );

    expect(result.kind).toBe('failed');
  });

  /**
   * ADR-0010's known cost, detected by its signature rather than guessed at: the exchange
   * succeeded, and the very next request came back 401. Nothing else produces that pair — a real
   * sign-in failure fails the exchange itself.
   */
  it('names the blocked third-party cookie when the exchange succeeded and the next request did not', async () => {
    const result = await establishSession(
      gateway({ confirmSession: vi.fn().mockRejectedValue(unauthorized) }),
    );

    expect(result).toEqual({ kind: 'cookiesBlocked' });
  });

  it('does not blame the cookie when the confirming request failed for another reason', async () => {
    const result = await establishSession(
      gateway({
        confirmSession: vi.fn().mockRejectedValue(
          new ApiError({ status: 503, title: 'Request failed', detail: 'TodoWerk is restarting.' }),
        ),
      }),
    );

    expect(result).toEqual({ kind: 'failed', message: 'TodoWerk is restarting.' });
  });

  /** The confirming request is only reached when there is something to confirm. */
  it('never reaches the confirming request when the exchange failed', async () => {
    const parts = gateway({ exchange: vi.fn().mockRejectedValue(consentRequired) });

    await establishSession(parts);

    expect(parts.confirmSession).not.toHaveBeenCalled();
  });

  it('says so when Teams gives it no token at all', async () => {
    const parts = gateway({ acquireToken: vi.fn().mockRejectedValue(new Error('no host')) });

    const result = await establishSession(parts);

    expect(result.kind).toBe('failed');
    expect(parts.exchange).not.toHaveBeenCalled();
  });

  it('says something readable when the server cannot be reached at all', async () => {
    const result = await establishSession(
      gateway({ exchange: vi.fn().mockRejectedValue(new TypeError('Failed to fetch')) }),
    );

    expect(result).toEqual({
      kind: 'failed',
      message: 'TodoWerk could not be reached. Try again in a moment.',
    });
  });
});
