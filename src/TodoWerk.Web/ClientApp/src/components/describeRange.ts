/**
 * Which rows of a paged set are on screen — "51–100 of 312" — rather than which page, which says
 * where in the book but not what is open.
 */
export function describeRange({
  page,
  pageSize,
  totalCount,
}: {
  page: number;
  pageSize: number;
  totalCount: number;
}): string {
  if (totalCount === 0) return '0 of 0';

  // A set that shrank under the caller can leave its page past the end for a render, until the
  // page is clamped; the range clamps too, rather than reading "301–290".
  const current = Math.min(page, Math.ceil(totalCount / pageSize));
  const first = (current - 1) * pageSize + 1;
  const last = Math.min(current * pageSize, totalCount);

  return first === last ? `${first} of ${totalCount}` : `${first}–${last} of ${totalCount}`;
}
