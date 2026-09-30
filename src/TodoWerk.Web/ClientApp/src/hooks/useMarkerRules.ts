import { useCallback, useEffect, useRef, useState } from 'react';
import { createMarkerRule, deleteMarkerRule, getMarkerRules, updateMarkerRule } from '../api/client';
import { describeError as describe } from '../api/describeError';
import type { AbandonedMarker, MarkerRule, MarkerRuleMove } from '../api/types';

export interface MarkerRuleWorkflow {
  /** In the order the server returns, which is the order of the block. */
  rules: MarkerRule[];
  /**
   * Markers left behind by rules the person deleted, still at the front of at least one task. The
   * only place they are visible at all — a deleted rule is listed nowhere.
   */
  abandoned: AbandonedMarker[];
  loading: boolean;
  /** True while a write is in flight, so a button cannot double-fire. */
  busy: boolean;
  /** The server's refusal about the whole list, in its own words. */
  notice: string | undefined;
  dismissNotice: () => void;
  /** The hashtag whose marker is being chosen, or null. */
  editing: MarkerRuleTarget | null;
  /** The server's refusal about the dialog's own marker — "🍞 is already the marker for #bread". */
  editError: string | undefined;
  edit: (target: MarkerRuleTarget) => void;
  cancelEdit: () => void;
  save: (marker: string) => void;
  move: (ruleId: string, direction: MarkerRuleMove) => void;
  remove: (ruleId: string) => void;
  refresh: () => void;
}

/** What the Set Marker dialog is about: a hashtag, and the rule it already has if it has one. */
export interface MarkerRuleTarget {
  spelling: string;
  rule: MarkerRule | undefined;
}

/**
 * Everything the Workbench does with Marker Rules: the list it shows, the dialog it writes one
 * through, and the reordering and deleting beside it.
 *
 * A hook of its own rather than more state in `useChangeWorkflow`, because none of this is a
 * Change: a rule declares and writes no title (ADR-0014). Applying rules is a Change, and goes
 * through the workflow that already exists for the other three.
 */
export function useMarkerRules(): MarkerRuleWorkflow {
  const [rules, setRules] = useState<MarkerRule[]>([]);
  const [abandoned, setAbandoned] = useState<AbandonedMarker[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string>();
  const [editing, setEditing] = useState<MarkerRuleTarget | null>(null);
  const [editError, setEditError] = useState<string>();

  const cancelled = useRef(false);

  // Which read was asked for last. A completing Change and a reorder can both ask for the list
  // within a second of each other, and the older answer must not overwrite the newer one.
  const latest = useRef(0);

  // The read in flight, so it can be abandoned rather than merely ignored. The token above already
  // stops a stale answer reaching the screen; this stops the request itself outliving the screen,
  // which is what the signal `getMarkerRules(signal?)` takes is for.
  const inFlight = useRef<AbortController | null>(null);

  const refresh = useCallback(async () => {
    const asked = ++latest.current;
    const current = () => !cancelled.current && asked === latest.current;

    inFlight.current?.abort();

    const controller = new AbortController();
    inFlight.current = controller;

    try {
      const answer = await getMarkerRules(controller.signal);

      if (current()) {
        setRules(answer.rules);
        setAbandoned(answer.abandoned);
      }
    } catch (error) {
      // A superseded or unmounted read fails `current()` anyway, so an abort never reaches the
      // screen as a notice — the guard is the same one a stale answer meets.
      if (current()) setNotice(describe(error));
    } finally {
      if (current()) setLoading(false);
    }
  }, []);

  useEffect(() => {
    cancelled.current = false;
    void refresh();

    return () => {
      cancelled.current = true;
      inFlight.current?.abort();
      inFlight.current = null;
    };
  }, [refresh]);

  const save = useCallback(
    async (marker: string) => {
      const target = editing;

      if (target === null) return;

      setBusy(true);
      setEditError(undefined);

      try {
        if (target.rule) {
          await updateMarkerRule(target.rule.id, { marker });
        } else {
          await createMarkerRule(target.spelling, marker);
        }

        setEditing(null);
        await refresh();
      } catch (error) {
        // Kept in the dialog rather than raised over the page: the refusal names the hashtag that
        // holds the emoji, and it is answered by choosing another one right here.
        setEditError(describe(error));
      } finally {
        setBusy(false);
      }
    },
    [editing, refresh],
  );

  const move = useCallback(
    async (ruleId: string, direction: MarkerRuleMove) => {
      setBusy(true);
      setNotice(undefined);

      try {
        await updateMarkerRule(ruleId, { move: direction });
        await refresh();
      } catch (error) {
        setNotice(describe(error));
      } finally {
        setBusy(false);
      }
    },
    [refresh],
  );

  const remove = useCallback(
    async (ruleId: string) => {
      setBusy(true);
      setNotice(undefined);

      try {
        await deleteMarkerRule(ruleId);
        await refresh();
      } catch (error) {
        setNotice(describe(error));
      } finally {
        setBusy(false);
      }
    },
    [refresh],
  );

  return {
    rules,
    abandoned,
    loading,
    busy,
    notice,
    dismissNotice: useCallback(() => setNotice(undefined), []),
    editing,
    editError,
    edit: useCallback((target: MarkerRuleTarget) => {
      setEditError(undefined);
      setEditing(target);
    }, []),
    cancelEdit: useCallback(() => {
      setEditError(undefined);
      setEditing(null);
    }, []),
    save: useCallback((marker: string) => void save(marker), [save]),
    move: useCallback((ruleId: string, direction: MarkerRuleMove) => void move(ruleId, direction), [move]),
    remove: useCallback((ruleId: string) => void remove(ruleId), [remove]),
    refresh: useCallback(() => void refresh(), [refresh]),
  };
}

