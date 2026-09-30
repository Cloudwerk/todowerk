/**
 * One date format for the whole Workbench. The table and the detail panel show the same
 * `lastUsedAt`, and two formats for one value reads as two different facts.
 */
export function formatDate(value: string): string {
  return new Date(value).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
}
