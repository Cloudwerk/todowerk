import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useMarkerRules } from './useMarkerRules';
import { ApiError } from '../api/http';
import * as client from '../api/client';
import type { MarkerRule } from '../api/types';

vi.mock('../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof client>()),
  getMarkerRules: vi.fn(),
  createMarkerRule: vi.fn(),
  updateMarkerRule: vi.fn(),
  deleteMarkerRule: vi.fn(),
}));

const mocked = vi.mocked(client);

/**
 * Authoring a Marker Rule from the browser. None of this writes a task title: a rule
 * declares, and applying it is a Change (ADR-0014).
 */
describe('useMarkerRules', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocked.getMarkerRules.mockResolvedValue({ rules: [rule()], abandoned: [] });
    mocked.createMarkerRule.mockResolvedValue(rule());
    mocked.updateMarkerRule.mockResolvedValue(rule());
    mocked.deleteMarkerRule.mockResolvedValue(undefined);
  });

  it('lists the rules in the order the server returned them', async () => {
    mocked.getMarkerRules.mockResolvedValue({
      rules: [rule({ id: 'r2', spelling: 'coffee', marker: '☕', position: 10 }), rule({ position: 20 })],
      abandoned: [],
    });

    const { result } = await render();

    expect(result.current.rules.map((each) => each.marker)).toEqual(['☕', '🍞']);
  });

  it('creates a rule for a hashtag that has none, and re-reads the list', async () => {
    const { result } = await render();

    act(() => result.current.edit({ spelling: 'bread', rule: undefined }));

    await act(async () => {
      result.current.save('🍞');
    });

    expect(mocked.createMarkerRule).toHaveBeenCalledWith('bread', '🍞');
    expect(mocked.getMarkerRules).toHaveBeenCalledTimes(2);
    expect(result.current.editing).toBeNull();
  });

  it('changes the marker of a rule that already exists', async () => {
    const { result } = await render();

    act(() => result.current.edit({ spelling: 'bread', rule: rule() }));

    await act(async () => {
      result.current.save('🥐');
    });

    expect(mocked.updateMarkerRule).toHaveBeenCalledWith('r1', { marker: '🥐' });
    expect(mocked.createMarkerRule).not.toHaveBeenCalled();
  });

  /**
   * The refusal names the hashtag that holds the emoji, and it is answered by choosing another one
   * in the dialog — so it stays there rather than being raised over the whole page, and it is the
   * server's sentence rather than one of ours.
   */
  it('keeps the server refusal in the dialog, in the server words', async () => {
    mocked.createMarkerRule.mockRejectedValue(
      new ApiError({
        status: 409,
        code: 'Markers.MarkerInUse',
        detail: 'That emoji is already the marker for #bread. Choose another, or change that rule first.',
      }),
    );

    const { result } = await render();

    act(() => result.current.edit({ spelling: 'loaf', rule: undefined }));

    await act(async () => {
      result.current.save('🍞');
    });

    expect(result.current.editError).toBe(
      'That emoji is already the marker for #bread. Choose another, or change that rule first.',
    );

    // Still open on the same hashtag, because choosing a different emoji is what answers it.
    expect(result.current.editing?.spelling).toBe('loaf');
  });

  it('reorders a rule and shows the order the server answers with', async () => {
    const first = rule({ id: 'r1', spelling: 'bread', marker: '🍞' });
    const second = rule({ id: 'r2', spelling: 'coffee', marker: '☕' });

    mocked.getMarkerRules.mockResolvedValueOnce({ rules: [first, second], abandoned: [] });

    const { result } = await render();

    expect(result.current.rules.map((each) => each.marker)).toEqual(['🍞', '☕']);

    mocked.getMarkerRules.mockResolvedValue({ rules: [second, first], abandoned: [] });

    await act(async () => {
      result.current.move('r2', 'Up');
    });

    expect(mocked.updateMarkerRule).toHaveBeenCalledWith('r2', { move: 'Up' });
    expect(result.current.rules.map((each) => each.marker)).toEqual(['☕', '🍞']);
  });

  it('deletes a rule and re-reads the list', async () => {
    const { result } = await render();

    mocked.getMarkerRules.mockResolvedValue({ rules: [], abandoned: [] });

    await act(async () => {
      result.current.remove('r1');
    });

    expect(mocked.deleteMarkerRule).toHaveBeenCalledWith('r1');
    expect(result.current.rules).toHaveLength(0);
  });

  it('raises a refusal about the list itself as a notice', async () => {
    mocked.updateMarkerRule.mockRejectedValue(
      new ApiError({
        status: 400,
        code: 'Markers.RuleCannotMove',
        detail: 'That rule is already at the end it is being moved towards.',
      }),
    );

    const { result } = await render();

    await act(async () => {
      result.current.move('r1', 'Up');
    });

    expect(result.current.notice).toBe('That rule is already at the end it is being moved towards.');
  });

  async function render() {
    const rendered = renderHook(() => useMarkerRules());

    // The first read happens on mount; let it settle so no test starts mid-flight.
    await act(async () => {
      await Promise.resolve();
    });

    return rendered;
  }
});

function rule(overrides: Partial<MarkerRule> = {}): MarkerRule {
  return {
    id: 'r1',
    key: 'BREAD',
    spelling: 'bread',
    marker: '🍞',
    retiredMarker: null,
    position: 10,
    taggedTaskCount: 3,
    markedTaskCount: 1,
    staleTaskCount: 0,
    retiredTaskCount: 0,
    ...overrides,
  };
}
