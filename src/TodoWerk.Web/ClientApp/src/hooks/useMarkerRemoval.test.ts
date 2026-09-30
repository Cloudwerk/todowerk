import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useChangeWorkflow } from './useChangeWorkflow';
import * as client from '../api/client';
import type { Change, ChangePreview } from '../api/types';

vi.mock('../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof client>()),
  getChangeQueue: vi.fn(),
  previewChange: vi.fn(),
  previewMarkerApply: vi.fn(),
  previewMarkerRemoval: vi.fn(),
  confirmChange: vi.fn(),
  confirmMarkerApply: vi.fn(),
  confirmMarkerRemoval: vi.fn(),
  cancelChange: vi.fn(),
  undoChange: vi.fn(),
}));

const mocked = vi.mocked(client);

/**
 * The fifth Change from the browser's side: the same dialog and the same write path as the
 * fourth, in the other direction.
 *
 * What differs is the scope. An Apply is scoped by hashtag; a Remove cannot be, because a stale
 * marker is stale precisely because its hashtag has gone — and one a deleted rule left behind never
 * had a hashtag to be named by. So a Remove is scoped by the emoji itself (ADR-0014).
 */
describe('useChangeWorkflow, removing markers', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
    mocked.getChangeQueue.mockResolvedValue({ active: null, history: [] });
    mocked.previewMarkerRemoval.mockResolvedValue(preview());
    mocked.confirmMarkerRemoval.mockResolvedValue(queued());
  });

  it('asks for every stale marker when the scope names none', async () => {
    const { result } = await render();

    act(() => result.current.startMarkers({ action: 'remove', marker: null, label: 'Remove stale markers' }));

    await settle();

    expect(mocked.previewMarkerRemoval).toHaveBeenCalledWith(null);
    expect(mocked.previewMarkerApply).not.toHaveBeenCalled();
    expect(mocked.previewChange).not.toHaveBeenCalled();
    expect(result.current.dialog.preview?.kind).toBe('RemoveMarkers');
  });

  it('asks for one emoji when the scope names it', async () => {
    const { result } = await render();

    act(() =>
      result.current.startMarkers({ action: 'remove', marker: '🍞', label: 'Remove stale 🍞 markers' }),
    );

    await settle();

    expect(mocked.previewMarkerRemoval).toHaveBeenCalledWith('🍞');
    expect(result.current.dialog.markerScope?.label).toBe('Remove stale 🍞 markers');
  });

  /**
   * The direction is part of the scope, not a second thing to get right at confirmation time. A
   * dialog opened to remove can only ever confirm a removal.
   */
  it('confirms the removal it previewed, and never an apply', async () => {
    const { result } = await render();

    act(() =>
      result.current.startMarkers({ action: 'remove', marker: '🍞', label: 'Remove stale 🍞 markers' }),
    );

    await settle();
    await act(async () => {
      result.current.confirm();
    });

    expect(mocked.confirmMarkerRemoval).toHaveBeenCalledWith('🍞');
    expect(mocked.confirmMarkerApply).not.toHaveBeenCalled();
    expect(mocked.confirmChange).not.toHaveBeenCalled();
    expect(result.current.active?.kind).toBe('RemoveMarkers');
  });

  it('shows the server refusal in the dialog rather than over the page', async () => {
    mocked.previewMarkerRemoval.mockRejectedValue(new Error('nothing to remove'));

    const { result } = await render();

    act(() => result.current.startMarkers({ action: 'remove', marker: null, label: 'Remove stale markers' }));

    await settle();

    expect(result.current.dialog.error).toBeTruthy();
    expect(result.current.dialog.preview).toBeUndefined();
    expect(result.current.notice).toBeUndefined();
  });

  /** Dismissing takes the question with it, the way it does for every other Change. */
  it('forgets the scope when the dialog is dismissed', async () => {
    const { result } = await render();

    act(() => result.current.startMarkers({ action: 'remove', marker: null, label: 'Remove stale markers' }));

    await settle();

    act(() => result.current.dismiss());

    expect(result.current.dialog.open).toBe(false);
    expect(result.current.dialog.markerScope).toBeUndefined();
  });

  async function settle() {
    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });
  }

  async function render() {
    const rendered = renderHook(() => useChangeWorkflow(() => {}));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    return rendered;
  }
});

function preview(overrides: Partial<ChangePreview> = {}): ChangePreview {
  return {
    kind: 'RemoveMarkers',
    sourceKeys: [],
    targetSpelling: '',
    items: [
      {
        taskListId: 'l1',
        listDisplayName: 'Arbeit',
        currentTitle: '🍞 Brot kaufen',
        newTitle: 'Brot kaufen',
      },
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
    kind: 'RemoveMarkers',

    // A Remove records no scope keys at all: its scope is the markers it carries.
    sourceKeys: [],
    targetSpelling: '',
    appliedMarkers: [
      {
        key: 'BREAD',
        spelling: 'bread',
        marker: '🍞',
        retiredMarker: null,
        position: 10,
        abandoned: false,
        removable: true,
      },
    ],
    state: 'Pending',
    plannedTaskCount: 1,
    writtenCount: 0,
    skippedCount: 0,
    failedCount: 0,
    cancelRequested: false,
    requestedAt: '2026-09-08T10:00:00Z',
    completedAt: null,
    failureCode: 'None',
    failureReason: null,
    undoOfChangeId: null,
    canUndo: false,
    ...overrides,
  };
}
