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
  confirmChange: vi.fn(),
  confirmMarkerApply: vi.fn(),
  cancelChange: vi.fn(),
  undoChange: vi.fn(),
}));

const mocked = vi.mocked(client);

/**
 * The fourth Change from the browser's side: two entry points, one dialog, one write. What
 * differs between them is the scope — every rule, or the one the button sat beside — and nothing
 * else, because the write is always the whole block (ADR-0014).
 */
describe('useChangeWorkflow, applying markers', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
    mocked.getChangeQueue.mockResolvedValue({ active: null, history: [] });
    mocked.previewMarkerApply.mockResolvedValue(preview());
    mocked.confirmMarkerApply.mockResolvedValue(queued());
  });

  it('asks for every rule when the scope carries no key', async () => {
    const { result } = await render();

    act(() => result.current.startMarkers({ action: 'apply', ruleKey: null, label: 'Apply all markers' }));

    await settle();

    expect(mocked.previewMarkerApply).toHaveBeenCalledWith(null);
    expect(mocked.previewChange).not.toHaveBeenCalled();
    expect(result.current.dialog.preview?.kind).toBe('ApplyMarkers');
  });

  it('asks for one rule when the scope names its hashtag', async () => {
    const { result } = await render();

    act(() => result.current.startMarkers({ action: 'apply', ruleKey: 'BREAD', label: 'Apply 🍞 to #bread' }));

    await settle();

    expect(mocked.previewMarkerApply).toHaveBeenCalledWith('BREAD');
    expect(result.current.dialog.markerScope?.label).toBe('Apply 🍞 to #bread');
  });

  it('confirms the scope it previewed, and nothing the browser could have edited', async () => {
    const { result } = await render();

    act(() => result.current.startMarkers({ action: 'apply', ruleKey: 'BREAD', label: 'Apply 🍞 to #bread' }));

    await settle();

    await act(async () => {
      result.current.confirm();
    });

    expect(mocked.confirmMarkerApply).toHaveBeenCalledWith('BREAD');
    expect(mocked.confirmChange).not.toHaveBeenCalled();
    expect(result.current.inFlight).toBe(true);
    expect(result.current.dialog.open).toBe(false);
  });

  /** The skips the preview knows in advance travel with it, so the dialog can name them. */
  it('carries the skips the preview named', async () => {
    mocked.previewMarkerApply.mockResolvedValue(
      preview({
        skips: [
          {
            taskListId: 'l1',
            listDisplayName: 'Arbeit',
            currentTitle: 'Ein sehr langer Titel #bread',
            reason: 'The new title would be longer than Microsoft To Do stores, so this task was left alone.',
          },
        ],
      }),
    );

    const { result } = await render();

    act(() => result.current.startMarkers({ action: 'apply', ruleKey: null, label: 'Apply all markers' }));

    await settle();

    expect(result.current.dialog.preview?.skips).toHaveLength(1);
  });

  /**
   * The choice a Merge asks for when two marked hashtags are folded together, sent with the
   * confirmation rather than guessed by the server.
   */
  it('sends the surviving marker the user chose', async () => {
    mocked.previewChange.mockResolvedValue(
      preview({
        kind: 'Merge',
        requiresMergeConfirmation: true,
        targetSpelling: 'breakfast',
        markerRules: {
          sourceRules: [
            { key: 'BREAD', spelling: 'bread', marker: '🍞' },
            { key: 'COFFEE', spelling: 'coffee', marker: '☕' },
          ],
          targetRule: null,
          survivingMarker: null,
          requiresSurvivorChoice: true,
        },
      }),
    );
    mocked.confirmChange.mockResolvedValue(queued({ kind: 'Merge' }));

    const { result } = await render();

    act(() =>
      result.current.start(
        [
          {
            key: 'BREAD',
            canonicalSpelling: 'bread',
            spellings: ['bread'],
            taskCount: 1,
            listCount: 1,
            lastUsedAt: '2026-09-01T09:00:00Z',
            hasMultipleSpellings: false,
            hasNearDuplicates: false,
            isStale: false,
          },
        ],
        'breakfast',
      ),
    );

    await settle();

    act(() => result.current.chooseSurvivingMarker('☕'));

    await act(async () => {
      result.current.confirm();
    });

    expect(mocked.confirmChange).toHaveBeenCalledWith(['BREAD'], 'breakfast', true, '☕');
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
    kind: 'ApplyMarkers',
    sourceKeys: ['BREAD'],
    targetSpelling: '',
    items: [
      {
        taskListId: 'l1',
        listDisplayName: 'Arbeit',
        currentTitle: 'Brot kaufen #bread',
        newTitle: '🍞 Brot kaufen #bread',
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
    kind: 'ApplyMarkers',
    sourceKeys: ['BREAD'],
    targetSpelling: '',
    appliedMarkers: [
      {
        key: 'BREAD',
        spelling: 'bread',
        marker: '🍞',
        retiredMarker: null,
        position: 10,
        abandoned: false,
        removable: false,
      },
    ],
    state: 'Pending',
    plannedTaskCount: 1,
    writtenCount: 0,
    skippedCount: 0,
    failedCount: 0,
    cancelRequested: false,
    requestedAt: '2026-09-05T10:00:00Z',
    completedAt: null,
    failureCode: 'None',
    failureReason: null,
    undoOfChangeId: null,
    canUndo: false,
    ...overrides,
  };
}
