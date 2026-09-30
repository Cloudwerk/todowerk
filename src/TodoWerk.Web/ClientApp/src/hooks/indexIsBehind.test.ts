import { describe, expect, it } from 'vitest';
import { indexIsBehind } from './indexIsBehind';
import type { Change, IndexStatus } from '../api/types';

/**
 * The one piece of arithmetic behind "the table is one scan behind". Easy to get backwards, and
 * getting it backwards means either a permanent nag or an inventory that silently contradicts a
 * rename the user just watched succeed.
 */
describe('indexIsBehind', () => {
  it('is false before anything has been changed', () => {
    expect(indexIsBehind(status('2026-08-12T10:00:00Z'), [])).toBe(false);
  });

  it('is true while the index predates the change that finished', () => {
    expect(indexIsBehind(status('2026-08-12T10:00:00Z'), [change('2026-08-12T10:05:00Z')])).toBe(true);
  });

  it('is false once a scan has run since the change', () => {
    expect(indexIsBehind(status('2026-08-12T10:06:00Z'), [change('2026-08-12T10:05:00Z')])).toBe(false);
  });

  /** A Change that wrote nothing left the index no further behind than it already was. */
  it('ignores a change that wrote nothing', () => {
    expect(
      indexIsBehind(status('2026-08-12T10:00:00Z'), [{ ...change('2026-08-12T10:05:00Z'), writtenCount: 0 }]),
    ).toBe(false);
  });

  /** Nothing has ever synced, so the index cannot possibly include the write. */
  it('is true when no successful sync has happened at all', () => {
    expect(indexIsBehind(status(null), [change('2026-08-12T10:05:00Z')])).toBe(true);
  });

  it('says nothing when the status has not loaded', () => {
    expect(indexIsBehind(undefined, [change('2026-08-12T10:05:00Z')])).toBe(false);
  });

  function status(currentAsOf: string | null): IndexStatus {
    return {
      activity: 'Idle',
      currentAsOf,
      hasCompletedFirstScan: true,
      listCount: 1,
      listsIndexed: 1,
      tasksIndexed: 3,
      lists: [],
      lastScanFailure: null,
      lastScanFailureCode: 'None',
    };
  }

  function change(completedAt: string): Change {
    return {
      id: 'c1',
      kind: 'Rename',
      sourceKeys: ['PRIO1'],
      targetSpelling: 'Priority1',
      appliedMarkers: [],
      state: 'Completed',
      plannedTaskCount: 2,
      writtenCount: 2,
      skippedCount: 0,
      failedCount: 0,
      cancelRequested: false,
      requestedAt: '2026-08-12T10:04:00Z',
      completedAt,
      failureCode: 'None',
      failureReason: null,
      undoOfChangeId: null,
      canUndo: true,
    };
  }
});
