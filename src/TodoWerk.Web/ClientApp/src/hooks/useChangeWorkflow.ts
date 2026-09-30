import { useCallback, useEffect, useRef, useState } from 'react';
import {
  cancelChange,
  confirmChange,
  confirmMarkerApply,
  confirmMarkerRemoval,
  getChangeQueue,
  previewChange,
  previewMarkerApply,
  previewMarkerRemoval,
  undoChange,
} from '../api/client';
import { ApiError } from '../api/http';
import { describeError as describe } from '../api/describeError';
import type { Change, ChangePreview, HashtagInventoryRow } from '../api/types';

/** How often the queue is re-read while a Change is pending or running. */
const QUEUE_POLL_MS = 2000;

/** How long typing pauses before the preview is recomputed. */
const PREVIEW_DEBOUNCE_MS = 400;

export interface ChangeWorkflow {
  /** The Change that is pending or running, or null. There is at most one per user. */
  active: Change | null;
  history: Change[];
  /** True while a Change is queued or running: the entry points close until it finishes. */
  inFlight: boolean;
  /** True while a cancel or an undo is being sent, so a button cannot double-fire. */
  busy: boolean;
  dialog: ChangeDialogState;
  /** Something the whole screen should say, not something the dialog owns. */
  notice: string | undefined;
  dismissNotice: () => void;
  start: (sources: HashtagInventoryRow[], suggestedTarget: string) => void;
  /** Opens the same dialog for an Apply Markers: every rule, or the one named. */
  startMarkers: (scope: MarkerScope) => void;
  setTarget: (target: string) => void;
  /** Which marker the user picked to survive a Merge that folds two marked hashtags together. */
  chooseSurvivingMarker: (marker: string) => void;
  confirm: () => void;
  dismiss: () => void;
  cancel: (changeId: string) => void;
  undo: (changeId: string) => void;
  refresh: () => void;
}

export interface ChangeDialogState {
  open: boolean;
  /** Canonical spellings of the selected rows — what the dialog's title reads back. */
  sources: string[];
  target: string;
  preview: ChangePreview | undefined;
  previewing: boolean;
  /** The server's refusal, in its own words. */
  error: string | undefined;
  confirming: boolean;
  /**
   * Set when this dialog is applying Marker Rules rather than changing a Hashtag. The fourth kind
   * has no target spelling, so the box is absent rather than empty.
   */
  markerScope: MarkerScope | undefined;
  /** The marker the user picked to survive a Merge, or undefined while they have not. */
  survivingMarker: string | undefined;
}

/**
 * What a marker change covers, and which direction it goes in — and what to call it on screen.
 *
 * The two are scoped by different things, which is why this is a union rather than a flag. An Apply
 * picks the tasks it covers by hashtag; a Remove cannot, because a stale marker is stale precisely
 * because the hashtag has gone — and one a deleted rule left behind has no hashtag at all. So a
 * Remove is scoped by the emoji itself (ADR-0014).
 */
export type MarkerScope =
  | { action: 'apply'; ruleKey: string | null; label: string }
  | { action: 'remove'; marker: string | null; label: string };

const NoDialog: ChangeDialogState = {
  open: false,
  sources: [],
  target: '',
  preview: undefined,
  previewing: false,
  error: undefined,
  confirming: false,
  markerScope: undefined,
  survivingMarker: undefined,
};

/**
 * Everything the Workbench does with Changes: the queue it watches, the dialog it confirms
 * through, and the cancel and undo it offers afterwards.
 *
 * A hook rather than state scattered through the page, because this is the first place the client
 * holds anything the server has not confirmed yet — a target being typed, a preview that is one
 * keystroke out of date, a Change queued but not started. That is the state worth testing, and it
 * is testable here without rendering a grid.
 */
