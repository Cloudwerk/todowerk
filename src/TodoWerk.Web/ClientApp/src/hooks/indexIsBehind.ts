import type { Change, IndexStatus } from '../api/types';

/**
 * Whether the inventory on screen predates the Change that just finished.
 *
 * A Change writes to Microsoft To Do and leaves the index to catch up through Graph (ADR-0006), so
 * for one scan the table disagrees with what the user just did. Saying so is the whole point: an
 * inventory that silently contradicts a rename somebody watched succeed reads as a bug.
 *
 * Kept as a function rather than folded into the page, because it is the one piece of arithmetic
 * behind that sentence and it is easy to get backwards.
 */
export function indexIsBehind(status: IndexStatus | undefined, history: readonly Change[]): boolean {
  if (!status) return false;

  const finished = history.find((change) => change.completedAt !== null && change.writtenCount > 0);

  if (!finished?.completedAt) return false;

  // No successful sync at all yet: the index cannot possibly include the write.
  if (!status.currentAsOf) return true;

  return Date.parse(status.currentAsOf) < Date.parse(finished.completedAt);
}
