import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { FluentProvider } from '@fluentui/react-components';
import { LicenceDeniedCard } from './LicenceDeniedCard';
import { licenceCouldNotBeVerifiedCode, licenceEndedCode } from '../api/types';
import { TeamsHostProvider } from '../host';
import type { TeamsHost } from '../host';
import type { DeniedCode } from '../licence';
import { cloudwerkLightTheme } from '../theme';

/**
 * Two problem codes and two cards. Keeping them apart is the whole point of having two: a paying
 * customer meets the second during a portal outage, and a card telling them their access had ended
 * would send them to buy something they already own.
 */
describe('LicenceDeniedCard', () => {
  it('renders the ended card for the ended code', () => {
    show(licenceEndedCode, { detail: 'Your trial of TodoWerk ended on 2 October 2026.' });

    expect(screen.getByText('Your trial of TodoWerk ended on 2 October 2026.')).toBeTruthy();
    expect(screen.queryByText(/keeps trying/i)).toBeNull();
  });

  it('names the operator contact in the browser', () => {
    show(licenceEndedCode, { contact: 'support@cloudwerk.test' });

    expect(screen.getByText(/support@cloudwerk\.test/)).toBeTruthy();
  });

  it('names nothing and links nowhere inside the Teams tab', () => {
    show(licenceEndedCode, {
      contact: 'support@cloudwerk.test',
      purchaseUrl: 'https://portal.test/Solutions',
      inTeams: true,
    });

    expect(screen.queryByText(/support@cloudwerk\.test/)).toBeNull();
    expect(screen.queryByRole('link')).toBeNull();
  });

  /**
   * The reason ManagementPortal delivers the address on a refusal at all: this card's reader is
   * the person who has just been told their access ended.
   */
  it('offers somewhere to buy on the ended card in the browser', () => {
    show(licenceEndedCode, { purchaseUrl: 'https://portal.test/Solutions' });

    const link = screen.getByRole('link', { name: /buy todowerk/i });

    expect(link.getAttribute('href')).toBe('https://portal.test/Solutions');
  });

  it('carries no link on the ended card when the server delivered no address', () => {
    show(licenceEndedCode, { contact: 'support@cloudwerk.test' });

    expect(screen.queryByRole('link')).toBeNull();
  });

  /**
   * The card met by people who have *paid*, during a portal outage. The server sends it no
   * address, and it would refuse to render one anyway: sending a paying customer to buy what they
   * already own is the mistake the two cards exist to keep apart.
   */
  it('never offers a way to buy on the card for a portal it could not reach', () => {
    show(licenceCouldNotBeVerifiedCode, { purchaseUrl: 'https://portal.test/Solutions' });

    expect(screen.queryByRole('link')).toBeNull();
  });

  /**
   * No purchase link inside the Teams tab, on any device, and none of the words around one either:
   * free, trial, a price.
   */
  it('carries no purchase wording inside the tab', () => {
    show(licenceEndedCode, {
      contact: 'support@cloudwerk.test',
      purchaseUrl: 'https://portal.test/Solutions',
      inTeams: true,
    });

    const text = document.body.textContent ?? '';

    expect(text).not.toMatch(/\bfree\b/i);
    expect(text).not.toMatch(/\btrial\b/i);
    expect(text).not.toMatch(/[€$£]\s?\d/);
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('renders the second card for the code that clears itself, and never the first', () => {
    show(licenceCouldNotBeVerifiedCode, {
      detail: "TodoWerk could not confirm your organisation's licence. It keeps trying.",
    });

    expect(screen.getByRole('heading', { name: /could not confirm your licence/i })).toBeTruthy();
    expect(
      screen.getByText("TodoWerk could not confirm your organisation's licence. It keeps trying."),
    ).toBeTruthy();
    expect(screen.getByText(/nothing needs doing/i)).toBeTruthy();

    // The word this card must never be mistaken for.
    expect(document.body.textContent ?? '').not.toMatch(/has ended/i);
  });

  /** With no sentence from the server, the body must still say something the heading did not. */
  it('never repeats its heading as its body when the server sent no detail', () => {
    show(licenceEndedCode, {});

    expect(screen.getAllByText(/has ended/i)).toHaveLength(1);
    expect(screen.getByText(/your licence has run out/i)).toBeTruthy();
  });

  it('offers neither card a way to sign in again, which would loop', () => {
    show(licenceEndedCode, {});

    expect(screen.queryByRole('button', { name: /sign in/i })).toBeNull();
    expect(screen.queryByRole('link', { name: /sign in/i })).toBeNull();
  });

  function show(
    code: DeniedCode,
    {
      detail = null,
      contact = null,
      purchaseUrl = null,
      inTeams = false,
    }: {
      detail?: string | null;
      contact?: string | null;
      purchaseUrl?: string | null;
      inTeams?: boolean;
    },
  ) {
    const card = (
      <FluentProvider theme={cloudwerkLightTheme}>
        <LicenceDeniedCard
          code={code}
          detail={detail}
          contact={contact}
          purchaseUrl={purchaseUrl}
        />
      </FluentProvider>
    );

    return render(
      inTeams ? (
        <TeamsHostProvider host={noopHost} onSessionEnded={() => {}}>
          {card}
        </TeamsHostProvider>
      ) : (
        card
      ),
    );
  }
});

const noopHost: TeamsHost = {
  openInBrowser: () => {},
  authenticate: () => Promise.resolve(),
};
