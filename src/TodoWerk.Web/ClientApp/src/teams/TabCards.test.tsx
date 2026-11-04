import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FluentProvider } from '@fluentui/react-components';
import { ConsentRequiredCard } from './TabCards';
import { TeamsHostProvider } from '../host';
import type { TeamsHost } from '../host';
import { cloudwerkLightTheme } from '../theme';

const approvalLink = 'https://todowerk.test/auth/tenant-consent?approval=CfDJ8-minted-here';

/**
 * The consent card is the first thing every person in an unapproved tenant sees in the tab, and in
 * a tenant that keeps the decision for an administrator it is also the last thing the tab can say
 * before Microsoft's own refusal takes over. So what is pinned here is that the route for somebody
 * else — the link, the instruction that goes with it, and how to undo an approval — is on the card
 * from the start rather than after a failure the tab never hears about.
 */
describe('ConsentRequiredCard', () => {
  afterEach(() => {
    Reflect.deleteProperty(window.navigator, 'clipboard');
  });

  it('keeps the approval for yourself as the primary action', () => {
    show();

    expect(screen.getByRole('button', { name: 'Approve and continue' })).toBeTruthy();
  });

  it('offers the link for an administrator, and says to open it as one', () => {
    show();

    expect(document.body.textContent ?? '').toMatch(/can't approve it yourself\?/i);
    expect(screen.getByRole('button', { name: 'Copy link for your administrator' })).toBeTruthy();
    expect(document.body.textContent ?? '').toMatch(
      /open it signed in with an administrator account/i,
    );
    expect(
      screen.getByRole('link', { name: /what approval grants/i }).getAttribute('href'),
    ).toMatch(/\/administrators$/);
  });

  /** Before anybody approves anything, because that is when the question is asked. */
  it('says how to withdraw an approval, pointing at the section that explains it', () => {
    show();

    expect(document.body.textContent ?? '').toMatch(
      /you or your administrator can withdraw this at any time/i,
    );
    expect(
      screen.getByRole('link', { name: /how to withdraw it/i }).getAttribute('href'),
    ).toMatch(/\/administrators#revoking$/);
  });

  it('copies exactly the link the server minted, and says so', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    clipboard({ writeText });

    show();

    fireEvent.click(screen.getByRole('button', { name: 'Copy link for your administrator' }));

    expect(await screen.findByText(/link copied/i)).toBeTruthy();
    expect(writeText).toHaveBeenCalledWith(approvalLink);
    expect(screen.queryByRole('textbox', { name: /link for your administrator/i })).toBeNull();
  });

  /**
   * A frame whose host did not delegate the clipboard is refused it, and the tab cannot know in
   * advance which hosts do. The link must still leave the card.
   */
  it('shows the link to copy by hand when the clipboard says no', async () => {
    clipboard({
      writeText: vi.fn().mockRejectedValue(new DOMException('Denied', 'NotAllowedError')),
    });

    show();

    fireEvent.click(screen.getByRole('button', { name: 'Copy link for your administrator' }));

    const field = (await screen.findByRole('textbox', {
      name: /link for your administrator/i,
    })) as HTMLInputElement;

    expect(field.value).toBe(approvalLink);
    expect(field.readOnly).toBe(true);
    expect(screen.queryByText(/link copied/i)).toBeNull();
  });

  it('shows the link to copy by hand when there is no clipboard at all', async () => {
    show();

    fireEvent.click(screen.getByRole('button', { name: 'Copy link for your administrator' }));

    const field = (await screen.findByRole('textbox', {
      name: /link for your administrator/i,
    })) as HTMLInputElement;

    expect(field.value).toBe(approvalLink);
  });

  /**
   * A refusal the server sent without a link is not one it sends, but the card is not the place
   * to find out. The address it falls back to still takes an administrator to the approval.
   */
  it('still offers a link to the approval when the server sent none', async () => {
    show(null);

    fireEvent.click(screen.getByRole('button', { name: 'Copy link for your administrator' }));

    const field = (await screen.findByRole('textbox', {
      name: /link for your administrator/i,
    })) as HTMLInputElement;

    expect(field.value).toBe(`${window.location.origin}/auth/tenant-consent`);
  });

  function show(link: string | null = approvalLink) {
    const host: TeamsHost = { openInBrowser: () => {}, authenticate: () => Promise.resolve() };

    return render(
      <FluentProvider theme={cloudwerkLightTheme}>
        <TeamsHostProvider host={host} onSessionEnded={() => {}}>
          <ConsentRequiredCard onGranted={() => {}} approvalLink={link} />
        </TeamsHostProvider>
      </FluentProvider>,
    );
  }

  function clipboard(value: Pick<Clipboard, 'writeText'>) {
    Object.defineProperty(window.navigator, 'clipboard', { value, configurable: true });
  }
});
