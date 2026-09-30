import { createContext, useContext, useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { getLicence } from './api/client';
import { ApiError } from './api/http';
import { licenceCouldNotBeVerifiedCode, licenceEndedCode } from './api/types';
import type { Licence } from './api/types';

/** The two ways a person can be shut out, kept apart all the way to the card. */
export type DeniedCode = typeof licenceEndedCode | typeof licenceCouldNotBeVerifiedCode;

/**
 * What is known about the signed-in person's Licence. One shape, read from one place, so the
 * banner, the Tenant Overview panel and the consent invitation cannot disagree about the same
 * person.
 */
export type LicenceState =
  | { status: 'loading' }
  | { status: 'licensed'; licence: Licence }
  /**
   * `contact` is the operator's address and `purchaseUrl` is where TodoWerk is bought. Both are
   * carried by the server on the ended problem and only on that one — the other refusal is met
   * during a portal outage; it carries neither contact nor purchase link. Null everywhere else,
   * including in the tab, which would not render either of them anyway.
   */
  | {
      status: 'denied';
      code: DeniedCode;
      detail: string | null;
      contact: string | null;
      purchaseUrl: string | null;
    }
  /**
   * Not signed in, or the read failed for a reason that is not a denial. Deliberately not a
   * denial of its own: a 500 or a dropped connection is not news about anybody's Licence, and a
   * card claiming otherwise would be a product breaking itself over its own bookkeeping.
   */
  | { status: 'unknown' };

/**
 * How often a denied person's browser asks again. It matches the server's own retry floor after an
 * unreachable portal, so the "could not be verified" card clears within about half a minute of the
 * portal coming back — with no button and no reload.
 *
 * The ended card polls too, at the same rate. Somebody who has just bought a Licence is exactly
 * who is looking at it, and the alternative is telling them to reload a page that says their
 * access has ended.
 */
const RETRY_MS = 30_000;

const LicenceContext = createContext<LicenceState>({ status: 'unknown' });

/**
 * Reads the Licence once a session and keeps asking while the answer is a denial.
 *
 * Mounted inside the application shell, so both hosts get it from one place: the browser SPA and
 * the Teams tab render the same `App`, and a provider per entry point would be two things to keep
 * in step.
 */
export function LicenceProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<LicenceState>({ status: 'loading' });

  // `attempt` rather than a timer that sets state directly: a re-read has to abort cleanly when
  // the component goes away, and the effect below is what owns that.
  const [attempt, setAttempt] = useState(0);

  // How many reads have come back — with a Licence, with a denial, or with nothing. The retry
  // timer is armed off this rather than off `state`, because `state` cannot be relied on to
  // change: the updater below keeps a denial by returning the very same object, React then skips
  // the render, and an effect depending on `state` never runs again. One dropped connection would
  // then be enough to stop the polling for good and strand the card on the screen until somebody
  // reloaded the page — the outcome the note beside `RETRY_MS` says must not happen. A count
  // rather than a fresh copy of the state, because a count cannot be accidentally made equal
  // again by somebody tidying the updater.
  const [settled, setSettled] = useState(0);

  useEffect(() => {
    const controller = new AbortController();

    getLicence(controller.signal)
      .then((licence) => {
        if (controller.signal.aborted) return;

        setState({ status: 'licensed', licence });
        setSettled((previous) => previous + 1);
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;

        // A denial is only left behind for a licence answer or another denial — never for a
        // transient failure. Dropping it on a dropped connection or a 502 from a fronting proxy
        // would take the card away and render the Workbench over an API answering 403 to
        // everything.
        setState((previous) => {
          const next = read(error);

          return next.status === 'unknown' && previous.status === 'denied' ? previous : next;
        });
        setSettled((previous) => previous + 1);
      });

    return () => controller.abort();
  }, [attempt]);

  useEffect(() => {
    if (state.status !== 'denied') return undefined;

    const timer = setTimeout(() => setAttempt((previous) => previous + 1), RETRY_MS);

    return () => clearTimeout(timer);
    // `settled` arms it once per answer that came back; `state.status` stops it the moment the
    // answer stops being a denial. Deliberately not `attempt`, which changes when a read *starts*:
    // that would measure the half-minute from the asking rather than from the answer, and land the
    // next ask inside the server's own thirty-second floor — which `PortalLicenceResolver` stamps
    // from the moment it began handling the call, before the portal round trip — to be served the
    // denial it already had. Whether it cleared at thirty seconds or at sixty would then be decided
    // by network jitter.
  }, [state.status, settled]);

  return <LicenceStateProvider state={state}>{children}</LicenceStateProvider>;
}

/**
 * Publishes one Licence state to everything below it. Split out of the provider above so that the
 * reading and the publishing are separable: a screen under test is given a state directly rather
 * than a stubbed `fetch` and a wait, and what it renders is then a function of that state alone.
 */
export function LicenceStateProvider({
  state,
  children,
}: {
  state: LicenceState;
  children: ReactNode;
}) {
  return <LicenceContext.Provider value={state}>{children}</LicenceContext.Provider>;
}

/** The Licence, as every screen reads it. */
export function useLicence(): LicenceState {
  return useContext(LicenceContext);
}

/**
 * Which of the four states a failed read means. Only the two problem codes the Licensing module
 * publishes become a denial — anything else, a 401 included, leaves the rest of the application to
 * say what it always said about being signed out or unreachable.
 */
function read(error: unknown): LicenceState {
  if (!(error instanceof ApiError)) {
    return { status: 'unknown' };
  }

  const code = error.problem.code;

  if (code === licenceEndedCode || code === licenceCouldNotBeVerifiedCode) {
    const contact = error.problem.contact;
    const purchaseUrl = error.problem.purchaseUrl;

    return {
      status: 'denied',
      code,
      detail: error.problem.detail ?? null,
      contact: typeof contact === 'string' ? contact : null,
      // Read on both codes and rendered on one. Which refusal may show it is the card's decision
      // and the card's test, and the server sends it on the ended problem alone in any case —
      // guarding it twice is cheaper than a link appearing on the outage card by accident.
      purchaseUrl: typeof purchaseUrl === 'string' ? purchaseUrl : null,
    };
  }

  return { status: 'unknown' };
}
