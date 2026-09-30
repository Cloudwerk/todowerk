import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { REQUEST_TIMEOUT_MS, apiFetch } from './http';

/**
 * The one property every request in this client shares: it settles.
 *
 * A request that hangs — something between the browser and TodoWerk swallowing the connection
 * rather than refusing it — would otherwise wait on the browser's own network stack to give up,
 * which is minutes and differs by browser. Anything armed off that read's answer would wait with
 * it: the denied Licence card's retry, and the Workbench polls, which refuse to start a second read
 * while one is still in flight.
 */
describe('apiFetch', () => {
  beforeEach(() => vi.useFakeTimers());

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('gives up on a request that never answers, as a timeout rather than as a denial', async () => {
    const sent = hangs();
    const read = apiFetch('/api/licence');

    // Attached before the clock moves. A rejection with nobody yet listening is reported by the
    // runner as unhandled, and the test that then fails is a different one.
    const outcome = expect(read).rejects.toMatchObject({ name: 'TimeoutError' });

    await vi.advanceTimersByTimeAsync(REQUEST_TIMEOUT_MS - 1);
    expect(sent.signal?.aborted).toBe(false);

    await vi.advanceTimersByTimeAsync(1);
    await outcome;

    // The socket is let go of, rather than merely stopped being waited on.
    expect(sent.signal?.aborted).toBe(true);
  });

  it("reports the caller's own abort as an abort, and stops the clock with it", async () => {
    hangs();

    const controller = new AbortController();
    const read = apiFetch('/api/licence', { signal: controller.signal });
    const outcome = expect(read).rejects.toMatchObject({ name: 'AbortError' });

    controller.abort();

    await outcome;
    expect(vi.getTimerCount()).toBe(0);
  });

  it('leaves no clock running once the answer has landed', async () => {
    vi.stubGlobal('fetch', () =>
      Promise.resolve(
        new Response('{"ok":true}', { status: 200, headers: { 'content-type': 'application/json' } }),
      ),
    );

    await expect(apiFetch('/api/me')).resolves.toEqual({ ok: true });
    expect(vi.getTimerCount()).toBe(0);
  });
});

/**
 * A fetch that never answers, and does the one thing the real one does with its signal: rejects
 * with an `AbortError` when it is aborted. A stub that ignored the signal would make the timeout
 * look broken when it is the stub that is.
 */
function hangs() {
  const sent: { signal?: AbortSignal } = {};

  vi.stubGlobal('fetch', (_input: RequestInfo | URL, init?: RequestInit) => {
    sent.signal = init?.signal ?? undefined;

    return new Promise<Response>((_resolve, reject) => {
      init?.signal?.addEventListener(
        'abort',
        () => reject(new DOMException('The operation was aborted.', 'AbortError')),
        { once: true },
      );
    });
  });

  return sent;
}
