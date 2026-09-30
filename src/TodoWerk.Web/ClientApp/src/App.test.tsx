import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FluentProvider } from '@fluentui/react-components';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { App } from './App';
import { TeamsHostProvider } from './host';
import type { TeamsHost } from './host';
import { cloudwerkLightTheme } from './theme';

/**
 * The shell around every screen: what the header calls the screens, and where the session
 * controls live. Erasure is an obligation and has to stay reachable in both hosts; it does not
 * have to sit at the same weight as sign-out on every screen, and here it does not.
 */
describe('App', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('names the screens by what they show', async () => {
    show();

    expect(await screen.findByRole('link', { name: /^hashtags$/i })).toBeTruthy();
    expect(screen.getByRole('link', { name: /your organisation/i })).toBeTruthy();
    expect(screen.queryByRole('link', { name: /workbench/i })).toBeNull();
  });

  it('keeps sign-out and erasure behind the person’s name in the browser', async () => {
    show();

    const name = await screen.findByRole('button', { name: /ada lovelace/i });

    expect(screen.queryByRole('menuitem')).toBeNull();
    expect(screen.queryByRole('button', { name: /delete my data/i })).toBeNull();

    fireEvent.click(name);

    expect(await screen.findByRole('menuitem', { name: /sign out/i })).toBeTruthy();
    expect(screen.getByRole('menuitem', { name: /delete my data/i })).toBeTruthy();
    expect(screen.queryByRole('menuitem', { name: /open in browser/i })).toBeNull();
  });

  /** Inside the tab the identity is the Teams identity, so the way out of the frame takes sign-out's place. */
  it('offers the way out of the frame instead of sign-out inside the tab', async () => {
    show({ inTeams: true });

    fireEvent.click(await screen.findByRole('button', { name: /ada lovelace/i }));

    expect(await screen.findByRole('menuitem', { name: /open in browser/i })).toBeTruthy();
    expect(screen.getByRole('menuitem', { name: /delete my data/i })).toBeTruthy();
    expect(screen.queryByRole('menuitem', { name: /sign out/i })).toBeNull();
  });

  /**
   * The route to erasure, which is the shell's share of it: the menu item interposes a
   * confirmation rather than destroying anything, and this is the only place the confirmation's
   * wording is asserted. Which of its two buttons comes first is asked of the dialog directly, in
   * `EraseMeDialog.test.tsx`, in both hosts.
   *
   * By text rather than by role, because tabster marks the modal `aria-hidden` in jsdom. A Fluent
   * modal hands itself to tabster, which decides what is exposed to assistive technology from where
   * the focus is — and in jsdom nothing is ever focusable, because tabster measures that with
   * `getBoundingClientRect` and jsdom lays nothing out. So the dialog opened from a menu never
   * becomes the active modal here, and a quarter of a second later tabster marks it `aria-hidden`
   * and leaves it that way; a role query races that timer. Text queries do not consult the
   * accessibility tree and do not race.
   */
  it('confirms erasure before anything happens', async () => {
    show();

    fireEvent.click(await screen.findByRole('button', { name: /ada lovelace/i }));
    fireEvent.click(await screen.findByRole('menuitem', { name: /delete my data/i }));

    expect(await screen.findByText(/delete everything todowerk holds about you/i)).toBeTruthy();
  });

  /**
   * The Handbook and the legal documents, in one place. The App Package names the About Page as the
   * installation's support link, so a Teams administrator arrives on it, and the product offers it
   * too.
   */
  it('gathers the Handbook and the legal documents behind one help control', async () => {
    show({ guideUrl: 'https://todowerk.example/guide' });

    const help = await screen.findByRole('button', { name: /help and about/i });

    expect(screen.queryByRole('menuitem')).toBeNull();

    fireEvent.click(help);

    expect(await screen.findByRole('menuitem', { name: /^guide$/i })).toBeTruthy();
    expect(screen.getByRole('menuitem', { name: /about todowerk/i })).toBeTruthy();
    expect(screen.getByRole('menuitem', { name: /for administrators/i })).toBeTruthy();
    expect(screen.getByRole('menuitem', { name: /terms of use/i })).toBeTruthy();
    expect(screen.getByRole('menuitem', { name: /privacy notice/i })).toBeTruthy();
  });

  /** Reading is not a third screen: the nav names the two screens and nothing else. */
  it('keeps the Guide out of the screen nav', async () => {
    show({ guideUrl: 'https://todowerk.example/guide' });

    await screen.findByRole('button', { name: /help and about/i });

    expect(screen.queryByRole('link', { name: /guide/i })).toBeNull();
  });

  /**
   * A Self-Host has no Guide — it is served on the Hosted Service's host by something other than
   * this application — and the menu draws no entry rather than a dead one (ADR-0013). The pages
   * this application serves itself are there either way.
   */
  it('omits the Guide where the deployment has none', async () => {
    show();

    fireEvent.click(await screen.findByRole('button', { name: /help and about/i }));

    expect(await screen.findByRole('menuitem', { name: /about todowerk/i })).toBeTruthy();
    expect(screen.queryByRole('menuitem', { name: /^guide$/i })).toBeNull();
  });

  /**
   * The menu is the server's list, rendered — set, order and wording. The component holds no paths
   * and no order of its own beside `DocumentNav.For`. Asserting the whole sequence rather than the
   * membership is the point — membership is what the test above already covers, and order is what
   * could drift without anything failing.
   */
  it('offers the documents in the order the server gave them', async () => {
    show({ guideUrl: 'https://todowerk.example/guide' });

    fireEvent.click(await screen.findByRole('button', { name: /help and about/i }));
    await screen.findByRole('menuitem', { name: /^guide$/i });

    expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
      'About TodoWerk',
      'For administrators',
      'Guide',
      'Terms of use',
      'Privacy notice',
    ]);
  });

  /**
   * ADR-0013 insists the About Page and the page for administrators are anonymous because they are
   * read by people who have not signed in and may never, so TodoWerk's own front door offers them
   * to that reader too. The screens and the session controls stay behind the sign-in, because they
   * are the part that belongs to somebody.
   */
  it('offers the documents to somebody who has not signed in', async () => {
    show({ signedIn: false });

    fireEvent.click(await screen.findByRole('button', { name: /help and about/i }));

    expect(await screen.findByRole('menuitem', { name: /for administrators/i })).toBeTruthy();
    expect(screen.getByRole('menuitem', { name: /about todowerk/i })).toBeTruthy();
    expect(screen.queryByRole('link', { name: /^hashtags$/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /ada lovelace/i })).toBeNull();
  });

  /**
   * Inside the tab a document has to leave the frame through the host, not through a link: a link
   * clicked in a Teams tab opens inside Teams, and these pages refuse to be framed (ADR-0010).
   */
  it('opens a document in the person’s own browser from inside the tab', async () => {
    const opened: string[] = [];

    show({ inTeams: true, host: { ...noopHost, openInBrowser: (url) => opened.push(url) } });

    fireEvent.click(await screen.findByRole('button', { name: /help and about/i }));
    fireEvent.click(await screen.findByRole('menuitem', { name: /for administrators/i }));

    expect(opened.length).toBe(1);
    expect(new URL(opened[0]).pathname).toBe('/administrators');
  });

  function show({
    inTeams = false,
    guideUrl = null,
    signedIn = true,
    host = noopHost,
  }: {
    inTeams?: boolean;
    guideUrl?: string | null;
    signedIn?: boolean;
    host?: TeamsHost;
  } = {}) {
    vi.stubGlobal('fetch', vi.fn((path: string) => answer(path, guideUrl, signedIn)));

    const router = createMemoryRouter([
      {
        path: '/',
        element: <App />,
        children: [
          { index: true, element: <div>the screen</div> },
          { path: 'tenant', element: <div>the other screen</div> },
        ],
      },
    ]);

    const tree = (
      <FluentProvider theme={cloudwerkLightTheme}>
        <RouterProvider router={router} />
      </FluentProvider>
    );

    render(
      inTeams ? (
        <TeamsHostProvider host={host} onSessionEnded={() => {}}>
          {tree}
        </TeamsHostProvider>
      ) : (
        tree
      ),
    );
  }
});

