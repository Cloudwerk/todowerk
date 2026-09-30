import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FluentProvider } from '@fluentui/react-components';
import { MemoryRouter } from 'react-router';
import { TenantOverview } from './TenantOverview';
import { LicenceStateProvider } from '../licence';
import type { LicenceState } from '../licence';
import type { Licence, LicenceKind, TenantOverview as TenantOverviewDto } from '../api/types';
import { cloudwerkLightTheme } from '../theme';

/**
 * What the Licence changes on this screen: a panel naming it, and an invitation that follows it. The rule the invitation enforces is the one ADR-0012 is most insistent about —
 * paying for one seat is not standing to approve TodoWerk for an organisation.
 */
describe('TenantOverview', () => {
  afterEach(() => vi.unstubAllGlobals());

  it.each<[LicenceKind, string]>([
    ['Tenant', 'Tenant licence'],
    ['Personal', 'Personal licence'],
    ['Trial', 'Trial'],
  ])('names the %s kind and its end date', async (kind, label) => {
    show({ kind, endsAt: '2027-03-14T00:00:00Z' });

    const panel = await screen.findByText(new RegExp(`${label}, until`));

    expect(panel.textContent).toMatch(/2027/);
  });

  it('has no licence panel on a Self-Host', async () => {
    show({ kind: null });

    await screen.findByRole('heading', { name: /your organisation/i });

    expect(screen.queryByRole('heading', { name: /your licence/i })).toBeNull();
  });

  /**
   * Seat counts are deliberately absent. A count of distinct people is exactly what the statistics
   * floor protects, and a page that respected the floor for one figure and not another would be
   * wrong in one direction or the other.
   */
  it('shows no seat figure anywhere', async () => {
    show({ kind: 'Tenant' });

    await screen.findByRole('heading', { name: /your licence/i });

    expect(document.body.textContent ?? '').not.toMatch(/seat/i);
  });

  it.each<[LicenceKind, boolean]>([
    ['Tenant', true],
    ['Trial', true],
    ['Personal', false],
  ])('offers the consent invitation under %s: %s', async (kind, offered) => {
    show({ kind, mayOfferTenantConsent: kind !== 'Personal' });

    await screen.findByRole('heading', { name: /your licence/i });

    const invitation = screen.queryByRole('heading', { name: /approve TodoWerk for everyone/i });

    expect(invitation !== null).toBe(offered);
  });

  /**
   * Hiding the invitation hides the "approve again" route with it — that link is the same route
   * under another name, and leaving it would make the rule cosmetic.
   */
  it('hides the approve-again route too under a Personal Licence', async () => {
    show({ kind: 'Personal', mayOfferTenantConsent: false }, { tenantConsentGrantedThroughTodoWerk: true });

    await screen.findByRole('heading', { name: /your licence/i });

    expect(screen.queryByRole('link', { name: /approve again/i })).toBeNull();
    expect(screen.queryByRole('heading', { name: /approved for the organisation/i })).toBeNull();
  });

  /**
   * The window between the screen opening and the Licence arriving. A permissive default here would
   * put the invitation in front of a Personal Licence holder — the one thing ADR-0012 says must
   * never be offered — for exactly as long as the read takes, which on a cold connection is long
   * enough to click.
   */
  it('offers nothing while the Licence is still being read', async () => {
    render(wrap({ status: 'loading' }, overview()));

    await screen.findByRole('heading', { name: /your organisation/i });

    expect(screen.queryByRole('heading', { name: /approve TodoWerk for everyone/i })).toBeNull();
  });

  /**
   * A Licence that could not be read is a failure of TodoWerk's own bookkeeping, not news about
   * anybody's standing — so the invitation is offered as though there were no Licence to consult.
   */
  it('leaves the invitation alone when the Licence could not be read', async () => {
    render(wrap({ status: 'unknown' }, overview()));

    expect(await screen.findByRole('heading', { name: /approve TodoWerk for everyone/i })).toBeTruthy();
  });

  function show(
    licence: Partial<Licence> & { kind: LicenceKind | null },
    tenant: Partial<TenantOverviewDto> = {},
  ) {
    return render(
      wrap(
        {
          status: 'licensed',
          licence: {
            endsAt: '2027-03-14T00:00:00Z',
            banner: 'None',
            mayOfferTenantConsent: true,
            purchaseUrl: null,
            ...licence,
          },
        },
        overview(tenant),
      ),
    );
  }
});

function overview(tenant: Partial<TenantOverviewDto> = {}): TenantOverviewDto {
  return {
    // Below the floor, so the server sends none and the screen draws nothing in their place. This
    // suite is about the two panels above them.
    statistics: null,
    tenantConsentGrantedThroughTodoWerk: false,
    ...tenant,
  };
}

function wrap(licence: LicenceState, tenant: TenantOverviewDto) {
  // The screen reads the overview over the network and the Licence from context. Only the first is
  // stubbed: the second is the thing under test, and a stubbed fetch for it would be testing the
  // stub.
  vi.stubGlobal(
    'fetch',
    vi.fn(() =>
      Promise.resolve(
        new Response(JSON.stringify(tenant), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ),
    ),
  );

  return (
    <FluentProvider theme={cloudwerkLightTheme}>
      <MemoryRouter>
        <LicenceStateProvider state={licence}>
          <TenantOverview />
        </LicenceStateProvider>
      </MemoryRouter>
    </FluentProvider>
  );
}
