import {
  Badge,
  Body1,
  Button,
  Caption1,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  ProgressBar,
  Spinner,
  Subtitle2,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { signInUrl } from '../api/client';
import { useEntraRoundTrip } from '../host';
import type { Change, ChangeState } from '../api/types';
import { formatDate } from './formatDate';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  running: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
  },
  headline: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  spacer: {
    marginInlineStart: 'auto',
  },
  history: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
    margin: 0,
    padding: 0,
    listStyle: 'none',
  },
  entry: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  tag: {
    fontFamily: tokens.fontFamilyMonospace,
  },
  empty: {
    color: tokens.colorNeutralForeground3,
  },
});

/**
 * The running Change, with per-task progress and Cancel — or nothing, when none is running.
 *
 * Its own panel rather than half of one: it sits above the inventory table, because the person
 * who started it wants to watch it finish and be able to stop it, while the history sits below,
 * because the table is the product (CONTEXT.md).
 *
 * @param busy True while a cancel or an undo is in flight, so the button stops rather than double-fires.
 */
export function ActiveChangePanel({
  change,
  onCancel,
  busy,
}: {
  change: Change | null;
  onCancel: (changeId: string) => void;
  busy: boolean;
}) {
  const styles = useStyles();

  if (!change) {
    return null;
  }

  const done = change.writtenCount + change.skippedCount + change.failedCount;

  return (
    <section className={styles.root} aria-label="Running change">
      <div className={styles.running}>
        <div className={styles.headline}>
          <Spinner size="tiny" />
          <Body1>
            <Description change={change} styles={styles} />
          </Body1>
          <Caption1>
            {change.state === 'Pending'
              ? 'Queued — starting shortly.'
              : `${done} of ${change.plannedTaskCount} tasks done.`}
          </Caption1>

          <div className={styles.spacer}>
            <Button
              appearance="subtle"
              disabled={busy || change.cancelRequested}
              onClick={() => onCancel(change.id)}
            >
              {change.cancelRequested ? 'Stopping…' : 'Cancel'}
            </Button>
          </div>
        </div>

        <ProgressBar
          value={done}
          max={Math.max(1, change.plannedTaskCount)}
          aria-label={`${done} of ${change.plannedTaskCount} tasks changed`}
        />

        {change.cancelRequested && (
          <Caption1>Stopping after the task it is on. What it has already changed stays, and can be undone.</Caption1>
        )}
      </div>
    </section>
  );
}

/**
 * The history, with Undo — as CONTEXT.md describes the Workbench — or nothing, when there is none
 * yet.
 *
 * @param busy True while a cancel or an undo is in flight, so the buttons stop rather than double-fire.
 */
