import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { FluentProvider } from '@fluentui/react-components';
import { LicenceBanner } from './LicenceBanner';
import { LicenceStateProvider } from '../licence';
import type { LicenceState } from '../licence';
import { TeamsHostProvider } from '../host';
import type { TeamsHost } from '../host';
import { cloudwerkLightTheme } from '../theme';
import type { Licence, LicenceBannerState, LicenceKind } from '../api/types';

/**
 * What the Workbench says to one person about their own Trial, and the silence that is the common
 * case. The two things worth pinning hardest are the browser/tab difference — inside the Teams tab
 * the banner shows the sentence and no purchase link, on any device — and that dismissing the
 * running state does not silence the warning that follows it.
 */
describe('LicenceBanner', () => {
  beforeEach(() => window.localStorage.clear());
  afterEach(() => window.localStorage.clear());

  it('says nothing under a Tenant or Personal Licence', () => {
    show({ kind: 'Tenant', banner: 'None' });

    expect(screen.queryByText(/trying TodoWerk/i)).toBeNull();
  });

  it('says nothing on a Self-Host, where there is no Licence at all', () => {
    show({ kind: null, banner: 'None' });

    expect(screen.queryByText(/trying TodoWerk/i)).toBeNull();
  });

  it('says nothing while the Licence could not be read', () => {
    render(wrap(<LicenceBanner />, { status: 'unknown' }));

    expect(screen.queryByText(/trying TodoWerk/i)).toBeNull();
  });

  it('names the end date while a Trial is running, and never a day count', () => {
    show({ kind: 'Trial', banner: 'TrialRunning', endsAt: '2026-10-02T09:14:07Z' });

    const text = screen.getByText(/trying TodoWerk until/i).textContent ?? '';

    expect(text).toMatch(/2026/);
    expect(text).not.toMatch(/\bdays?\b/i);
  });

  it('keeps the sentence when a Trial has no end date to name', () => {
    show({ kind: 'Trial', banner: 'TrialRunning', endsAt: null });

    expect(screen.getByText('You are trying TodoWerk.')).toBeTruthy();
  });

  it('carries both purchase links in the browser when a URL was delivered', () => {
    show({ kind: 'Trial', banner: 'TrialRunning', purchaseUrl: 'https://portal.test/buy' });

    expect(screen.getByRole('link', { name: /buy for yourself/i })).toBeTruthy();
    expect(screen.getByRole('link', { name: /buy for your organisation/i })).toBeTruthy();
  });

  it('carries no link when the endpoint delivered no purchase URL', () => {
    show({ kind: 'Trial', banner: 'TrialRunning', purchaseUrl: null });

    expect(screen.queryByRole('link')).toBeNull();
  });

  /**
   * The tab-versus-browser rule, on every device and with no device check anywhere. It is the same
   * distinction the denied cards draw and the one the client already had.
   */
  it('carries no link inside the Teams tab, whatever the endpoint delivered', () => {
    show({ kind: 'Trial', banner: 'TrialRunning', purchaseUrl: 'https://portal.test/buy' }, { inTeams: true });

    expect(screen.getByText(/trying TodoWerk until/i)).toBeTruthy();
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('hides the running banner once it has been dismissed in this browser', () => {
    const licence = { kind: 'Trial' as const, banner: 'TrialRunning' as const };

    const view = show(licence);
    fireEvent.click(screen.getByRole('button', { name: /dismiss/i }));

    expect(screen.queryByText(/trying TodoWerk/i)).toBeNull();

    // And it stays dismissed across a reload, because the dismissal is in this browser's storage
    // and never on the server.
    view.unmount();
    show(licence);

    expect(screen.queryByText(/trying TodoWerk/i)).toBeNull();
  });

  /**
   * The one behaviour a naive "dismissed once, gone forever" would get wrong: an earlier dismissal
   * of the running banner must not silence the warning, which is different news.
   */
  it('brings the warning back once after the running banner was dismissed', () => {
    const view = show({ kind: 'Trial', banner: 'TrialRunning' });
    fireEvent.click(screen.getByRole('button', { name: /dismiss/i }));
    view.unmount();

    show({ kind: 'Trial', banner: 'TrialEndingSoon' });

    expect(screen.getByText(/trying TodoWerk until/i)).toBeTruthy();

    // And can then be dismissed on its own.
    fireEvent.click(screen.getByRole('button', { name: /dismiss/i }));

    expect(screen.queryByText(/trying TodoWerk/i)).toBeNull();
  });

  function show(
    licence: Partial<Licence> & { kind: LicenceKind | null; banner: LicenceBannerState },
    options: { inTeams?: boolean } = {},
  ) {
    return render(
      wrap(
        <LicenceBanner />,
        {
          status: 'licensed',
          licence: {
            endsAt: '2026-10-02T09:14:07Z',
            mayOfferTenantConsent: true,
            purchaseUrl: null,
            ...licence,
          },
        },
        options.inTeams,
      ),
    );
  }
});

function wrap(children: React.ReactNode, state: LicenceState, inTeams = false) {
  const inner = (
    <FluentProvider theme={cloudwerkLightTheme}>
      <LicenceStateProvider state={state}>{children}</LicenceStateProvider>
    </FluentProvider>
  );

  // The host is what every surface asks where it is. There is deliberately no `isInTeams` flag and
  // no user-agent check to stand in for it.
  return inTeams ? (
    <TeamsHostProvider host={noopHost} onSessionEnded={() => {}}>
      {inner}
    </TeamsHostProvider>
  ) : (
    inner
  );
}

const noopHost: TeamsHost = {
  openInBrowser: () => {},
  authenticate: () => Promise.resolve(),
};
