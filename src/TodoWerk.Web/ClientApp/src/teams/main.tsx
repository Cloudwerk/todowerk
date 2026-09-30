import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { FluentProvider } from '@fluentui/react-components';
import { app, authentication } from '@microsoft/teams-js';
import type { TeamsHost } from '../host';
import { OutsideTeamsCard, SmallScreenCard } from './TabCards';
import { needsDesktopScreen } from './screen';
import { TeamsApp } from './TeamsApp';
import { teamsThemeFor, themeFromSearch } from './theme';

/**
 * The Teams tab's entry point, and the only module in this client that imports TeamsJS. The
 * browser bundle reaches nothing from here — a test asserts that against the built output rather
 * than trusting the import graph to have stayed tidy.
 */

/**
 * How long `app.initialize()` is given. Outside Teams there is no host to shake hands with and the
 * promise simply never settles, so a tab address opened in an ordinary browser would spin forever.
 * Generous enough for a cold Teams mobile webview and short enough that a person who took a wrong
 * turn is told so rather than left waiting.
 */
const initializeTimeoutMs = 4000;

const root = createRoot(document.getElementById('root')!);

void start();

async function start() {
  if (!(await initialized())) {
    // No Teams around us. The theme placeholders were never substituted either, so this renders in
    // the light theme, which is the right guess for a browser window.
    root.render(
      <StrictMode>
        <FluentProvider theme={teamsThemeFor(null)}>
          <OutsideTeamsCard />
        </FluentProvider>
      </StrictMode>,
    );

    return;
  }

  // The querystring is what the first paint used; the context is the authority, and on desktop the
  // two agree. Asked for anyway, because a manifest whose placeholders were not substituted — the
  // case mobile Teams is known for — has no theme in the URL at all.
  const context = await app.getContext().catch(() => null);
  const theme = themeFromSearch(window.location.search) ?? asThemeName(context?.app.theme);

  // A phone is told so before anything signs in. A capability not carried on mobile gets a
  // graceful answer there, and the Workbench is not carried: its table is unreadable at phone
  // width, and no sign-in would improve that.
  if (needsDesktopScreen(context?.app.host.clientType)) {
    root.render(
      <StrictMode>
        <FluentProvider theme={teamsThemeFor(theme)}>
          <SmallScreenCard />
        </FluentProvider>
      </StrictMode>,
    );
    app.notifySuccess();

    return;
  }

  root.render(
    <StrictMode>
      <TeamsApp
        host={teamsHost}
        acquireToken={() => authentication.getAuthToken()}
        initialTheme={theme}
        subscribeToTheme={(onChanged) => app.registerOnThemeChangeHandler(onChanged)}
      />
    </StrictMode>,
  );

  // Tells the Teams client the tab loaded. The manifest asks Teams for its native loading indicator
  // (`showLoadingIndicator`), which this call takes down; without it Teams waits thirty seconds and
  // then shows its own failure frame over a tab that is working.
  app.notifySuccess();
}

async function initialized(): Promise<boolean> {
  let timer: ReturnType<typeof setTimeout> | undefined;

  try {
    await Promise.race([
      app.initialize(),
      new Promise<never>((_, reject) => {
        timer = setTimeout(() => reject(new Error('Teams did not answer.')), initializeTimeoutMs);
      }),
    ]);

    return true;
  } catch {
    return false;
  } finally {
    clearTimeout(timer);
  }
}

const teamsHost: TeamsHost = {
  openInBrowser: (url) => {
    void app.openLink(url);
  },
  authenticate: (url) =>
    authentication
      .authenticate({
        url,
        // Big enough for Microsoft's own sign-in and consent screens, which are what fills it.
        width: 600,
        height: 620,
      })
      .then(() => undefined),
};

function asThemeName(theme: string | undefined) {
  return themeFromSearch(`theme=${encodeURIComponent(theme ?? '')}`);
}
