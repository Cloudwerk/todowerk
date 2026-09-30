import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from './http';

export type ApiState<T> =
  | { status: 'loading' }
  | { status: 'ready'; data: T }
  | { status: 'signedOut' }
  /**
   * `stale` carries the last data that did load. A grid that vanishes because one poll failed
   * punishes the user for a hiccup they could not see; the caller shows the stale data with the
   * error beside it, and drops to a bare error only when there is nothing to show.
   */
  | { status: 'error'; error: ApiError; stale?: T };

export interface ApiQuery<T> {
  state: ApiState<T>;
  /** True while a request is in flight — the first read, a changed key, or a refresh. */
  fetching: boolean;
  /**
   * True while what is on screen answers an earlier question: the key changed and the new answer
   * has not landed, so the rows are still the old sort, filter or page. A refresh of the same key
   * is never stale — the rows are current, only possibly old — which is what lets a poll during
   * a scan run without the table flickering every three seconds. Never true with nothing on
   * screen: an empty screen is not answering any question.
   */
  stale: boolean;
  /** Re-runs the request, keeping the current data on screen while it is in flight. */
  refresh: () => void;
}

/** Which question the last answer — data or failure — was for. */
interface Settled {
  key: string | null;
  attempt: number;
}

const NothingSettled: Settled = { key: null, attempt: -1 };

/**
 * A read that re-runs when its `key` changes — a sort, a filter, a page — and can be refreshed
 * on demand. The key is explicit rather than derived from `load`, because a closure is a new
 * value on every render and would re-fetch forever.
 *
 * The previous data stays on screen while the next request is in flight, and a superseded
 * request is aborted rather than left running: its socket is freed and its response can never
 * overwrite a newer one.
 *
 * A 401 is not an error to display — it means "not signed in", which the caller renders as the
 * sign-in prompt.
 */
export function useApiQuery<T>(load: (signal: AbortSignal) => Promise<T>, key: string): ApiQuery<T> {
  const [state, setState] = useState<ApiState<T>>({ status: 'loading' });
  const [attempt, setAttempt] = useState(0);

  // What the last answer was for, written in the same batch as the answer itself. Read against
  // the question being asked now it says whether a request is in flight — on the very render in
  // which the key changed, with no effect having to say so and no extra render for it to say so
  // in — and whether the data on screen still answers the current question.
  const [settled, setSettled] = useState(NothingSettled);

  // Kept in refs so a changing closure does not itself trigger the effect; `key` decides that —
  // and so the error path can reach for the previous data without depending on it.
  const loadRef = useRef(load);
  loadRef.current = load;
  const stateRef = useRef(state);
  stateRef.current = state;
  const settledRef = useRef(settled);
  settledRef.current = settled;

  // Written imperatively at the two moments a request starts and settles, because `refresh` has
  // to read it between renders and the rendered flag below is a render behind.
  const inFlight = useRef(false);

  useEffect(() => {
    const controller = new AbortController();
    inFlight.current = true;

    loadRef
      .current(controller.signal)
      .then((data) => {
        if (controller.signal.aborted) return;

        setSettled({ key, attempt });
        setState({ status: 'ready', data });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;

        // Data loaded under one sort or filter is not "the last good data" for a different one —
        // showing it under the new label would be a confident answer to a question nobody asked.
        const previous = stateRef.current;
        const stale =
          settledRef.current.key !== key
            ? undefined
            : previous.status === 'ready'
              ? previous.data
              : previous.status === 'error'
                ? previous.stale
                : undefined;

        setSettled({ key, attempt });

        if (error instanceof ApiError) {
          setState(error.status === 401 ? { status: 'signedOut' } : { status: 'error', error, stale });
          return;
        }

        setState({
          status: 'error',
          error: new ApiError({ status: 0, title: 'Could not reach TodoWerk.' }),
          stale,
        });
      })
      .finally(() => {
        // Only the run that is still current may declare the field clear. A superseded run
        // settles after its replacement has started, and clearing the flag there would hand
        // every later poll permission to abort the live request and start again — the very
        // starvation the flag exists to prevent.
        if (!controller.signal.aborted) inFlight.current = false;
      });

    return () => controller.abort();
  }, [key, attempt]);

  /**
   * Ignored while a request is already running, because refreshing aborts it and starts again:
   * a three-second poll against a query that takes longer would cancel its own work forever and
   * never render — and the slowest the query ever gets is during the scan the poll exists for.
   * A changed `key` still supersedes immediately; only asking for the same thing twice waits.
   */
  const refresh = useCallback(() => {
    if (!inFlight.current) setAttempt((previous) => previous + 1);
  }, []);

  const fetching = settled.key !== key || settled.attempt !== attempt;
  const showing = state.status === 'ready' || (state.status === 'error' && state.stale !== undefined);

  return {
    state,
    fetching,
    stale: fetching && showing && settled.key !== key,
    refresh,
  };
}

/**
 * A one-shot read, for a screen that has nothing to re-read on. The same request and the same
 * error handling as `useApiQuery`, with a fixed key so it runs once.
 */
export function useApi<T>(load: (signal: AbortSignal) => Promise<T>): ApiState<T> {
  return useApiQuery(load, 'once').state;
}
