import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { useApiQuery } from './useApi';

/**
 * What the page can say while a request is in flight. The data stays on screen when the key
 * changes — that was always so — but the page has to be able to tell that the rows it is showing
 * answer an earlier question, or a pager reads "Page 2" over page 1's rows for as long as the
 * query takes.
 */
describe('useApiQuery', () => {
  it('reports a request in flight from the first read until it lands', async () => {
    const answers = new Answers();
    const { result } = renderHook(() => useApiQuery(answers.load, 'a'));

    expect(result.current.fetching).toBe(true);
    // Nothing is on screen yet, so nothing is stale.
    expect(result.current.stale).toBe(false);

    await act(async () => answers.resolve('one'));

    expect(result.current.fetching).toBe(false);
    expect(result.current.stale).toBe(false);
    expect(result.current.state).toEqual({ status: 'ready', data: 'one' });
  });

  it('keeps the previous answer on screen while a new key loads, and says it is stale', async () => {
    const answers = new Answers();
    const { result, rerender } = renderHook(({ key }) => useApiQuery(answers.load, key), {
      initialProps: { key: 'a' },
    });

    await act(async () => answers.resolve('one'));

    rerender({ key: 'b' });

    expect(result.current.state).toEqual({ status: 'ready', data: 'one' });
    expect(result.current.fetching).toBe(true);
    expect(result.current.stale).toBe(true);

    await act(async () => answers.resolve('two'));

    expect(result.current.state).toEqual({ status: 'ready', data: 'two' });
    expect(result.current.fetching).toBe(false);
    expect(result.current.stale).toBe(false);
  });

  /** A poll re-reads the same question: the rows on screen are current, only possibly old. */
  it('is not stale while the same key is re-read', async () => {
    const answers = new Answers();
    const { result } = renderHook(() => useApiQuery(answers.load, 'a'));

    await act(async () => answers.resolve('one'));

    act(() => result.current.refresh());

    expect(result.current.fetching).toBe(true);
    expect(result.current.stale).toBe(false);

    await act(async () => answers.resolve('one again'));

    expect(result.current.fetching).toBe(false);
    expect(result.current.state).toEqual({ status: 'ready', data: 'one again' });
  });

  /**
   * A failure that drops the rows leaves nothing on screen, so nothing is stale — whatever key
   * the rows used to answer. Without this, a retry after that failure would report stale rows that
   * were not there.
   */
  it('is not stale after a failure has dropped the rows, whatever comes next', async () => {
    const answers = new Answers();
    const { result, rerender } = renderHook(({ key }) => useApiQuery(answers.load, key), {
      initialProps: { key: 'a' },
    });

    await act(async () => answers.resolve('one'));

    rerender({ key: 'b' });
    await act(async () => answers.reject(new TypeError('offline')));

    expect(result.current.state.status).toBe('error');
    expect(result.current.stale).toBe(false);

    rerender({ key: 'c' });

    expect(result.current.fetching).toBe(true);
    expect(result.current.stale).toBe(false);
  });

  it('settles the flag when the request fails too', async () => {
    const answers = new Answers();
    const { result } = renderHook(() => useApiQuery(answers.load, 'a'));

    await act(async () => answers.reject(new TypeError('offline')));

    expect(result.current.fetching).toBe(false);
    expect(result.current.state.status).toBe('error');
  });
});

/** One pending promise per request, answered from the test in the order the hook asked. */
class Answers {
  private readonly pending: { resolve: (value: string) => void; reject: (error: unknown) => void }[] = [];

  readonly load = () =>
    new Promise<string>((resolve, reject) => {
      this.pending.push({ resolve, reject });
    });

  async resolve(value: string) {
    this.pending.shift()?.resolve(value);
    await Promise.resolve();
  }

  async reject(error: unknown) {
    this.pending.shift()?.reject(error);
    await Promise.resolve();
  }
}
