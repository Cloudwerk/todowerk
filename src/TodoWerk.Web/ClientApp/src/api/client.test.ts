import { afterEach, describe, expect, it, vi } from 'vitest';
import { confirmTeamsSession } from './client';

/**
 * One request in this client carries a header nothing else does, and the reason it is tested is
 * that the thing it buys happens somewhere no client test can see: the server logs a 401 on a
 * request wearing this header as the blocked third-party cookie rather than as an ordinary
 * unauthenticated call. Drop the header and the tab still behaves correctly and the production log
 * goes quiet, which is the failure this pins.
 */
describe('confirmTeamsSession', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('marks the confirming request as the Teams tab bootstrap', async () => {
    const fetched = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ displayName: 'Signed In' }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );

    vi.stubGlobal('fetch', fetched);

    await confirmTeamsSession();

    const [path, init] = fetched.mock.calls[0] as [string, RequestInit];

    expect(path).toBe('/api/me');

    // Spelled out rather than read from `teamsBootstrapHeader`, which would assert only that the
    // constant equals itself. This is a wire contract with a C# middleware that spells it out too:
    // between them the two halves cannot be renamed apart without a test failing, which is the
    // failure mode worth catching — a tab sending a header nothing reads goes on working, and takes
    // the diagnostic with it in silence.
    expect(new Headers(init.headers).get('X-TodoWerk-Teams-Bootstrap')).toBe('1');
  });
});
