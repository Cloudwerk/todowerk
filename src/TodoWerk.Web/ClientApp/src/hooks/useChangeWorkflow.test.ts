import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useChangeWorkflow } from './useChangeWorkflow';
import { ApiError } from '../api/http';
import * as client from '../api/client';
import type { Change, ChangePreview, HashtagInventoryRow } from '../api/types';

vi.mock('../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof client>()),
  getChangeQueue: vi.fn(),
  previewChange: vi.fn(),
  confirmChange: vi.fn(),
  cancelChange: vi.fn(),
  undoChange: vi.fn(),
}));

const mocked = vi.mocked(client);

/**
 * The state the client holds before the server has confirmed it — a target being typed, a preview
 * one keystroke out of date, a Change queued but not started. That state is what this suite is
 * for.
 */
describe('useChangeWorkflow', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    mocked.getChangeQueue.mockResolvedValue({ active: null, history: [] });
    mocked.previewChange.mockResolvedValue(preview());
  });

  it('previews once typing pauses rather than once per keystroke', async () => {
    const { result } = await render();

    act(() => result.current.start([row()], 'Priority1'));
    act(() => result.current.setTarget('Prior'));
    act(() => result.current.setTarget('Priorit'));
    act(() => result.current.setTarget('Priority1'));

    expect(mocked.previewChange).not.toHaveBeenCalled();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    expect(mocked.previewChange).toHaveBeenCalledTimes(1);
    expect(mocked.previewChange).toHaveBeenCalledWith(['PRIO1'], 'Priority1');
  });

  /**
   * A preview that lands after the target moved on describes a spelling nobody is asking about
   * any more, and confirming it would queue a Change the dialog never showed.
   */
  it('discards a preview whose target the user has moved on from', async () => {
    const { result } = await render();

    mocked.previewChange.mockResolvedValue(preview({ targetSpelling: 'Stale' }));

    act(() => result.current.start([row()], 'Stale'));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    expect(result.current.dialog.preview?.targetSpelling).toBe('Stale');

    // Typing again clears the preview immediately: what is on screen must never describe a
    // spelling other than the one in the box.
    act(() => result.current.setTarget('Fresh'));

    expect(result.current.dialog.preview).toBeUndefined();
  });

  /**
   * The second confirmation a Merge asks for is the preview's own verdict, not a constant. That
   * is what makes a rename which became a Merge since — because a scan put the target in the
   * index — get refused rather than waved through.
   */
  it('sends the preview verdict as the merge confirmation', async () => {
    const { result } = await render();

    mocked.previewChange.mockResolvedValue(preview({ kind: 'Merge', requiresMergeConfirmation: true }));
    mocked.confirmChange.mockResolvedValue(queued());

    act(() => result.current.start([row(), row({ key: 'KLIENT', canonicalSpelling: 'klient' })], 'customer'));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    await act(async () => {
      result.current.confirm();
    });

    expect(mocked.confirmChange).toHaveBeenCalledWith(['PRIO1', 'KLIENT'], 'Priority1', true, null);
    expect(result.current.dialog.open).toBe(false);
    expect(result.current.inFlight).toBe(true);
  });

  it('keeps the dialog open and shows the server refusal in its own words', async () => {
    const { result } = await render();

    mocked.confirmChange.mockRejectedValue(
      new ApiError({ status: 409, detail: 'You already have a change waiting or running.' }),
    );

    act(() => result.current.start([row()], 'Priority1'));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    await act(async () => {
      result.current.confirm();
    });

    expect(result.current.dialog.open).toBe(true);
    expect(result.current.dialog.error).toBe('You already have a change waiting or running.');
    expect(result.current.dialog.confirming).toBe(false);
  });

  it('refuses to preview an empty target only through the server, never by guessing', async () => {
    const { result } = await render();

    mocked.previewChange.mockRejectedValue(
      new ApiError({ status: 400, detail: 'That is not a name a hashtag can have.' }),
    );

    act(() => result.current.start([row()], ''));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    expect(result.current.dialog.error).toBe('That is not a name a hashtag can have.');
    expect(result.current.dialog.preview).toBeUndefined();
  });

  /**
   * The index is left to catch up through Graph, so the page has to re-read itself when a Change
   * stops running — once, on the edge, not on every poll.
   */
  it('tells the page to refresh when the running change finishes', async () => {
    const onCompleted = vi.fn();

    mocked.getChangeQueue.mockResolvedValue({ active: queued(), history: [] });

    const { result } = await render(onCompleted);

    expect(result.current.inFlight).toBe(true);

    mocked.getChangeQueue.mockResolvedValue({ active: null, history: [finished()] });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2500);
    });

    expect(onCompleted).toHaveBeenCalledTimes(1);
    expect(result.current.history).toHaveLength(1);
  });

  async function render(onCompleted: () => void = () => {}) {
    const rendered = renderHook(() => useChangeWorkflow(onCompleted));

    // The first queue read happens on mount; let it settle so no test starts mid-flight.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    return rendered;
  }
});

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

function preview(overrides: Partial<ChangePreview> = {}): ChangePreview {
  return {
    kind: 'Rename',
    sourceKeys: ['PRIO1'],
    targetSpelling: 'Priority1',
    items: [
      { taskListId: 'l1', listDisplayName: 'Arbeit', currentTitle: 'Angebot #Prio1', newTitle: 'Angebot #Priority1' },
    ],
    taskCount: 1,
    requiresMergeConfirmation: false,
    excludedLists: [],
    skips: [],
    markerRules: { sourceRules: [], targetRule: null, survivingMarker: null, requiresSurvivorChoice: false },
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
    state: 'Pending',
    plannedTaskCount: 1,
    writtenCount: 0,
    skippedCount: 0,
    failedCount: 0,
    cancelRequested: false,
    requestedAt: '2026-08-12T10:00:00Z',
    completedAt: null,
    failureCode: 'None',
    failureReason: null,
    undoOfChangeId: null,
    canUndo: false,
    ...overrides,
  };
}

function finished(): Change {
  return queued({
    state: 'Completed',
    writtenCount: 1,
    completedAt: '2026-08-12T10:01:00Z',
    canUndo: true,
  });
}
