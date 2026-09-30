import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { FluentProvider } from '@fluentui/react-components';
import { IndexFreshness } from './IndexFreshness';
import { cloudwerkLightTheme } from '../theme';
import type { IndexStatus, TaskListStatus } from '../api/types';

/**
 * The freshness chrome sits above the inventory table, which CONTEXT.md calls the product. What
 * this suite holds is that the chrome stays one line when there is nothing to say per list, and
 * unfolds on its own exactly when there is: a scan in progress, or a list that failed.
 */
describe('IndexFreshness', () => {
  it('keeps the per-list breakdown folded while the index is idle and every list is fine', () => {
    show(status());

    expect(screen.queryByText(/Arbeit/)).toBeNull();
    expect(disclosure().getAttribute('aria-expanded')).toBe('false');
  });

  it('unfolds the breakdown on request, and folds it again', () => {
    show(status());

    fireEvent.click(disclosure());

    expect(screen.getByText(/Arbeit/)).toBeTruthy();
    expect(disclosure().getAttribute('aria-expanded')).toBe('true');

    fireEvent.click(disclosure());

    expect(screen.queryByText(/Arbeit/)).toBeNull();
  });

  it('unfolds by itself while a scan is running', () => {
    show(status({ activity: 'Scanning', listsIndexed: 1 }));

    expect(screen.getByText(/Arbeit/)).toBeTruthy();
  });

  it('unfolds by itself when a list did not finish', () => {
    show(status({ lists: [list('Arbeit'), list('Privat', { state: 'Failed', failureReason: 'Graph said no.' })] }));

    expect(screen.getByText(/Privat — failed/)).toBeTruthy();
  });

  /** A fold made while scanning must not hide the list that fails after the scan. */
  it('unfolds again when the reason to changes, whatever was chosen for the last one', () => {
    const view = show(status({ activity: 'Scanning', listsIndexed: 1 }));

    fireEvent.click(disclosure());
    expect(screen.queryByText(/Arbeit/)).toBeNull();

    view.rerender(
      wrap(status({ lists: [list('Arbeit'), list('Privat', { state: 'Failed', failureReason: 'Graph said no.' })] })),
    );

    expect(screen.getByText(/Privat — failed/)).toBeTruthy();
  });

  it('counts the lists on the disclosure', () => {
    show(status({ listCount: 1, listsIndexed: 1, lists: [list('Arbeit')] }));

    expect(screen.getByRole('button', { name: /^1 list$/i })).toBeTruthy();
  });

  /** The sentence already says a scan is running; the button need not say it a second time. */
  it('keeps the re-scan button label constant while scanning, and disables it', () => {
    show(status({ activity: 'Scanning' }));

    const button = screen.getByRole('button', { name: /check for changes/i }) as HTMLButtonElement;

    expect(button.disabled).toBe(true);
    expect(screen.queryByRole('button', { name: /scanning/i })).toBeNull();
  });

  /**
   * The absolute time behind "4 minutes ago" is a tooltip reachable by keyboard, not a `title`
   * attribute — the same reason the flag badges give for theirs.
   */
  it('offers the absolute time as a focusable description rather than a title attribute', () => {
    show(status());

    const time = document.querySelector('time') as HTMLTimeElement;

    expect(time.getAttribute('title')).toBeNull();
    expect(time.tabIndex).toBe(0);
  });

  function disclosure(): HTMLButtonElement {
    return screen.getByRole('button', { name: /2 lists/i }) as HTMLButtonElement;
  }

  function show(value: IndexStatus) {
    return render(wrap(value));
  }

  function wrap(value: IndexStatus) {
    return (
      <FluentProvider theme={cloudwerkLightTheme}>
        <IndexFreshness status={value} onRescan={() => {}} rescanning={false} />
      </FluentProvider>
    );
  }
});

function status(overrides: Partial<IndexStatus> = {}): IndexStatus {
  return {
    activity: 'Idle',
    currentAsOf: '2026-09-03T11:00:00Z',
    hasCompletedFirstScan: true,
    listCount: 2,
    listsIndexed: 2,
    tasksIndexed: 40,
    lists: [list('Arbeit'), list('Privat')],
    lastScanFailure: null,
    lastScanFailureCode: 'None',
    ...overrides,
  };
}

function list(displayName: string, overrides: Partial<TaskListStatus> = {}): TaskListStatus {
  return {
    taskListId: `list-${displayName}`,
    displayName,
    state: 'Indexed',
    tasksIndexed: 20,
    lastSuccessfulSyncAt: '2026-09-03T11:00:00Z',
    lastCompletedScanAt: '2026-09-03T11:00:00Z',
    failureReason: null,
    failureCode: 'None',
    ...overrides,
  };
}
