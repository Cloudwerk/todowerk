import type { ProblemDetails } from './types';

/** Error thrown for any non-2xx API response, carrying the RFC 7807 payload. */
export class ApiError extends Error {
  readonly problem: ProblemDetails;

  constructor(problem: ProblemDetails) {
    super(problem.title ?? `Request failed with status ${problem.status ?? 'unknown'}`);
    this.name = 'ApiError';
    this.problem = problem;
  }

  get status(): number | undefined {
    return this.problem.status;
  }
}

/**
 * How long one request may go unanswered before the client gives up on it. Sized against the
 * server rather than against anybody's patience.
 *
 * The Licence gate sits in front of every authenticated endpoint, and behind it any request at all
 * can end up waiting on the licence lookup — which endpoint waits is decided by the state of that
 * person's cached answer rather than by the path, so a "local" read is only local while their
 * answer is fresh. The server bounds the licence lookup, at a configured value of at most one
 * minute; ninety seconds clears that ceiling. The client cannot see which value is configured, so
 * the number here has to clear the ceiling and not the default.
 *
 * Below the configured value this would be worse than having no timeout at all. The browser would
 * abandon an answer the server is about to send, and the server deliberately does not record a
 * caller's cancellation as an outage — so the thirty-second floor that stops it calling the portal
 * once per request never gets stamped, and somebody being served on the fail-open window would pay
 * a full portal timeout on every poll instead of one slow request with the rest answered from
 * cache.
 *
 * Ninety seconds is the minute the server may take plus thirty for everything that is not the
 * portal: the proxy, TLS, TodoWerk's own work, the body. Still well inside the minutes a browser's
 * own network stack waits before giving up, which is what this replaces — and a read that settles
 * is what re-arms the denied card's retry, which is the reason any of this matters.
 */
export const REQUEST_TIMEOUT_MS = 90_000;

/**
 * Typed fetch against the app's own API. Sends the session cookie, expects JSON, turns error
 * responses into ApiError with parsed ProblemDetails — and gives up after REQUEST_TIMEOUT_MS.
 */
export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  // A controller of the client's own, aborted by whichever comes first: the caller's signal, or
  // the clock. Deliberately not `AbortSignal.any([signal, AbortSignal.timeout(ms)])`, which is the
  // idiomatic spelling of exactly this — it is missing on Safari before 17.4 and *throws* rather
  // than degrading, and this runs inside the shell that both hosts render, so on that Safari the
  // throw would be the tab failing to paint rather than a slow retry. TypeScript's DOM library
  // declares both, so nothing but this comment would have caught it. `AbortSignal.timeout` is also
  // scheduled on a clock of jsdom's own that the suite's fake timers cannot advance, so the
  // idiomatic version would be untestable here as well.
  //
  // The caller's signal is listened to and never aborted, so `signal.aborted` still means to
  // everybody reading it what it always meant: the component went away.
  const controller = new AbortController();
  const caller = init?.signal ?? null;
  let timedOut = false;

  const timer = setTimeout(() => {
    timedOut = true;
    controller.abort();
  }, REQUEST_TIMEOUT_MS);

  const forward = () => controller.abort();

  if (caller?.aborted) forward();
  else caller?.addEventListener('abort', forward, { once: true });

  try {
    const response = await fetch(path, {
      credentials: 'same-origin',
      ...init,
      // After the spread, so the controller's signal wins over the caller's: the caller's is
      // forwarded onto it above rather than passed through.
      signal: controller.signal,
      headers: {
        Accept: 'application/json',
        ...init?.headers,
      },
    });

    if (!response.ok) {
      throw new ApiError(await readProblem(response));
    }

    if (response.status === 204) {
      return undefined as T;
    }

    return (await response.json()) as T;
  } catch (error) {
    // `fetch` reports the clock's abort exactly as it reports the caller's, and whether the
    // rejection carries the signal's reason differs between browsers — so the flag decides which
    // happened rather than the error does. The platform's own name for it, so that a caller telling
    // the two apart reads `name` just as it would if `AbortSignal.timeout` had done the aborting.
    if (timedOut) {
      throw new DOMException(
        `TodoWerk did not answer ${path} within ${REQUEST_TIMEOUT_MS / 1000} seconds.`,
        'TimeoutError',
      );
    }

    throw error;
  } finally {
    // On every way out — an answer, a refusal, either abort — so a settled request leaves no clock
    // running against a controller nobody is listening to. In `finally` rather than after the
    // headers, so the timeout covers reading the body too.
    clearTimeout(timer);
    caller?.removeEventListener('abort', forward);
  }
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  const fallback: ProblemDetails = { status: response.status, title: response.statusText };
  const contentType = response.headers.get('content-type') ?? '';

  if (!contentType.includes('json')) {
    return fallback;
  }

  try {
    const body = (await response.json()) as ProblemDetails;
    return { ...fallback, ...body };
  } catch {
    return fallback;
  }
}
