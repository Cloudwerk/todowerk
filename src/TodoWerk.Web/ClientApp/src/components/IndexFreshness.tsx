import { useEffect, useId, useState } from 'react';
import {
  Body1,
  Button,
  Caption1,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  ProgressBar,
  SplitButton,
  Spinner,
  Text,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import type { MenuButtonProps } from '@fluentui/react-components';
import { ChevronDownRegular, ChevronRightRegular } from '@fluentui/react-icons';
import { signInUrl } from '../api/client';
import { useEntraRoundTrip } from '../host';
import type { IndexStatus, TaskListStatus } from '../api/types';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  headline: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  spacer: {
    marginInlineStart: 'auto',
  },
  lists: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    margin: 0,
    padding: 0,
    listStyle: 'none',
  },
  listRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  // The status token rather than a colour off Fluent's fixed palette: a failed list is the same
  // warning the MessageBar above it carries, and a theme that inverts should be free to say so in
  // its own words. On the dark ramp it also lands further clear of the panel behind it.
  failed: {
    color: tokens.colorStatusWarningForeground1,
  },
});

interface IndexFreshnessProps {
  status: IndexStatus;
  /** `full` re-reads every task rather than only what Graph reports as changed. */
  onRescan: (full: boolean, taskListId?: string) => void;
  rescanning: boolean;
}

/**
 * Freshness in the chrome, not in a diagnostics page: TodoWerk keeps its own index
 * (ADR-0003), so it is sometimes stale, and hiding that would let somebody act on numbers
 * they have no reason to trust.
 *
 * One line when there is nothing to say per list, because what sits below this is the inventory
 * table, which CONTEXT.md calls the product: a person with twelve lists should not read twelve
 * rows of chrome before their first hashtag.
 */