export function useChangeWorkflow(onCompleted: () => void): ChangeWorkflow {
  const [active, setActive] = useState<Change | null>(null);
  const [history, setHistory] = useState<Change[]>([]);
  const [dialog, setDialog] = useState<ChangeDialogState>(NoDialog);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string>();

  // The keys are what the server matches on; the spellings are only what the dialog reads back.
  const sourceKeys = useRef<string[]>([]);

  // Which preview was asked for last. Two dialogs opened in quick succession can share a target,
  // and reconstructing "is this still the same question" from its parts would miss the sources.
  const latestPreview = useRef(0);

  // Which Change was running last time the queue was read. Used to fire `onCompleted` once, on
  // the edge, rather than on every poll while nothing is happening.
  const wasRunning = useRef(false);

  const completed = useRef(onCompleted);
  completed.current = onCompleted;

  const refresh = useCallback(async () => {
    try {
      const queue = await getChangeQueue();

      setActive(queue.active);
      setHistory(queue.history);

      if (wasRunning.current && queue.active === null) {
        completed.current();
      }

      wasRunning.current = queue.active !== null;
    } catch (error) {
      // A queue that could not be read is not worth a banner: it is polled, the next one will
      // say the same thing or better, and the Change is running on the server either way.
      if (!(error instanceof ApiError)) return;
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    if (active === null) return undefined;

    const timer = window.setInterval(() => void refresh(), QUEUE_POLL_MS);

    return () => window.clearInterval(timer);
  }, [active, refresh]);

  // The preview follows the target, one pause behind. Every keystroke would otherwise be its own
  // plan over every task carrying the tag.
  useEffect(() => {
    if (!dialog.open) return undefined;

    const keys = sourceKeys.current;
    const target = dialog.target;
    const scope = dialog.markerScope;

    const timer = window.setTimeout(() => {
      setDialog((current) => ({ ...current, previewing: true }));

      const token = ++latestPreview.current;
      const asked = !scope
        ? previewChange(keys, target)
        : scope.action === 'remove'
          ? previewMarkerRemoval(scope.marker)
          : previewMarkerApply(scope.ruleKey);

      // Discarded if what was asked has moved on while this was in flight: showing a preview of a
      // question nobody is asking any more is worse than showing none.
      const stillAsked = (current: ChangeDialogState) => current.open && token === latestPreview.current;

      asked.then(
        (preview) => {
          setDialog((current) =>
            stillAsked(current) ? { ...current, preview, previewing: false, error: undefined } : current,
          );
        },
        (error: unknown) => {
          setDialog((current) =>
            stillAsked(current)
              ? { ...current, preview: undefined, previewing: false, error: describe(error) }
              : current,
          );
        },
      );
    }, PREVIEW_DEBOUNCE_MS);

    return () => window.clearTimeout(timer);
  }, [dialog.open, dialog.target, dialog.markerScope]);

  const start = useCallback((sources: HashtagInventoryRow[], suggestedTarget: string) => {
    sourceKeys.current = sources.map((row) => row.key);

    setNotice(undefined);
    setDialog({
      ...NoDialog,
      open: true,
      sources: sources.map((row) => row.canonicalSpelling),
      target: suggestedTarget,
    });
  }, []);

  /**
   * The other entry point to the same dialog, in either direction. No sources and no target: a
   * marker change is scoped by which markers it covers, and what it writes is the block (ADR-0014).
   */
  const startMarkers = useCallback((scope: MarkerScope) => {
    sourceKeys.current = [];

    setNotice(undefined);
    setDialog({ ...NoDialog, open: true, markerScope: scope });
  }, []);

  // A new target is a new question, and the survivor somebody chose was an answer to the old one.
  const setTarget = useCallback((target: string) => {
    setDialog((current) => ({
      ...current,
      target,
      preview: undefined,
      error: undefined,
      survivingMarker: undefined,
    }));
  }, []);

  // Cleared alongside nothing else: the choice is about the preview on screen, and a preview that
  // is replaced takes its question with it.
  const chooseSurvivingMarker = useCallback((marker: string) => {
    setDialog((current) => ({ ...current, survivingMarker: marker, error: undefined }));
  }, []);

  const dismiss = useCallback(() => {
    latestPreview.current++;
    setDialog(NoDialog);
  }, []);

  const confirm = useCallback(async () => {
    const preview = dialog.preview;
    const scope = dialog.markerScope;
    const survivor = dialog.survivingMarker;

    if (!preview) return;

    setDialog((current) => ({ ...current, confirming: true, error: undefined }));

    try {
      // What the user confirmed is what the preview showed them, merge warning included. Sending
      // the preview's verdict rather than a constant is what makes a rename that became a merge
      // since — because a scan or another change put the target in the index — get refused rather
      // than waved through.
      const queued = scope
        ? scope.action === 'remove'
          ? await confirmMarkerRemoval(scope.marker)
          : await confirmMarkerApply(scope.ruleKey)
        : await confirmChange(
            sourceKeys.current,
            preview.targetSpelling,
            preview.requiresMergeConfirmation,
            survivor ?? null,
          );

      setActive(queued);
      wasRunning.current = true;
      setDialog(NoDialog);
    } catch (error) {
      setDialog((current) => ({ ...current, confirming: false, error: describe(error) }));
    }
  }, [dialog.preview, dialog.markerScope, dialog.survivingMarker]);

  const cancel = useCallback(
    async (changeId: string) => {
      setBusy(true);

      try {
        setActive(await cancelChange(changeId));
      } catch (error) {
        setNotice(describe(error));
      } finally {
        setBusy(false);
        void refresh();
      }
    },
    [refresh],
  );

  const undo = useCallback(
    async (changeId: string) => {
      setBusy(true);
      setNotice(undefined);

      try {
        setActive(await undoChange(changeId));
        wasRunning.current = true;
      } catch (error) {
        setNotice(describe(error));
      } finally {
        setBusy(false);
        void refresh();
      }
    },
    [refresh],
  );

  return {
    active,
    history,
    inFlight: active !== null,
    busy,
    dialog,
    notice,
    dismissNotice: useCallback(() => setNotice(undefined), []),
    start,
    startMarkers,
    setTarget,
    chooseSurvivingMarker,
    confirm: useCallback(() => void confirm(), [confirm]),
    dismiss,
    cancel: useCallback((changeId: string) => void cancel(changeId), [cancel]),
    undo: useCallback((changeId: string) => void undo(changeId), [undo]),
    refresh: useCallback(() => void refresh(), [refresh]),
  };
}

