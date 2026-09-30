import { describe, expect, it } from 'vitest';
import { describeRange } from './describeRange';

/** "51–100 of 312" says which rows are on screen; "Page 2 of 7" only says where in the book. */
describe('describeRange', () => {
  it('names the rows on a full page', () => {
    expect(describeRange({ page: 2, pageSize: 50, totalCount: 312 })).toBe('51–100 of 312');
  });

  it('ends the last page at the total rather than past it', () => {
    expect(describeRange({ page: 7, pageSize: 50, totalCount: 312 })).toBe('301–312 of 312');
  });

  it('names a single row as one row', () => {
    expect(describeRange({ page: 7, pageSize: 50, totalCount: 301 })).toBe('301 of 301');
  });

  /** A set that shrank leaves the page past the end for a render; the range reads as the last page. */
  it('reads as the last page when the page is past the end', () => {
    expect(describeRange({ page: 7, pageSize: 50, totalCount: 290 })).toBe('251–290 of 290');
  });

  it('names an empty set plainly', () => {
    expect(describeRange({ page: 1, pageSize: 50, totalCount: 0 })).toBe('0 of 0');
  });
});
