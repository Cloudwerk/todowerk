import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { FluentProvider } from '@fluentui/react-components';
import { SignInPrompt, signInOutcome } from './SignInPrompt';
import { cloudwerkLightTheme } from '../theme';

/**
 * The card somebody with no session is looking at — including somebody whose sign-in has just
 * ended without an approval, who is shown this card rather than a problem-details document.
 *
 * What is pinned here is that each way of coming back draws a card that says what happened and
 * offers a way on, and that the ordinary invitation is not one of them: a person arriving for the
 * first time must not be told something was refused.
 */
describe('SignInPrompt', () => {
  afterEach(() => window.history.replaceState({}, '', '/'));

  describe('signInOutcome', () => {
    it('reads a refused sign-in, a failed one, and an approval', () => {
      expect(signInOutcome('?signin=not-approved')).toBe('not-approved');
      expect(signInOutcome('?signin=failed')).toBe('failed');
      expect(signInOutcome('?consent=granted')).toBe('approved');
    });

    /** A declined approval leaves somebody exactly where a declined sign-in does. */
    it('draws the same card for an approval nobody granted', () => {
      expect(signInOutcome('?consent=not-granted')).toBe('not-approved');
    });

    it('is the ordinary invitation for anything else', () => {
      expect(signInOutcome('')).toBe('invitation');
      expect(signInOutcome('?signin=something-nobody-sends')).toBe('invitation');
      expect(signInOutcome('?theme=dark')).toBe('invitation');
    });
  });

  it('invites a first-time visitor without mentioning a refusal', () => {
    show('/');

    expect(screen.getByRole('heading', { name: /connect to microsoft to do/i })).toBeTruthy();
    expect(screen.getByRole('link', { name: /sign in with microsoft/i })).toBeTruthy();
    expect(screen.queryByRole('link', { name: /approve for your organisation/i })).toBeNull();
    expect(document.body.textContent ?? '').not.toMatch(/declined|approved/i);
  });

  /**
   * The whole point of the card: the approval that unblocks the tenant is offered here rather than
   * described, and the second route — try the sign-in again — is beside it.
   */
  it('offers the administrator round trip and another attempt when nothing was approved', () => {
    show('/?signin=not-approved');

    expect(screen.getByRole('heading', { name: /has not been approved/i })).toBeTruthy();

    const approve = screen.getByRole('link', { name: /approve for your organisation/i });
    expect(approve.getAttribute('href')).toBe('/auth/tenant-consent');

    expect(screen.getByRole('link', { name: /sign in again/i })).toBeTruthy();
    expect(
      screen.getByRole('link', { name: /what approval grants/i }).getAttribute('href'),
    ).toContain('/administrators');
  });

  /** Neither reading may be asserted, because Microsoft's answer does not distinguish them. */
  it('accuses nobody of declining and blames nobody for being unable to approve', () => {
    show('/?signin=not-approved');

    const text = document.body.textContent ?? '';

    expect(text).toMatch(/declined/i);
    expect(text).toMatch(/administrator/i);
    expect(text).not.toMatch(/you declined/i);
  });

  it('asks for another attempt, and offers no approval, when the round trip merely broke', () => {
    show('/?signin=failed');

    expect(screen.getByRole('heading', { name: /did not finish/i })).toBeTruthy();
    expect(screen.getByRole('link', { name: /sign in again/i })).toBeTruthy();
    expect(screen.queryByRole('link', { name: /approve for your organisation/i })).toBeNull();
  });

  it('says an approval landed, and asks for a sign-in rather than another approval', () => {
    show('/?consent=granted');

    expect(screen.getByRole('heading', { name: /is approved for your organisation/i })).toBeTruthy();
    expect(screen.getByRole('link', { name: /sign in again/i })).toBeTruthy();
    expect(screen.queryByRole('link', { name: /approve for your organisation/i })).toBeNull();
  });

  function show(address: string) {
    window.history.replaceState({}, '', address);

    return render(
      <FluentProvider theme={cloudwerkLightTheme}>
        <SignInPrompt />
      </FluentProvider>,
    );
  }
});
