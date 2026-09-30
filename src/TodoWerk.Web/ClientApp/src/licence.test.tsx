import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { REQUEST_TIMEOUT_MS } from './api/http';
import { LicenceProvider, useLicence } from './licence';
import type { LicenceState } from './licence';

/**
 * What the client does with the two problem codes, and what it does with a failure that is
 * neither.
 *
 * A denial is sticky. It is left behind only for a licence answer or another denial, never for a
 * dropped connection or a 502 from a fronting proxy: dropping it there would take the card away and
 * render the Workbench over an API answering 403 to everything, and because only a denial schedules
 * the retry, the client would never ask again until somebody reloaded the page.
 */
describe('LicenceProvider', () => {
  beforeEach(() => vi.useFakeTimers({ shouldAdvanceTime: true }));

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('reads the Licence once and publishes it', async () => {
    answerWith([licensed()]);
    render(<Probe />);

    await settlesAs('licensed');
  });

  it('publishes the denial and its code', async () => {
    answerWith([denied('Licensing.Ended')]);
    render(<Probe />);

    await settlesAs('denied');
    expect(screen.getByTestId('code').textContent).toBe('Licensing.Ended');
  });

  /**
   * A denied person is refused `/api/licence` like everything else, so the refusal itself is the
   * only way anything reaches the card they can see. The purchase URL travels on it for that
   * reason, as `contact` does.
   */
  it('carries the purchase URL off the refusal itself', async () => {
    answerWith([denied('Licensing.Ended', { purchaseUrl: 'https://portal.test/Solutions' })]);
    render(<Probe />);

    await settlesAs('denied');
    expect(screen.getByTestId('purchase').textContent).toBe('https://portal.test/Solutions');
  });

  it('carries no purchase URL when the refusal named none', async () => {
    answerWith([denied('Licensing.Ended')]);
    render(<Probe />);

    await settlesAs('denied');
    expect(screen.getByTestId('purchase').textContent).toBe('');
  });

  /**
   * Anything but a string is nothing at all. A problem document is server-shaped rather than
   * type-checked, and what a bad value would become on screen is a link somewhere nobody chose.
   */
  it('ignores a purchase URL that is not a string', async () => {
    answerWith([denied('Licensing.Ended', { purchaseUrl: 42 })]);
    render(<Probe />);

    await settlesAs('denied');
    expect(screen.getByTestId('purchase').textContent).toBe('');
  });

  it('keeps the denial when a later read fails for a reason that is not a denial', async () => {
    answerWith([denied('Licensing.CouldNotBeVerified'), () => Promise.reject(new TypeError('offline'))]);
    render(<Probe />);

    await settlesAs('denied');

    await vi.advanceTimersByTimeAsync(31_000);

    // Still denied. Read rather than waited for, because what is being claimed here is that
    // nothing changed — a wait would pass on the state it was given.
    expect(statusText()).toBe('denied');

    // And it asked again.
    await waitFor(() => expect(calls()).toBeGreaterThan(1));
  });

  /**
   * The other half of the sentence above.
   *
   * A denial is kept when a retry fails transiently — and keeping it must not mean stopping. The
   * "could not be verified" card clears itself with no button and no reload, and only a denial
   * schedules the next read, so a denial that survives without re-arming the timer is a card
   * that stays on the screen until somebody reloads the page. One dropped connection is enough to
   * reach it, which is the same dropped connection that put the card there.
   */
  it('keeps asking after a retry that fails for a reason that is not a denial', async () => {
    answerWith([denied('Licensing.CouldNotBeVerified'), () => Promise.reject(new TypeError('offline'))]);
    render(<Probe />);

    await settlesAs('denied');
    expect(calls()).toBe(1);

    await vi.advanceTimersByTimeAsync(31_000);
    await waitFor(() => expect(calls()).toBe(2));

    // The read after the one that failed, which only a re-armed timer schedules.
    await vi.advanceTimersByTimeAsync(31_000);
    await waitFor(() => expect(calls()).toBe(3));
  });

  /**
   * The third way a retry can fail to come back: it never comes back at all. Nothing here is armed
   * off a read that has not answered, so without a clock of its own the client would leave the card
   * there until the browser gave up in its own time — minutes, and a different number in each
   * browser. The client's own clock makes a hang into an answer like any other.
   */
  it('keeps asking after a read that never comes back at all', async () => {
    answerWith([denied('Licensing.CouldNotBeVerified'), hangs(), licensed()]);
    render(<Probe />);

    await settlesAs('denied');

    await vi.advanceTimersByTimeAsync(31_000);
    await waitFor(() => expect(calls()).toBe(2));

    // The read that hangs, and the client's own clock ending the wait. The card is kept, because a
    // timeout is not news about anybody's Licence — and waited for rather than read, because what
    // arms the next attempt is the state this settles into.
    await vi.advanceTimersByTimeAsync(REQUEST_TIMEOUT_MS);
    await waitFor(() => expect(statusText()).toBe('denied'));

    // And it asked again: without the timeout there would be no answer to arm anything off, and
    // this read would never happen.
    await vi.advanceTimersByTimeAsync(31_000);
    await waitFor(() => expect(calls()).toBe(3));
    await waitFor(() => expect(statusText()).toBe('licensed'));
  });

  it('restores the Workbench when a retry succeeds, with no reload', async () => {
    answerWith([denied('Licensing.CouldNotBeVerified'), licensed()]);
    render(<Probe />);

    await settlesAs('denied');

    await vi.advanceTimersByTimeAsync(31_000);

    await waitFor(() => expect(statusText()).toBe('licensed'));
  });

  it('does not turn an ordinary failure into a denial', async () => {
    answerWith([() => Promise.reject(new TypeError('offline'))]);
    render(<Probe />);

    await waitFor(() => expect(statusText()).toBe('unknown'));
  });
});

