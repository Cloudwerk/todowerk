import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { narrowScreenQuery, useNarrowScreen } from './useNarrowScreen';

describe('useNarrowScreen', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('follows the screen when it turns or a window is resized', () => {
    const screenWidth = stubScreen(false);

    const { result } = renderHook(() => useNarrowScreen());
    expect(result.current).toBe(false);

    act(() => screenWidth.change(true));
    expect(result.current).toBe(true);

    act(() => screenWidth.change(false));
    expect(result.current).toBe(false);
  });

  // A host with no way to ask gets the layout every screen could show before this existed.
  it('answers wide where there is no matchMedia', () => {
    vi.stubGlobal('matchMedia', undefined);

    expect(renderHook(() => useNarrowScreen()).result.current).toBe(false);
  });
});

function stubScreen(narrow: boolean) {
  const handlers = new Set<() => void>();
  const query = {
    matches: narrow,
    addEventListener: (_type: string, handler: () => void) => handlers.add(handler),
    removeEventListener: (_type: string, handler: () => void) => handlers.delete(handler),
  };

  vi.stubGlobal('matchMedia', (media: string) => {
    expect(media).toBe(narrowScreenQuery);
    return query;
  });

  return {
    change(nowNarrow: boolean) {
      query.matches = nowNarrow;
      for (const handler of handlers) handler();
    },
  };
}
