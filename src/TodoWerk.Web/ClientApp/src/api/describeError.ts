import { ApiError } from './http';

/**
 * The server's own words for a refusal, with nothing added. Wrapping its sentence in one of ours
 * would assert the same thing twice, the second time on the authority of code that knows less
 * (CONTRIBUTING § What a failure is allowed to say).
 *
 * The two exceptions are the two the server cannot word for itself: a session that has expired,
 * where the answer carries no sentence a reader could act on, and a request that never arrived.
 */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    return error.status === 401
      ? 'Your session has expired. Sign in again to continue.'
      : (error.problem.detail ?? error.message);
  }

  return 'Could not reach TodoWerk. Check your connection and try again.';
}
