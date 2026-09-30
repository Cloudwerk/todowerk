import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FluentProvider } from '@fluentui/react-components';
import { EraseMeDialog } from './EraseMeDialog';
import { TeamsHostProvider } from '../host';
import type { TeamsHost } from '../host';
import { cloudwerkLightTheme } from '../theme';

/**
 * The tab's erasure is a request already on its way the moment "Delete everything" is pressed.
 * A dialog that could still be closed after that would let somebody believe they had backed out
 * of a destruction that then arrives anyway.
 */
describe('EraseMeDialog', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Both hosts, because they are two blocks of JSX rather than one: the browser confirms with a
  // real form and the tab with a fetch, and only the button inside them is shared. A reordering in
  // either would go unseen if only the other were asked.
  it.each<[string, boolean]>([
    ['the browser', false],
    ['the tab', true],
  ])('offers the way to keep the data first in %s, as the primary choice', (_host, inTeams) => {
    show({ inTeams });

    const buttons = screen.getAllByRole('button', { name: /keep my data|delete everything/i });

    expect(buttons.map((button) => button.textContent)).toEqual(['Keep my data', 'Delete everything']);
  });

  it('closes no way but through once the destruction has been asked for', () => {
    // A request that never answers, so the dialog stays in the moment after the click.
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})));

    const onOpenChange = vi.fn();

    show({ onOpenChange });

    fireEvent.click(screen.getByRole('button', { name: /delete everything/i }));

    const keep = screen.getByRole('button', { name: /keep my data/i }) as HTMLButtonElement;

    expect(keep.disabled).toBe(true);
    expect((screen.getByRole('button', { name: /deleting/i }) as HTMLButtonElement).disabled).toBe(true);

    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' });

    expect(onOpenChange).not.toHaveBeenCalled();
  });

  function show({
    onOpenChange = () => {},
    inTeams = true,
  }: { onOpenChange?: (open: boolean) => void; inTeams?: boolean } = {}) {
    const dialog = (
      <FluentProvider theme={cloudwerkLightTheme}>
        <EraseMeDialog open onOpenChange={onOpenChange} />
      </FluentProvider>
    );

    render(
      inTeams ? (
        <TeamsHostProvider host={pendingHost} onSessionEnded={() => {}}>
          {dialog}
        </TeamsHostProvider>
      ) : (
        dialog
      ),
    );
  }
});

/** A host whose popup never comes back either. */
const pendingHost: TeamsHost = {
  openInBrowser: () => {},
  authenticate: () => new Promise(() => {}),
};