function answer(
  path: string,
  guideUrl: string | null = null,
  signedIn = true,
): Promise<Response> {
  if (path.startsWith('/api/me')) {
    return signedIn
      ? json({ displayName: 'Ada Lovelace', username: 'ada@example.test' })
      : Promise.resolve(new Response(null, { status: 401 }));
  }

  // Anonymous on the server, so it answers the same whether or not there is a session — which is
  // what lets the shell offer the documents to somebody who has not signed in.
  if (path.startsWith('/api/handbook')) {
    return json({ documents: documentNav(guideUrl) });
  }

  if (path.startsWith('/api/licence')) {
    return signedIn
      ? json({ kind: 'Tenant', endsAt: null, banner: 'None', mayOfferTenantConsent: true, purchaseUrl: null })
      : Promise.resolve(new Response(null, { status: 401 }));
  }

  return Promise.resolve(new Response(null, { status: 404 }));
}

/**
 * What `DocumentNav.For` builds, which is what `GET /api/handbook` answers with. Written out here
 * rather than reduced to a guide flag, because the client holds no set, no order and no wording
 * of its own: a fixture that generated the list from the component would be testing the component
 * against itself.
 */
function documentNav(guideUrl: string | null) {
  return [
    { text: 'About TodoWerk', href: '/about', group: 'Handbook' },
    { text: 'For administrators', href: '/administrators', group: 'Handbook' },
    ...(guideUrl === null ? [] : [{ text: 'Guide', href: guideUrl, group: 'Handbook' }]),
    { text: 'Terms of use', href: '/legal/terms', group: 'Legal' },
    { text: 'Privacy notice', href: '/legal/privacy', group: 'Legal' },
  ];
}

function json(body: unknown): Promise<Response> {
  return Promise.resolve(
    new Response(JSON.stringify(body), { status: 200, headers: { 'content-type': 'application/json' } }),
  );
}

const noopHost: TeamsHost = {
  openInBrowser: () => {},
  authenticate: () => Promise.resolve(),
};