/**
 * Waits for the answer, rather than for the element carrying it.
 *
 * `findByTestId('status')` is no barrier here: the probe renders that span on its first pass, while
 * the read is still in flight, so it is found while the state still says `loading` and whatever is
 * asserted next is asserted against a read that may not have landed. It would pass only while the
 * turns of the loop Testing Library spends before returning happen to outnumber those the fetch
 * needs.
 */
function settlesAs(status: LicenceState['status']) {
  return waitFor(() => expect(statusText()).toBe(status));
}

/** No jest-dom in this suite: every assertion reads the DOM through the standard API. */
function statusText() {
  return screen.getByTestId('status').textContent;
}

function Probe() {
  return (
    <LicenceProvider>
      <Report />
    </LicenceProvider>
  );
}

function Report() {
  const licence = useLicence();

  return (
    <>
      <span data-testid="status">{licence.status}</span>
      <span data-testid="code">{licence.status === 'denied' ? licence.code : ''}</span>
      <span data-testid="purchase">
        {licence.status === 'denied' ? (licence.purchaseUrl ?? '') : ''}
      </span>
    </>
  );
}

let requests = 0;

function calls() {
  return requests;
}

/**
 * Answers each read from a queue and repeats the last answer once it runs dry, so a test about
 * retrying does not have to script one reply per attempt it hopes will not happen.
 */
function answerWith(replies: ((init?: RequestInit) => Promise<Response>)[]) {
  requests = 0;
  let index = 0;

  vi.stubGlobal('fetch', (_input: RequestInfo | URL, init?: RequestInit) => {
    requests += 1;

    const reply = replies[Math.min(index, replies.length - 1)];
    index += 1;

    return reply(init);
  });
}

/**
 * A read that never answers — and that rejects when its signal is aborted, which is the one thing
 * the real `fetch` does with it. A stub ignoring the signal would make the client's own timeout
 * look broken when it is the stub that is.
 */
function hangs() {
  return (init?: RequestInit) =>
    new Promise<Response>((_resolve, reject) => {
      init?.signal?.addEventListener(
        'abort',
        () => reject(new DOMException('The operation was aborted.', 'AbortError')),
        { once: true },
      );
    });
}

function licensed() {
  return () =>
    Promise.resolve(
      new Response(
        JSON.stringify({
          kind: 'Tenant',
          endsAt: '2027-03-14T00:00:00Z',
          banner: 'None',
          mayOfferTenantConsent: true,
          purchaseUrl: null,
        }),
        { status: 200, headers: { 'content-type': 'application/json' } },
      ),
    );
}

function denied(code: string, extensions: Record<string, unknown> = {}) {
  return () =>
    Promise.resolve(
      new Response(JSON.stringify({ status: 403, code, detail: 'No.', ...extensions }), {
        status: 403,
        headers: { 'content-type': 'application/problem+json' },
      }),
    );
}
