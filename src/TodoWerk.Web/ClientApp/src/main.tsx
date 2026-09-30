import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { FluentProvider } from '@fluentui/react-components';
import { RouterProvider, createBrowserRouter } from 'react-router';
import { routes } from './routes';
import { useBrowserTheme } from './theme';

/**
 * The browser SPA's entry point. The CloudWerk brand theme is this document's and only this
 * document's: the Teams tab follows the Teams theme instead
 * ([ADR-0004 amendment](../../../../docs/adr/0004-fluent-ui-v9.md)).
 *
 * No TeamsHostProvider, so `useTeamsHost()` answers `null` everywhere below and every control that
 * behaves differently in the tab behaves the way it always did here.
 */
const router = createBrowserRouter(routes);

/**
 * A component rather than a bare `render` call, because which way round the ramp goes is a hook:
 * the operating system's colour scheme, read at the first render and followed while the page is
 * open.
 */
function BrowserApp() {
  const theme = useBrowserTheme();

  return (
    <FluentProvider theme={theme}>
      <RouterProvider router={router} />
    </FluentProvider>
  );
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserApp />
  </StrictMode>,
);
