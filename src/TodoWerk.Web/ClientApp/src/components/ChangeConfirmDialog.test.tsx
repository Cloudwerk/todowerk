import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { FluentProvider } from '@fluentui/react-components';
import { ChangeConfirmDialog } from './ChangeConfirmDialog';
import { cloudwerkLightTheme } from '../theme';
import type { ChangePreview } from '../api/types';

/**
 * The moment the product's trust rests on: up to two hundred titles, and in each of them the one
 * token that changes. What is on screen has to make that token findable, say at once when the new
 * spelling cannot be one, and put the count on the button that commits to it.
 */
describe('ChangeConfirmDialog', () => {
  it('marks the Hashtag that changes in each title, and strikes the one it replaces', () => {
    show({ preview: preview() });

    const marks = [...document.querySelectorAll('mark')].map((mark) => mark.textContent);
    const struck = [...document.querySelectorAll('s')].map((s) => s.textContent);

    expect(marks).toEqual(['#Priority1']);
    expect(struck).toEqual(['#Prio1']);
  });

  /** A casing clean-up leaves the `#Work` beside the `#work` alone, and the dialog must say so. */
  it('leaves an occurrence the Change does not touch plain', () => {
    show({
      preview: preview({
        targetSpelling: 'Work',
        items: [{ taskListId: 'l1', listDisplayName: 'Arbeit', currentTitle: 'Do #Work then #work', newTitle: 'Do #Work then #Work' }],
      }),
    });

    expect([...document.querySelectorAll('s')].map((s) => s.textContent)).toEqual(['#work']);
    expect([...document.querySelectorAll('mark')].map((mark) => mark.textContent)).toEqual(['#Work']);
  });

  it('answers a space in the new spelling at once, without waiting for the server', () => {
    show({ target: 'prio 1', preview: undefined });

    expect(screen.getByText('No spaces.')).toBeTruthy();
    expect(confirmButton().disabled).toBe(true);
  });

  it('lets the box speak for itself rather than beside the server refusal', () => {
    show({ target: '#prio1', preview: undefined, error: 'That is not a name a hashtag can have.' });

    expect(screen.getByText('Leave out the #.')).toBeTruthy();
    expect(screen.queryByText('That is not a name a hashtag can have.')).toBeNull();
  });

  it('hands every keystroke on', () => {
    const onTargetChange = vi.fn();

    show({ onTargetChange });

    fireEvent.change(screen.getByRole('textbox', { name: /new spelling/i }), { target: { value: 'Prior' } });

    expect(onTargetChange).toHaveBeenCalledWith('Prior');
  });

  it.each([
    [1, 'Rename', /^change 1 task$/i],
    [37, 'Rename', /^change 37 tasks$/i],
    [37, 'Merge', /^merge 37 tasks$/i],
  ] as const)('puts the count of %s on the button for a %s', (taskCount, kind, label) => {
    show({ preview: preview({ taskCount, kind, requiresMergeConfirmation: kind === 'Merge' }) });

    expect(confirmButton().textContent).toMatch(label);
  });

  it('keeps a plain label while there is no preview to count', () => {
    show({ preview: undefined, previewing: true });

    expect(confirmButton().textContent).toMatch(/^change them$/i);
    expect(confirmButton().disabled).toBe(true);
  });

  function confirmButton(): HTMLButtonElement {
    return screen.getByRole('button', { name: /^(change|merge) /i }) as HTMLButtonElement;
  }

  function show(
    overrides: Partial<Parameters<typeof ChangeConfirmDialog>[0]> = {},
  ) {
    render(
      <FluentProvider theme={cloudwerkLightTheme}>
        <ChangeConfirmDialog
          open
          sources={['Prio1']}
          target="Priority1"
          onTargetChange={() => {}}
          preview={preview()}
          previewing={false}
          error={undefined}
          confirming={false}
          onConfirm={() => {}}
          onDismiss={() => {}}
          markerScope={undefined}
          survivingMarker={undefined}
          onChooseSurvivingMarker={() => {}}
          {...overrides}
        />
      </FluentProvider>,
    );
  }
});

function preview(overrides: Partial<ChangePreview> = {}): ChangePreview {
  return {
    kind: 'Rename',
    sourceKeys: ['PRIO1'],
    targetSpelling: 'Priority1',
    items: [
      {
        taskListId: 'l1',
        listDisplayName: 'Arbeit',
        currentTitle: 'Angebot #Prio1 senden',
        newTitle: 'Angebot #Priority1 senden',
      },
    ],
    taskCount: 1,
    requiresMergeConfirmation: false,
    excludedLists: [],
    skips: [],
    markerRules: { sourceRules: [], targetRule: null, survivingMarker: null, requiresSurvivorChoice: false },
    ...overrides,
  };
}
