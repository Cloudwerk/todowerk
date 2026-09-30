import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FluentProvider } from '@fluentui/react-components';
import { Workbench } from './Workbench';
import { cloudwerkLightTheme } from '../theme';
import type {
  Change,
  ChangeQueue,
  HashtagInventoryPage,
  HashtagInventoryRow,
  IndexStatus,
  MarkerRule,
} from '../api/types';

/**
 * The screen as a whole, against a stubbed server: the table arrives, and a changed filter says
 * something is happening for as long as the rows on screen answer the previous one. Not a test
 * of any one component — those have their own — but of the way the page reads its two queries.
 */
describe('Workbench', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('shows the inventory once the index status and the rows have both landed', async () => {
    show();

    expect(await screen.findByText('#Prio1')).toBeTruthy();
    expect(screen.getByText('2 hashtags')).toBeTruthy();
    expect(screen.queryByRole('progressbar')).toBeNull();
  });

  it('says something is happening while the rows on screen answer an earlier filter', async () => {
    const server = show();

    await screen.findByText('#Prio1');

    // The next inventory read is held open until the test lets it go.
    server.holdInventory();

    fireEvent.click(screen.getByRole('combobox', { name: /show/i }));
    fireEvent.click(await screen.findByRole('option', { name: /not edited in a long time/i }));

    expect(await screen.findByRole('progressbar', { name: /loading the hashtags/i })).toBeTruthy();
    // The old rows stay readable, and the old count is not passed off as the new one.
    expect(screen.getByText('#Prio1')).toBeTruthy();
    expect(screen.queryByText('2 hashtags')).toBeNull();

    server.releaseInventory(inventory([row({ key: 'OLD', canonicalSpelling: 'old', isStale: true })]));

    expect(await screen.findByText('#old')).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole('progressbar')).toBeNull());
    expect(screen.getByText('1 hashtag')).toBeTruthy();
  });

  /**
   * An empty answer to the previous filter is not a verdict on the next one: "No hashtags found"
   * over a filter that has not answered yet would be a false, alarming statement.
   */
  it('shows the shape of a table rather than a verdict while an empty result is being replaced', async () => {
    const server = show({ inventory: inventory([]) });

    await screen.findByText(/no hashtags found/i);

    server.holdInventory();

    fireEvent.click(screen.getByRole('combobox', { name: /show/i }));
    fireEvent.click(await screen.findByRole('option', { name: /not edited in a long time/i }));

    expect(await screen.findByRole('progressbar', { name: /loading the hashtags/i })).toBeTruthy();
    expect(screen.queryByText(/no hashtags found/i)).toBeNull();
    expect(screen.queryByText(/nothing matches/i)).toBeNull();

    server.releaseInventory(inventory([row()]));

    expect(await screen.findByText('#Prio1')).toBeTruthy();
  });

  /**
   * An account with no hashtags at all is most likely someone who has never written one. The
   * empty state says how, and what for, and its button asks for the scan that would find the
   * first one — the same request the freshness bar makes, not a third way to scan.
   */
  it('teaches the hashtag habit when the index is empty and offers the next scan', async () => {
    show({ inventory: inventory([]) });

    await screen.findByText(/no hashtags found/i);
    expect(screen.getByText('Write one')).toBeTruthy();
    expect(screen.getByText(/a task lives in exactly one to do list/i)).toBeTruthy();
    expect(screen.getByRole('figure', { name: /three example tasks/i })).toBeTruthy();

    // Two of them now: the freshness bar's, and the empty state's. Either asks for the same scan.
    const buttons = screen.getAllByRole('button', { name: /check for changes/i });
    expect(buttons).toHaveLength(2);

    fireEvent.click(buttons[1]);

    await waitFor(() => {
      const scan = (fetch as ReturnType<typeof vi.fn>).mock.calls.find(([path]) =>
        String(path).startsWith('/api/index/scan'),
      );
      expect(scan).toBeTruthy();
      expect((scan?.[1] as RequestInit | undefined)?.method).toBe('POST');
    });
  });

  /**
   * A marker is written from the row it is about, and the column it was written from is
   * where it shows up. Nothing is written to a task: a rule declares, and applying it is a Change
   * (ADR-0014).
   */
  it('sets a marker from an inventory row and shows it in the column', async () => {
    show();

    fireEvent.click(await screen.findByRole('button', { name: /^marker for #prio1$/i }));

    const box = await screen.findByRole('textbox');
    fireEvent.change(box, { target: { value: '🍞' } });
    fireEvent.click(screen.getByRole('button', { name: /save marker/i }));

    // The column shows the rule it now has, and its coverage beside it.
    expect(await screen.findByText('🍞')).toBeTruthy();
    expect(screen.getByRole('button', { name: /^marker for #prio1$/i })).toBeTruthy();
  });

  /**
   * The running Change's panel leaves the moment it ends. How it ended — and, for a failure that
   * needs a fresh sign-in, the way out — is said where the person watched it run, not only in the
   * history below the table.
   */
  it('announces how a change ended where it was watched running', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });

    const server = show({ queue: { active: queued(), history: [] } });

    await screen.findByText('#Prio1');
    expect(screen.getByRole('region', { name: /running change/i })).toBeTruthy();

    server.answerQueue({ active: null, history: [failed()] });
    await vi.advanceTimersByTimeAsync(2500);

    expect(await screen.findByText(/the change did not finish/i)).toBeTruthy();
    // Said in the notice above the table and in the history below it: the same sentence twice.
    expect(screen.getAllByText(/the sign-in has expired/i)).toHaveLength(2);
    expect(screen.getAllByRole('link', { name: /sign in again/i }).length).toBeGreaterThan(0);
    expect(screen.queryByRole('region', { name: /running change/i })).toBeNull();
  });
});

/** A server that answers the four reads the page makes, with the inventory answer holdable. */
function show({
  inventory: first = inventory([row(), row({ key: 'KUNDE', canonicalSpelling: 'kunde' })]),
  queue: initialQueue = { active: null, history: [] },
  markerRules: initialRules = [],
}: { inventory?: HashtagInventoryPage; queue?: ChangeQueue; markerRules?: MarkerRule[] } = {}) {
  let held: ((page: HashtagInventoryPage) => void) | null = null;
  let hold = false;
  let queue = initialQueue;
  let rules = initialRules;

  vi.stubGlobal(
    'fetch',
    vi.fn((path: string, init?: RequestInit) => {
      if (path.startsWith('/api/index/status')) return json(status());
      if (path.startsWith('/api/index/scan')) return json({ queued: true });
      if (path.startsWith('/api/changes')) return json(queue);

      if (path.startsWith('/api/marker-rules')) {
        // `abandoned` is part of the answer, not an extra: a person with no deleted rules has an
        // empty list rather than none, and the panel sums it.
        if (init?.method !== 'POST') return json({ rules, abandoned: [] });

        // A rule declares and writes no title, so the stub does what the server does: keep it, and
        // hand it back for the row to show.
        const body = JSON.parse(String(init.body)) as { spelling: string; marker: string };
        const created = markerRule({ spelling: body.spelling, marker: body.marker, key: body.spelling.toUpperCase() });

        rules = [...rules, created];

        return json(created);
      }

      if (path.startsWith('/api/hashtags')) {
        if (!hold) return json(first);

        return new Promise<Response>((resolve) => {
          held = (page) => void json(page).then(resolve);
        });
      }

      return Promise.resolve(new Response(null, { status: 404 }));
    }),
  );

  render(
    <FluentProvider theme={cloudwerkLightTheme}>
      <Workbench />
    </FluentProvider>,
  );

  return {
    holdInventory: () => {
      hold = true;
    },
    releaseInventory: (page: HashtagInventoryPage) => {
      hold = false;
      held?.(page);
    },
    answerQueue: (next: ChangeQueue) => {
      queue = next;
    },
  };
}

function markerRule(overrides: Partial<MarkerRule> = {}): MarkerRule {
  return {
    id: 'r1',
    key: 'PRIO1',
    spelling: 'Prio1',
    marker: '🍞',
    retiredMarker: null,
    position: 10,
    taggedTaskCount: 2,
    markedTaskCount: 0,
    staleTaskCount: 0,
    retiredTaskCount: 0,
    ...overrides,
  };
}

function queued(overrides: Partial<Change> = {}): Change {
  return {
    id: 'c1',
    kind: 'Rename',
    sourceKeys: ['PRIO1'],
    targetSpelling: 'Priority1',
    appliedMarkers: [],
    state: 'Running',
    plannedTaskCount: 2,
    writtenCount: 0,
    skippedCount: 0,
    failedCount: 0,
    cancelRequested: false,
    requestedAt: '2026-09-03T10:00:00Z',
    completedAt: null,
    failureCode: 'None',
    failureReason: null,
    undoOfChangeId: null,
    canUndo: false,
    ...overrides,
  };
}

function failed(): Change {
  return queued({
    state: 'Failed',
    completedAt: '2026-09-03T10:01:00Z',
    failureCode: 'ReconnectRequired',
    failureReason: 'The sign-in has expired.',
  });
}

function json(body: unknown): Promise<Response> {
  return Promise.resolve(
    new Response(JSON.stringify(body), { status: 200, headers: { 'content-type': 'application/json' } }),
  );
}

function status(): IndexStatus {
  return {
    activity: 'Idle',
    currentAsOf: '2026-09-03T11:00:00Z',
    hasCompletedFirstScan: true,
    listCount: 1,
    listsIndexed: 1,
    tasksIndexed: 40,
    lists: [
      {
        taskListId: 'l1',
        displayName: 'Arbeit',
        state: 'Indexed',
        tasksIndexed: 40,
        lastSuccessfulSyncAt: '2026-09-03T11:00:00Z',
        lastCompletedScanAt: '2026-09-03T11:00:00Z',
        failureReason: null,
        failureCode: 'None',
      },
    ],
    lastScanFailure: null,
    lastScanFailureCode: 'None',
  };
}

function inventory(rows: HashtagInventoryRow[]): HashtagInventoryPage {
  return { rows, totalCount: rows.length, page: 1, pageSize: 50 };
}

function row(overrides: Partial<HashtagInventoryRow> = {}): HashtagInventoryRow {
  return {
    key: 'PRIO1',
    canonicalSpelling: 'Prio1',
    spellings: ['Prio1'],
    taskCount: 2,
    listCount: 1,
    lastUsedAt: '2026-08-12T09:00:00Z',
    hasMultipleSpellings: false,
    hasNearDuplicates: false,
    isStale: false,
    ...overrides,
  };
}