export function IndexFreshness({ status, onRescan, rescanning }: IndexFreshnessProps) {
  const styles = useStyles();
  const now = useNow();
  const listsId = useId();
  // Reconnecting ends at a Microsoft sign-in page, which refuses to be framed. In the browser this
  // does nothing and the link navigates; in the Teams tab it takes the navigation into a popup.
  // Under Teams single sign-on it should almost never fire — every tab load re-mints the session
  // from a fresh token — but a path that is never taken is a path that has quietly rotted.
  const reconnect = useEntraRoundTrip(() => onRescan(false));
  const scanning = status.activity !== 'Idle';
  const failed = status.lists.filter((list) => list.state === 'Failed');

  // The per-list breakdown unfolds by itself exactly when it has something to say — a scan in
  // progress, or a list that did not finish — and stays folded otherwise. Freshness is per list
  // (one list can be an hour staler than the rest, or failed outright, and the API carries those
  // timestamps so a person can re-scan that one list), so it is a click away rather than gone. A choice is remembered with the reason it overrode and lasts exactly as long as
  // that reason does: folding it during a scan must not hide the list that fails after the scan,
  // and with it the one Re-scan button for that list.
  const reason: UnfoldReason = scanning ? 'scanning' : failed.length > 0 ? 'failed' : 'none';
  const [choice, setChoice] = useState<{ open: boolean; reason: UnfoldReason } | null>(null);
  const showLists = choice !== null && choice.reason === reason ? choice.open : reason !== 'none';

  return (
    <section className={styles.root} aria-label="Index freshness">
      <div className={styles.headline}>
        {scanning && <Spinner size="tiny" />}
        <Body1>
          <Summary status={status} now={now} />
        </Body1>

        {status.listCount > 0 && (
          <Button
            appearance="subtle"
            icon={showLists ? <ChevronDownRegular /> : <ChevronRightRegular />}
            aria-expanded={showLists}
            aria-controls={showLists ? listsId : undefined}
            onClick={() => setChoice({ open: !showLists, reason })}
          >
            {status.listCount === 1 ? '1 list' : `${status.listCount} lists`}
          </Button>
        )}

        {/*
          Two re-scans, because they answer different worries. The default asks Graph what has
          changed and is seconds; the second re-reads every task and is the answer to "the numbers
          look wrong", which a delta stream cannot rule out on its own.

          The label stays put while a scan runs: the sentence beside it already says so, and a
          button that changes its name says it a second time.
        */}
        <div className={styles.spacer}>
          <Menu positioning="below-end">
            <MenuTrigger disableButtonEnhancement>
              {(triggerProps: MenuButtonProps) => (
                <SplitButton
                  appearance="subtle"
                  menuButton={{ ...triggerProps, 'aria-label': 'More re-scan choices' }}
                  primaryActionButton={{ onClick: () => onRescan(false) }}
                  disabled={scanning || rescanning}
                >
                  Check for changes
                </SplitButton>
              )}
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItem onClick={() => onRescan(true)}>Read every task again</MenuItem>
              </MenuList>
            </MenuPopover>
          </Menu>
        </div>
      </div>

      {scanning && status.listCount > 0 && (
        <ProgressBar
          value={status.listsIndexed}
          max={status.listCount}
          aria-label={`${status.listsIndexed} of ${status.listCount} lists scanned`}
        />
      )}

      {showLists && status.listCount > 0 && (
        <ul className={styles.lists} id={listsId}>
          {status.lists.map((list) => (
            <li key={list.taskListId} className={styles.listRow}>
              <Caption1>
                {list.displayName} — {describeList(list, scanning, now)}
              </Caption1>
              {!scanning && (
                <Button
                  appearance="subtle"
                  disabled={rescanning}
                  onClick={() => onRescan(true, list.taskListId)}
                  aria-label={`Re-scan ${list.displayName}`}
                >
                  Re-scan
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}

      {/*
        The scan's own failure, which no list can carry: a scan that never got a token or never
        got the list of lists leaves no per-list rows behind. Without this the screen says the
        account has not been indexed yet and gives no hint that it tried and could not.
      */}
      {status.lastScanFailure && !scanning && (
        <MessageBar intent="warning">
          <MessageBarBody>
            <MessageBarTitle>The last scan did not finish.</MessageBarTitle> {status.lastScanFailure}
          </MessageBarBody>
          {reconnect.failure !== null && <MessageBarBody>{reconnect.failure}</MessageBarBody>}
          {status.lastScanFailureCode === 'ReconnectRequired' && (
            <MessageBarActions>
              <Button
                as="a"
                href={signInUrl()}
                onClick={reconnect.intercept}
                appearance="primary"
                disabled={reconnect.running}
              >
                {reconnect.running ? 'Waiting for Microsoft…' : 'Sign in again'}
              </Button>
            </MessageBarActions>
          )}
        </MessageBar>
      )}

      {failed.length > 0 && (
        <Text className={styles.failed} size={200}>
          {failed.length === 1
            ? `“${failed[0].displayName}”: ${describeFailure(failed[0])}`
            : `${failed.length} lists did not finish scanning. TodoWerk will try them again.`}
        </Text>
      )}
    </section>
  );
}

/** Why the breakdown would unfold by itself, or `none`. */
type UnfoldReason = 'scanning' | 'failed' | 'none';

/**
 * A clock that ticks once a minute, so relative ages keep aging. Without it, "just now" computed
 * at render time stays on screen for hours — a stopped clock shown as the freshness guarantee.
 */
function useNow(): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 60_000);
    return () => window.clearInterval(timer);
  }, []);

  return now;
}

function Summary({ status, now }: { status: IndexStatus; now: number }) {
  if (status.activity === 'Queued') {
    return <>A scan is queued and will start shortly.</>;
  }

  if (status.activity === 'Scanning') {
    return status.listCount > 0 ? (
      <>
        Scanning — {status.listsIndexed} of {status.listCount} lists done, {status.tasksIndexed} tasks read.
      </>
    ) : (
      <>Scanning your task lists…</>
    );
  }

  if (!status.hasCompletedFirstScan) {
    return status.listCount === 0 ? (
      <>Your hashtags have not been indexed yet.</>
    ) : (
      <>The first scan has not finished, so some hashtags may be missing.</>
    );
  }

  return status.currentAsOf ? (
    <>
      Up to date as of <Age timestamp={status.currentAsOf} now={now} /> — {status.tasksIndexed} tasks indexed.
    </>
  ) : (
    <>{status.tasksIndexed} tasks indexed.</>
  );
}

/**
 * Relative on screen, absolute in a tooltip and in the markup — machines and sceptics both served.
 * A Fluent Tooltip rather than a `title` attribute, for the reasons the flag badges give: it is
 * wired as the element's accessible description, reachable by keyboard focus, and works on touch.
 */
function Age({ timestamp, now }: { timestamp: string; now: number }) {
  return (
    <Tooltip content={new Date(timestamp).toLocaleString()} relationship="description">
      <time dateTime={timestamp} tabIndex={0}>
        {describeAge(timestamp, now)}
      </time>
    </Tooltip>
  );
}

function describeList(list: TaskListStatus, scanning: boolean, now: number): string {
  switch (list.state) {
    case 'Indexed':
      return list.lastSuccessfulSyncAt && !scanning
        ? `${list.tasksIndexed} tasks, synced ${describeAge(list.lastSuccessfulSyncAt, now)}`
        : `${list.tasksIndexed} tasks`;
    case 'Scanning':
      return `reading… (${list.tasksIndexed})`;
    case 'Failed':
      return 'failed';
    default:
      return 'waiting';
  }
}

/**
 * The server's own words for why a list failed, with nothing added to them but the list's name at
 * the call site. A wrapper such as "X could not be read: …" would assert a cause the client cannot
 * know any more than the server can: a write that failed would show as a failed read, and send the
 * reader to Graph, to permissions and to the list itself. Each failure is already worded for a
 * reader where it happens; naming which list is all that is left to do here.
 */
function describeFailure(list: TaskListStatus): string {
  return list.failureReason ?? 'The scan of this list did not finish. TodoWerk will try again.';
}

/** Relative, because "four minutes ago" is the question being asked, not the wall-clock time. */
function describeAge(timestamp: string, now: number): string {
  const minutes = Math.round((now - new Date(timestamp).getTime()) / 60000);

  if (minutes < 1) return 'just now';
  if (minutes === 1) return 'a minute ago';
  if (minutes < 60) return `${minutes} minutes ago`;

  const hours = Math.round(minutes / 60);
  if (hours === 1) return 'an hour ago';
  if (hours < 24) return `${hours} hours ago`;

  const days = Math.round(hours / 24);
  return days === 1 ? 'yesterday' : `${days} days ago`;
}