export function ChangeHistoryPanel({
  history,
  onUndo,
  busy,
}: {
  history: Change[];
  onUndo: (changeId: string) => void;
  busy: boolean;
}) {
  const styles = useStyles();

  if (history.length === 0) {
    return null;
  }

  return (
    <section className={styles.root} aria-label="Recent changes">
      <Subtitle2>Recent changes</Subtitle2>
      <Caption1>Kept for 30 days, which is also how long each one can be undone.</Caption1>
      <ul className={styles.history}>
        {history.map((change) => (
          <li key={change.id} className={styles.entry}>
            <Text>
              <Description change={change} styles={styles} />
            </Text>
            <Outcome change={change} />
            <Caption1 className={styles.empty}>{formatDate(change.completedAt ?? change.requestedAt)}</Caption1>
            {change.canUndo && (
              <Button
                className={styles.spacer}
                appearance="subtle"
                disabled={busy}
                onClick={() => onUndo(change.id)}
              >
                Undo
              </Button>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}

/** What the Change does, in the words CONTEXT.md uses for its four shapes. */
function Description({ change, styles }: { change: Change; styles: Record<string, string> }) {
  const target = (
    <span className={styles.tag}>#{change.targetSpelling}</span>
  );

  // The two marker kinds first, because neither has a target spelling for the sentences below to
  // name — what they carry is on the row itself (ADR-0014).
  if (change.kind === 'ApplyMarkers') {
    const scope = markerScope(change, styles);

    return change.undoOfChangeId ? <>Undoing {scope}</> : <>Applying {scope}</>;
  }

  if (change.kind === 'RemoveMarkers') {
    const scope = removalScope(change);

    return change.undoOfChangeId ? <>Putting back {scope}</> : <>Removing {scope}</>;
  }

  if (change.undoOfChangeId) {
    return <>Undoing the change to {target}</>;
  }

  switch (change.kind) {
    case 'Merge':
      return (
        <>
          Combining {change.sourceKeys.length} hashtags into {target}
        </>
      );
    case 'NormaliseCasing':
      return <>Settling every spelling on {target}</>;
    default:
      return <>Renaming to {target}</>;
  }
}

/**
 * Which markers an Apply covers: one rule's, named, or all of them. Read off the Change's own copy
 * of the rules rather than off the rules table, which may have moved on since it was confirmed.
 */
function markerScope(change: Change, styles: Record<string, string>) {
  if (change.sourceKeys.length !== 1) return <>all your markers</>;

  const rule = change.appliedMarkers.find((marker) => !marker.abandoned && marker.key === change.sourceKeys[0]);

  return rule ? (
    <>
      {rule.marker} to <span className={styles.tag}>#{rule.spelling}</span>
    </>
  ) : (
    <>one marker</>
  );
}

/**
 * Which markers a Remove covers: one, named by the emoji itself, or every stale one. Named by the
 * emoji rather than by a hashtag because that is what its scope is — a stale marker's hashtag has
 * gone, and one a deleted rule left behind never had a row to name it by (ADR-0014).
 */
function removalScope(change: Change) {
  const scoped = change.appliedMarkers.filter((marker) => marker.removable);

  // Every marker the person has is the all-markers scope; anything narrower was asked for by emoji.
  if (scoped.length !== 1) return <>stale markers</>;

  return <>stale {scoped[0].marker} markers</>;
}

/**
 * How a finished Change ended, and — when it failed — what to do about it. The offer is decided by
 * the code rather than by reading the sentence, so the wording can change without taking the offer
 * with it.
 */
function Outcome({ change }: { change: Change }) {
  // The same treatment the freshness chrome's reconnect gets, for the same reason: in the Teams
  // tab this must open a popup rather than navigate the frame to a page that refuses to be framed.
  const reconnect = useEntraRoundTrip();

  if (change.state === 'Failed') {
    return (
      <MessageBar intent="error">
        <MessageBarBody>{failureSentence(change)}</MessageBarBody>
        {change.failureCode === 'ReconnectRequired' && (
          <MessageBarActions>
            <Button
              as="a"
              href={signInUrl()}
              onClick={reconnect.intercept}
              appearance="primary"
              disabled={reconnect.running}
            >
              {reconnect.running ? 'Waiting…' : 'Sign in again'}
            </Button>
          </MessageBarActions>
        )}
      </MessageBar>
    );
  }

  return (
    <Badge appearance="tint" color={colorFor(change.state)}>
      {summarise(change)}
    </Badge>
  );
}

/**
 * The failure in the server's own words, and what was written before it stopped. Shared with the
 * notice the Workbench raises where the person watched the Change run, so the two cannot drift.
 */
export function failureSentence(change: Change): string {
  const reason = change.failureReason ?? 'This change did not finish.';

  return change.writtenCount > 0
    ? `${reason} ${change.writtenCount} task(s) were changed before it stopped.`
    : reason;
}

function colorFor(state: ChangeState): 'success' | 'warning' | 'informative' {
  switch (state) {
    case 'Completed':
      return 'success';
    case 'CompletedWithSkips':
      return 'warning';
    default:
      return 'informative';
  }
}

function summarise(change: Change): string {
  const written = `${change.writtenCount} changed`;

  switch (change.state) {
    case 'Completed':
      return written;
    case 'CompletedWithSkips':
      return `${written}, ${change.skippedCount} skipped`;
    case 'Cancelled':
      return change.writtenCount === 0 ? 'cancelled' : `cancelled after ${written}`;
    default:
      return written;
  }
}
