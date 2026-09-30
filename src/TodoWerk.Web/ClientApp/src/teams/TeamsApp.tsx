import { useCallback, useEffect, useMemo, useState } from 'react';
import { FluentProvider } from '@fluentui/react-components';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { confirmTeamsSession, exchangeTeamsSsoToken } from '../api/client';
import { TeamsHostProvider } from '../host';
import type { TeamsHost } from '../host';
import { routes } from '../routes';
import {
  ConsentRequiredCard,
  CookiesBlockedCard,
  ErasedCard,
  StartingCard,
  TabFailureCard,
} from './TabCards';
import { establishSession } from './session';
import type { TabSession } from './session';
import { teamsThemeFor } from './theme';
import type { TeamsThemeName } from './theme';

interface TeamsAppProps {
  host: TeamsHost;

  /** The Teams SSO token, from `getAuthToken()`. Re-asked for on every attempt, never cached. */
  acquireToken: () => Promise<string>;

  /** Read from the tab's own URL, so the first paint is already the right one. */
  initialTheme: TeamsThemeName | null;

  /** Teams reporting that the person changed the theme while the tab was open. */
  subscribeToTheme: (onChanged: (theme: string) => void) => void;
}

/**
 * The tab: its theme, its session, and — once there is one — the Workbench.
 *
 * Nothing of the Workbench renders before the session is established or one of the three cards is
 * chosen. That is deliberate: the Workbench's first act is to read the inventory, and rendering it
 * against a session that does not exist would produce a screen full of sign-in prompts written for
 * a browser, in a surface where signing in means something else entirely.
 */
export function TeamsApp({ host, acquireToken, initialTheme, subscribeToTheme }: TeamsAppProps) {
  const [themeName, setThemeName] = useState<string | null>(initialTheme);
  const [session, setSession] = useState<TabSession | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => subscribeToTheme(setThemeName), [subscribeToTheme]);

  useEffect(() => {
    let current = true;

    setSession(null);

    void establishSession({
      acquireToken,
      exchange: (token) => exchangeTeamsSsoToken(token),
      // Any authenticated read would do; this is the cheapest one the app has, and its answer is
      // the same session the Workbench is about to use. It goes out marked as the bootstrap's
      // confirmation, which is what lets the server log a 401 here as the blocked cookie rather
      // than as one more anonymous call.
      confirmSession: () => confirmTeamsSession().then(() => undefined),
    }).then((result) => {
      if (current) {
        setSession(result);
      }
    });

    return () => {
      current = false;
    };
  }, [acquireToken, attempt]);

  const retry = useCallback(() => setAttempt((previous) => previous + 1), []);

  // Not a retry and not a reload: erasure leaves a working Teams identity behind, so anything that
  // re-ran the bootstrap would sign the person back in and count them as arriving again.
  const sessionEnded = useCallback(() => setSession({ kind: 'erased' }), []);

  // A memory router, not a browser one. The tab's address is the document Teams loaded, and
  // pushing a client route into an iframe's history navigates the frame away from it.
  const router = useMemo(() => createMemoryRouter(routes), []);

  return (
    <FluentProvider theme={teamsThemeFor(themeName)}>
      <TeamsHostProvider host={host} onSessionEnded={sessionEnded}>
        {session === null && <StartingCard />}
        {session?.kind === 'consentRequired' && <ConsentRequiredCard onGranted={retry} />}
        {session?.kind === 'cookiesBlocked' && <CookiesBlockedCard />}
        {session?.kind === 'erased' && <ErasedCard />}
        {session?.kind === 'failed' && <TabFailureCard message={session.message} onRetry={retry} />}
        {session?.kind === 'ready' && <RouterProvider router={router} />}
      </TeamsHostProvider>
    </FluentProvider>
  );
}
