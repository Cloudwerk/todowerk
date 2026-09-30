import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { TeamsHostProvider, useEntraRoundTrip } from './host';
import type { TeamsHost } from './host';

/**
 * What the tab says when a round trip through Microsoft comes back with nothing.
 *
 * Two endings that look identical from the tab and are not: somebody shut the window, and the
 * server told the popup that nothing was approved. The second is reported as a failure of its own
 * rather than as success, so the tab says what to do next instead of redrawing the same invitation
 * with no explanation.
 */
describe('useEntraRoundTrip', () => {
  it('says what to do next when the popup reports that nothing was approved', async () => {
    show(() => Promise.reject(new Error('not-approved')));

    fireEvent.click(screen.getByText('go'));

    await waitFor(() => expect(screen.getByTestId('failure').textContent).toMatch(/nothing was approved/i));
    expect(screen.getByTestId('failure').textContent).toMatch(/administrator/i);
  });

  it('keeps the sentence it had for a window somebody simply closed', async () => {
    show(() => Promise.reject(new Error('CancelledByUser')));

    fireEvent.click(screen.getByText('go'));

    await waitFor(() => expect(screen.getByTestId('failure').textContent).toMatch(/closed before it finished/i));
  });

  it('says nothing at all when the round trip completed', async () => {
    show(() => Promise.resolve());

    fireEvent.click(screen.getByText('go'));

    await waitFor(() => expect(screen.getByTestId('failure').textContent).toBe(''));
  });

  function show(authenticate: (url: string) => Promise<void>) {
    const host: TeamsHost = { openInBrowser: () => {}, authenticate };

    return render(
      <TeamsHostProvider host={host} onSessionEnded={() => {}}>
        <Probe />
      </TeamsHostProvider>,
    );
  }
});

/** A control of the shape every caller uses: an anchor whose navigation the tab takes over. */
function Probe() {
  const trip = useEntraRoundTrip();

  return (
    <>
      <a href="/auth/tenant-consent" onClick={trip.intercept}>
        go
      </a>
      <span data-testid="failure">{trip.failure ?? ''}</span>
    </>
  );
}
