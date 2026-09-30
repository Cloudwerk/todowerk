import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider } from '@fluentui/react-components';
import { describe, expect, it, vi } from 'vitest';
import { ChangeConfirmDialog } from './ChangeConfirmDialog';
import { cloudwerkLightTheme } from '../theme';
import type { ChangePreview } from '../api/types';

/**
 * The one dialog, doing the fourth kind and saying what a Change would do to the Marker Rules it
 * touches. No second dialog and no second hook: what differs is that an Apply has
 * no spelling to type and a Merge may have a question to ask.
 */
describe('ChangeConfirmDialog, markers', () => {
  it('asks for no spelling when it is applying markers', () => {
    show({
      markerScope: { action: 'apply', ruleKey: null, label: 'Apply all markers' },
      preview: preview({ kind: 'ApplyMarkers', targetSpelling: '' }),
    });

    expect(screen.queryByRole('textbox', { name: /new spelling/i })).toBeNull();
    expect(screen.getByText('Apply all markers')).toBeTruthy();
    expect(screen.getByRole('button', { name: /^mark /i })).toBeTruthy();
  });

  it('names the one rule it was started from', () => {
    show({
      markerScope: { action: 'apply', ruleKey: 'BREAD', label: 'Apply 🍞 to #bread' },
      preview: preview({ kind: 'ApplyMarkers', targetSpelling: '' }),
    });

    expect(screen.getByText('Apply 🍞 to #bread')).toBeTruthy();
  });

  /**
   * Named before anybody confirms rather than discovered in the outcome: how close a title already
   * sits to To Do's limit is not the user's to guess.
   */
  it('names the tasks it already knows it will leave alone', () => {
    show({
      markerScope: { action: 'apply', ruleKey: null, label: 'Apply all markers' },
      preview: preview({
        kind: 'ApplyMarkers',
        skips: [
          {
            taskListId: 'l1',
            listDisplayName: 'Arbeit',
            currentTitle: 'Ein sehr langer Titel #bread',
            reason: 'The new title would be longer than Microsoft To Do stores, so this task was left alone.',
          },
        ],
      }),
    });

    expect(screen.getByText('1 task will be left alone.')).toBeTruthy();
    expect(screen.getByText(/longer than Microsoft To Do stores/)).toBeTruthy();
  });

  it('says that a rename carries its marker rule along', () => {
    show({
      preview: preview({
        kind: 'Rename',
        markerRules: {
          sourceRules: [{ key: 'BREAD', spelling: 'bread', marker: '🍞' }],
          targetRule: null,
          survivingMarker: '🍞',
          requiresSurvivorChoice: false,
        },
      }),
    });

    expect(screen.getByText(/The marker rule/)).toBeTruthy();
  });

  /**
   * The one question a Merge cannot answer for itself (ADR-0014), so the button is held until it
   * is answered — the same way an unconfirmed Merge is.
   */
  it('holds the button until the surviving marker is chosen', () => {
    const onChoose = vi.fn();

    show({
      preview: preview({
        kind: 'Merge',
        requiresMergeConfirmation: true,
        markerRules: {
          sourceRules: [
            { key: 'BREAD', spelling: 'bread', marker: '🍞' },
            { key: 'COFFEE', spelling: 'coffee', marker: '☕' },
          ],
          targetRule: null,
          survivingMarker: null,
          requiresSurvivorChoice: true,
        },
      }),
      onChooseSurvivingMarker: onChoose,
    });

    expect(confirmButton().disabled).toBe(true);

    fireEvent.click(screen.getByRole('button', { name: /☕/ }));

    expect(onChoose).toHaveBeenCalledWith('☕');
  });

  it('lets the change through once a marker has been chosen', () => {
    show({
      survivingMarker: '☕',
      preview: preview({
        kind: 'Merge',
        requiresMergeConfirmation: true,
        markerRules: {
          sourceRules: [
            { key: 'BREAD', spelling: 'bread', marker: '🍞' },
            { key: 'COFFEE', spelling: 'coffee', marker: '☕' },
          ],
          targetRule: null,
          survivingMarker: null,
          requiresSurvivorChoice: true,
        },
      }),
    });

    expect(confirmButton().disabled).toBe(false);
  });

  function confirmButton(): HTMLButtonElement {
    return screen.getByRole('button', { name: /^(change|merge|mark) /i }) as HTMLButtonElement;
  }

  function show(overrides: Partial<Parameters<typeof ChangeConfirmDialog>[0]> = {}) {
    render(
      <FluentProvider theme={cloudwerkLightTheme}>
        <ChangeConfirmDialog
          open
          sources={['bread']}
          target="loaf"
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
    sourceKeys: ['BREAD'],
    targetSpelling: 'loaf',
    items: [
      {
        taskListId: 'l1',
        listDisplayName: 'Arbeit',
        currentTitle: 'Brot kaufen #bread',
        newTitle: 'Brot kaufen #loaf',
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
