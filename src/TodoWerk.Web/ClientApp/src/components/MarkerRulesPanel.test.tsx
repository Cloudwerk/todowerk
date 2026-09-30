import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider } from '@fluentui/react-components';
import { describe, expect, it, vi } from 'vitest';
import { MarkerRulesPanel } from './MarkerRulesPanel';
import { cloudwerkLightTheme } from '../theme';
import type { AbandonedMarker, MarkerRule } from '../api/types';

/**
 * The rules view: the order the block is written in, how far each rule has got, and
 * the two ways to apply them. Up and down buttons rather than drag, so the order is changeable
 * with a keyboard and inside the Teams tab.
 */
describe('MarkerRulesPanel', () => {
  it('shows the rules in the order the server returned, not sorted again here', () => {
    show({
      rules: [
        rule({ id: 'r1', spelling: 'coffee', marker: '☕', position: 10 }),
        rule({ id: 'r2', spelling: 'bread', marker: '🍞', position: 20 }),
      ],
    });

    const tags = screen.getAllByText(/^#(coffee|bread)$/);

    expect(tags.map((tag) => tag.textContent)).toEqual(['#coffee', '#bread']);
  });

  it('shows how far each rule has been applied', () => {
    show({ rules: [rule({ taggedTaskCount: 7, markedTaskCount: 3 })] });

    expect(screen.getByText('3 of 7 tagged tasks')).toBeTruthy();
  });

  /** A rule outlives its Occurrences, and says so rather than showing a bare nought. */
  it('says so when a rule has no tagged tasks left', () => {
    show({ rules: [rule({ taggedTaskCount: 0, markedTaskCount: 0 })] });

    expect(screen.getByText('no tagged tasks')).toBeTruthy();
  });

  it('cannot move the first rule up or the last one down', () => {
    show({
      rules: [rule({ id: 'r1', spelling: 'bread' }), rule({ id: 'r2', spelling: 'coffee', marker: '☕' })],
    });

    expect(button('Move #bread up').disabled).toBe(true);
    expect(button('Move #coffee down').disabled).toBe(true);
    expect(button('Move #coffee up').disabled).toBe(false);
  });

  it('moves a rule in the direction the button says', () => {
    const onMove = vi.fn();

    show({
      rules: [rule({ id: 'r1', spelling: 'bread' }), rule({ id: 'r2', spelling: 'coffee', marker: '☕' })],
      onMove,
    });

    fireEvent.click(button('Move #coffee up'));

    expect(onMove).toHaveBeenCalledWith('r2', 'Up');
  });

  /** Two entry points, one write: all the rules, or the one this row is about. */
  it('offers both ways to apply', () => {
    const onApplyAll = vi.fn();
    const onApplyOne = vi.fn();
    const only = rule();

    show({ rules: [only], onApplyAll, onApplyOne });

    fireEvent.click(screen.getByRole('button', { name: /apply all markers/i }));
    fireEvent.click(button('Apply the marker for #bread'));

    expect(onApplyAll).toHaveBeenCalledTimes(1);
    expect(onApplyOne).toHaveBeenCalledWith(only);
  });

  /** One Change at a time (ADR-0006), whichever shape it has. */
  it('closes both entry points while a change is in flight', () => {
    show({ rules: [rule()], changeInFlight: true });

    expect((screen.getByRole('button', { name: /apply all markers/i }) as HTMLButtonElement).disabled).toBe(true);
    expect(button('Apply the marker for #bread').disabled).toBe(true);
  });

  /**
   * Everything else here that destroys something asks first, and a trash icon beside four other
   * icons is one wrong click from taking a rule and its place in the order with it.
   */
  it('asks before a rule is deleted, and deletes only on the answer', () => {
    const onRemove = vi.fn();

    show({ rules: [rule()], onRemove });

    fireEvent.click(button('Delete the marker rule for #bread'));

    expect(onRemove).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /keep the rule/i })).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: /^delete rule$/i }));

    expect(onRemove).toHaveBeenCalledWith('r1');
  });

  /**
   * The stale count and the button beside it, which ADR-0014 made one thing: the count was held
   * back until there was an action to put next to it, because "a count without the action only
   * nags".
   */
  it('shows how much of a rule is stale, and offers to clear it', () => {
    const removeOne = vi.fn();

    show({ rules: [rule({ staleTaskCount: 2 })], onRemoveOne: removeOne });

    expect(screen.getByText('2 stale')).toBeTruthy();

    fireEvent.click(button(/Remove stale 🍞 markers for #bread/i));

    expect(removeOne).toHaveBeenCalledTimes(1);
  });

  it('says nothing about staleness when there is none, and offers nothing to clear', () => {
    show({ rules: [rule({ staleTaskCount: 0 })] });

    // The badge, not the whole-list button beside it, which says "stale" whatever a rule holds.
    expect(screen.queryByText(/^\d+ stale$/)).toBeNull();
    expect(button(/Remove stale 🍞 markers for #bread/i).disabled).toBe(true);
  });

  /** Nothing stale anywhere, so the whole-list button has nothing to do either. */
  it('closes the whole-list removal when nothing is stale', () => {
    show({ rules: [rule({ staleTaskCount: 0 })], abandoned: [] });

    expect(button(/^Remove stale markers…$/).disabled).toBe(true);
  });

  /** And a marker a deleted rule left behind is enough on its own: it has no rule row to count on. */
  it('opens the whole-list removal for a marker whose rule is gone', () => {
    show({ rules: [], abandoned: [abandoned({ staleTaskCount: 1 })] });

    expect(button(/^Remove stale markers…$/).disabled).toBe(false);
  });

  /**
   * The markers of deleted rules — the only place in the product they are visible at all. Without
   * this they sit at the front of somebody's tasks with no row anywhere offering to take them out.
   */
  it('lists the markers left behind by deleted rules, with a way to clear each', () => {
    const removeAbandoned = vi.fn();

    show({
      rules: [],
      abandoned: [abandoned({ marker: '🥐', staleTaskCount: 4 })],
      onRemoveAbandoned: removeAbandoned,
    });

    expect(screen.getByText(/on 4 tasks, with no rule/i)).toBeTruthy();

    fireEvent.click(button(/Remove the 🥐 markers left behind/i));

    expect(removeAbandoned).toHaveBeenCalledTimes(1);
  });

  it('says nothing about markers left behind when there are none', () => {
    show({ rules: [rule()], abandoned: [] });

    expect(screen.queryByText(/left behind/i)).toBeNull();
  });

  /** One Change at a time (ADR-0006), whichever direction it goes in. */
  it('closes the removal entry points while a change is in flight', () => {
    show({ rules: [rule({ staleTaskCount: 2 })], changeInFlight: true });

    expect(button(/^Remove stale markers…$/).disabled).toBe(true);
    expect(button(/Remove stale 🍞 markers for #bread/i).disabled).toBe(true);
  });

  /**
   * The badge is about work still to do, not about what the rule remembers. A swap that has reached
   * every task would otherwise go on offering to make a change that would change nothing.
   */
  it('shows the pending swap only while some task still carries the old emoji', () => {
    show({ rules: [rule({ retiredMarker: '🥖', retiredTaskCount: 3 })] });

    expect(screen.getByText('was 🥖')).toBeTruthy();
  });

  it('says nothing about a swap that has already reached everywhere', () => {
    show({ rules: [rule({ retiredMarker: '🥖', retiredTaskCount: 0 })] });

    expect(screen.queryByText('was 🥖')).toBeNull();
  });

  it('points somebody with no rules at where to make one', () => {
    show({ rules: [] });

    expect(screen.getByText(/no markers yet/i)).toBeTruthy();
  });

  function button(name: string | RegExp): HTMLButtonElement {
    return screen.getByRole('button', { name }) as HTMLButtonElement;
  }

  function show(overrides: Partial<Parameters<typeof MarkerRulesPanel>[0]> = {}) {
    render(
      <FluentProvider theme={cloudwerkLightTheme}>
        <MarkerRulesPanel
          rules={[rule()]}
          abandoned={[]}
          loading={false}
          busy={false}
          notice={undefined}
          onDismissNotice={() => {}}
          onEdit={() => {}}
          onMove={() => {}}
          onRemove={() => {}}
          onApplyAll={() => {}}
          onApplyOne={() => {}}
          onRemoveAll={() => {}}
          onRemoveOne={() => {}}
          onRemoveAbandoned={() => {}}
          changeInFlight={false}
          {...overrides}
        />
      </FluentProvider>,
    );
  }
});

function abandoned(overrides: Partial<AbandonedMarker> = {}): AbandonedMarker {
  return { marker: '🥐', staleTaskCount: 1, ...overrides };
}

function rule(overrides: Partial<MarkerRule> = {}): MarkerRule {
  return {
    id: 'r1',
    key: 'BREAD',
    spelling: 'bread',
    marker: '🍞',
    retiredMarker: null,
    position: 10,
    taggedTaskCount: 3,
    staleTaskCount: 0,
    retiredTaskCount: 0,
    markedTaskCount: 1,
    ...overrides,
  };
}
