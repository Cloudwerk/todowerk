import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { FluentProvider } from '@fluentui/react-components';
import { HashtagDetailPanel } from './HashtagDetailPanel';
import { cloudwerkLightTheme } from '../theme';
import type { HashtagInventoryRow } from '../api/types';

/**
 * The gate, stated outright: a Merge is by definition several sources, so below two
 * selected rows there is no merge entry point at all. One button follows the selection — one row
 * offers to change that Hashtag, several offer to combine them — and a Merge can never be started
 * from a single row wearing the wrong warning.
 */
describe('HashtagDetailPanel', () => {
  it('offers no action with nothing selected', () => {
    show([]);

    expect(screen.queryByRole('button')).toBeNull();
  });

  it('offers to change the one Hashtag selected, and no merge', () => {
    show([row()]);

    expect(button(/change #prio1/i).disabled).toBe(false);
    expect(screen.queryByRole('button', { name: /combine/i })).toBeNull();
  });

  it('offers to combine two selected Hashtags, and no single-row change', () => {
    show([row(), row({ key: 'KLIENT', canonicalSpelling: 'klient' })]);

    expect(button(/combine 2 into one/i).disabled).toBe(false);
    expect(screen.queryByRole('button', { name: /change #/i })).toBeNull();
  });

  /** One Change per user at a time (ADR-0006), so the entry point closes while one runs. */
  it('closes the entry point while a change is already in flight', () => {
    show([row()], { changeInFlight: true });

    expect(button(/change #prio1/i).disabled).toBe(true);
    expect(screen.getByText(/one change at a time/i)).toBeTruthy();
  });

  it('starts a change with the row and its own spelling suggested', () => {
    const onStartChange = vi.fn();

    show([row()], { onStartChange });

    button(/change #prio1/i).click();

    expect(onStartChange).toHaveBeenCalledWith([expect.objectContaining({ key: 'PRIO1' })], 'Prio1');
  });

  /** Combining suggests the name most tasks already use, so the busiest tag wins by default. */
  it('suggests the busiest spelling when combining', () => {
    const onStartChange = vi.fn();

    show([row({ taskCount: 2 }), row({ key: 'KUNDE', canonicalSpelling: 'kunde', taskCount: 9 })], {
      onStartChange,
    });

    button(/combine 2 into one/i).click();

    expect(onStartChange).toHaveBeenCalledWith(expect.any(Array), 'kunde');
  });

  /** Asserted on the DOM property rather than through a matcher library: one dependency fewer. */
  function button(name: RegExp): HTMLButtonElement {
    return screen.getByRole('button', { name }) as HTMLButtonElement;
  }

  function show(
    rows: HashtagInventoryRow[],
    options: { changeInFlight?: boolean; onStartChange?: () => void } = {},
  ) {
    render(
      <FluentProvider theme={cloudwerkLightTheme}>
        <HashtagDetailPanel
          rows={rows}
          onStartChange={options.onStartChange ?? (() => {})}
          changeInFlight={options.changeInFlight ?? false}
        />
      </FluentProvider>,
    );
  }
});

function row(overrides: Partial<HashtagInventoryRow> = {}): HashtagInventoryRow {
  return {
    key: 'PRIO1',
    canonicalSpelling: 'Prio1',
    spellings: ['Prio1'],
    taskCount: 2,
    listCount: 1,
    lastUsedAt: '2026-08-12T09:00:00Z',
    hasMultipleSpellings: false,
    hasNearDuplicates: false,
    isStale: false,
    ...overrides,
  };
}
