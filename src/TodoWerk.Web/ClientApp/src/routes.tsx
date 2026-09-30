import type { RouteObject } from 'react-router';
import { App } from './App';
import { TenantOverview } from './pages/TenantOverview';
import { Workbench } from './pages/Workbench';

/**
 * The screens, shared by both entry points. The browser puts them behind a browser router at `/`;
 * the Teams tab puts them behind a memory router, because the tab's own address is `/teams` and
 * pushing `/tenant` into an iframe's address bar would navigate away from the document the Teams
 * client loaded.
 */
export const routes: RouteObject[] = [
  {
    path: '/',
    element: <App />,
    children: [
      { index: true, element: <Workbench /> },
      // Its own route rather than a panel hung off the Workbench, which stays the hashtag
      // inventory it is for. The server's consent callback redirects here too.
      { path: 'tenant', element: <TenantOverview /> },
    ],
  },
];
