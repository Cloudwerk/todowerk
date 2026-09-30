import { createContext, useCallback, useContext, useMemo, useState } from 'react';
import type { MouseEvent, ReactNode } from 'react';

/**
 * The tab's own document. The same string the server branches its framing headers on and the app
 * manifest points `contentUrl` at — the path is the discriminator, and there is deliberately no
 * `isInTeams` flag anywhere.
 */
export const teamsTabPath = '/teams';

/**
 * Where every popup this client opens has to end. It initializes TeamsJS, reports success and
 * closes; a popup that lands anywhere else never reports back, and Teams eventually decides the
 * person cancelled it.
 */
export const teamsAuthEndPath = '/teams/auth-end';

/**
 * What the Teams tab can do that a browser page cannot, and the browser SPA has none of it.
 * Deliberately two methods and no TeamsJS types: every component that needs the tab to behave
 * differently reaches for this, and nothing but the tab's own entry point imports TeamsJS.
 */
export interface TeamsHost {
  /** Opens a URL outside the frame, in the person's own browser. */
  openInBrowser(url: string): void;

  /**
   * Runs a TodoWerk URL as a Teams authentication popup. Resolves when the popup reports success,
   * rejects when it is closed or fails. Always a TodoWerk URL: the round trip to Microsoft happens
   * in the middle and the flow starts and ends on this domain.
   */
  authenticate(url: string): Promise<void>;
}

interface TeamsSurface {
  host: TeamsHost;

  /**
   * Says the session this tab was running on is over and is not coming back — which in practice
   * means somebody has just had their data erased. The tab renders a last screen and stops; it
   * must not simply re-run its bootstrap, because the Teams identity is still perfectly good and
   * would sign the person straight back in and record them as arriving again.
   */
  sessionEnded(): void;
}

const TeamsSurfaceContext = createContext<TeamsSurface | null>(null);

export function TeamsHostProvider({
  host,
  onSessionEnded,
  children,
}: {
  host: TeamsHost;
  onSessionEnded: () => void;
  children: ReactNode;
}) {
  const surface = useMemo<TeamsSurface>(
    () => ({ host, sessionEnded: onSessionEnded }),
    [host, onSessionEnded],
  );

  return <TeamsSurfaceContext.Provider value={surface}>{children}</TeamsSurfaceContext.Provider>;
}

/** The host, or `null` in the browser SPA — which is every component's way of asking where it is. */
export function useTeamsHost(): TeamsHost | null {
  return useContext(TeamsSurfaceContext)?.host ?? null;
}

/**
 * Ends the tab's session. A no-op in the browser SPA, where the same moment is a real navigation
 * the server drives.
 */
export function useSessionEnded(): () => void {
  const surface = useContext(TeamsSurfaceContext);

  return useCallback(() => surface?.sessionEnded(), [surface]);
}

/** Where the browser SPA lives, absolute, because `openLink` will not take a path. */
export function browserAppUrl(): string {
  return new URL('/', window.location.origin).toString();
}

/**
 * Turns a TodoWerk URL that starts a round trip through Microsoft into the one a popup should be
 * opened at: the same flow, landing on the tab's auth-end page instead of wherever it would have
 * put a browser.
 */
export function popupUrl(href: string): string {
  const url = new URL(href, window.location.origin);
  url.searchParams.set('returnUrl', teamsAuthEndPath);

  return url.toString();
}

export interface EntraRoundTrip {
  /**
   * Attach to any control whose `href` starts a round trip ending at the identity provider. In the
   * browser it does nothing and the navigation happens as it always did; in the tab it takes the
   * navigation over, because sign-in pages refuse to be framed and a tab that navigated to one
   * would render Microsoft's refusal instead of TodoWerk.
   */
  intercept: (event: MouseEvent<HTMLElement>) => void;

  /** True while a popup is open, so the control that opened it can say so. */
  running: boolean;

  /** What went wrong, in a sentence — including the person simply closing the popup. */
  failure: string | null;
}

/**
 * The one mechanism behind all four surfaces that end in a navigation to
 * `login.microsoftonline.com`: starting Tenant Consent, approving again, reconnecting after the
 * refresh token expires, and the last leg of erasure
 * ([ADR-0010](../../../../docs/adr/0010-teams-tab-session-and-framing.md)).
 *
 * @param onCompleted Run after the popup reports success — usually a re-read, because what the
 * popup changed is on the server and the tab is still showing what it read before.
 */
/**
 * What to tell somebody whose popup came back with nothing.
 *
 * TeamsJS rejects with the reason the popup reported, and the one reason TodoWerk's own auth-end
 * document sends is the one that is not a closed window: the server said nothing was approved.
 * Everything else — a window somebody shut, a timeout, a host that could not open one — keeps the
 * sentence it always had, because those really are the same event to the person reading it.
 */
function sentenceFor(error: unknown): string {
  const reason = error instanceof Error ? error.message : String(error ?? '');

  return reason.includes('not-approved')
    ? 'Nothing was approved, so nothing has changed. An administrator can approve TodoWerk for everyone in your organisation.'
    : 'That window closed before it finished. Nothing was changed — try again.';
}

export function useEntraRoundTrip(onCompleted?: () => void): EntraRoundTrip {
  const host = useTeamsHost();
  const [running, setRunning] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  const intercept = useCallback(
    (event: MouseEvent<HTMLElement>) => {
      if (host === null) {
        return;
      }

      // Read before preventDefault, from the element the handler is attached to rather than from
      // whatever inside it was clicked.
      const href = event.currentTarget.getAttribute('href');
      event.preventDefault();

      if (href === null) {
        return;
      }

      setRunning(true);
      setFailure(null);

      host
        .authenticate(popupUrl(href))
        .then(() => onCompleted?.())
        .catch((error: unknown) => setFailure(sentenceFor(error)))
        .finally(() => setRunning(false));
    },
    [host, onCompleted],
  );

  return { intercept, running, failure };
}
