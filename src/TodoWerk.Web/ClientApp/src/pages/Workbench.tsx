import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactElement } from 'react';
import {
  Body1,
  Button,
  Caption1,
  Dropdown,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarGroup,
  MessageBarTitle,
  Option,
  ProgressBar,
  SearchBox,
  Skeleton,
  SkeletonItem,
  Subtitle1,
  Subtitle2,
  Text,
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { BroomRegular, CircleRegular, SearchRegular, TagRegular } from '@fluentui/react-icons';
import { getHashtagInventory, getIndexStatus, requestIndexScan, signInUrl } from '../api/client';
import { ApiError } from '../api/http';
import type { Change, HashtagInventoryFilter, HashtagInventoryRow, HashtagInventorySort } from '../api/types';
import { useApiQuery } from '../api/useApi';
import { ChangeConfirmDialog } from '../components/ChangeConfirmDialog';
import { ActiveChangePanel, ChangeHistoryPanel, failureSentence } from '../components/ChangeQueuePanel';
import { describeRange } from '../components/describeRange';
import { DismissButton } from '../components/DismissButton';
import { HashtagDetailPanel } from '../components/HashtagDetailPanel';
import { HashtagInventoryTable } from '../components/HashtagInventoryTable';
import { IndexFreshness } from '../components/IndexFreshness';
import { MarkerRulesPanel } from '../components/MarkerRulesPanel';
import { SetMarkerDialog } from '../components/SetMarkerDialog';
import { LicenceBanner } from '../components/LicenceBanner';
import { useProseStyles } from '../components/prose';
import { SignInPrompt } from '../components/SignInPrompt';
import { useEntraRoundTrip } from '../host';
import { useChangeWorkflow } from '../hooks/useChangeWorkflow';
import { useMarkerRules } from '../hooks/useMarkerRules';
import { indexIsBehind } from '../hooks/indexIsBehind';

const PAGE_SIZE = 50;

/** How often the chrome re-reads freshness while a scan is running. */
const SCAN_POLL_MS = 3000;

/**
 * How long typing may pause before the search actually runs. Each keystroke would otherwise be
 * its own aggregate query — five queries to type "kunde", four of them answering a question
 * nobody is still asking.
 */
const SEARCH_DEBOUNCE_MS = 300;

/** Rows a page shows while the first read is out: enough to look like the table, not a wall. */
const SKELETON_ROWS = 8;

/** How long "a scan is already on its way" stays on screen. News about a moment, not a state. */
const INFO_NOTICE_MS = 8000;

const FILTERS: { value: HashtagInventoryFilter; label: string }[] = [
  { value: 'None', label: 'All hashtags' },
  { value: 'MultipleSpellings', label: 'Written more than one way' },
  { value: 'NearDuplicates', label: 'Similar to another' },
  { value: 'Stale', label: 'Not edited in a long time' },
];

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalL,
    maxWidth: '1200px',
  },
  // Controls without labels of their own: in a toolbar the placeholder is the label, and the
  // dropdown's value says what it shows. Centred on one line rather than hung from labels.
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  search: {
    minWidth: '260px',
  },
  count: {
    marginInlineStart: 'auto',
    color: tokens.colorNeutralForeground3,
    fontVariantNumeric: 'tabular-nums',
  },
  columns: {
    display: 'flex',
    gap: tokens.spacingHorizontalL,
    alignItems: 'start',
    flexWrap: 'wrap',
  },
  table: {
    flexGrow: 1,
    minWidth: '520px',
  },
  // Room for the bar whether or not it is showing, so the table does not shift when it appears.
  // Pulled up into the gap above it rather than adding a row of its own.
  progress: {
    minHeight: tokens.strokeWidthThickest,
    marginBlockEnd: `calc(-1 * ${tokens.spacingVerticalL} + ${tokens.spacingVerticalXS})`,
  },
  // Dimmed, not removed, while the rows on screen answer an earlier sort, filter or page: what
  // is there is still readable, and the bar above it says why it is about to change.
  rows: {
    transitionProperty: 'opacity',
    transitionDuration: tokens.durationFast,
    transitionTimingFunction: tokens.curveEasyEase,
  },
  rowsStale: {
    opacity: 0.6,
  },
  pager: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalS,
  },
  range: {
    fontVariantNumeric: 'tabular-nums',
  },
  // Kept in the layout but not shown while the rows are stale, so the buttons beside it stay put
  // and a range is never read over rows it does not describe.
  rangeStale: {
    visibility: 'hidden',
  },
  placeholder: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
    alignItems: 'start',
    padding: tokens.spacingHorizontalXXL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  // The example list: drawn like To Do's own rows — a circle, a title, the hashtags in the
  // link colour — so the reader recognises where the hashtag goes before reading a word.
  mockList: {
    margin: 0,
    marginBlock: tokens.spacingVerticalS,
    width: '100%',
    maxWidth: '520px',
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    overflow: 'hidden',
  },
  mockRow: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalM,
    paddingInline: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  mockCircle: {
    flexShrink: 0,
    fontSize: '20px',
    color: tokens.colorNeutralForeground3,
  },
  mockTag: {
    color: tokens.colorBrandForegroundLink,
    fontWeight: tokens.fontWeightSemibold,
  },
  mockCaption: {
    display: 'block',
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalL,
    color: tokens.colorNeutralForeground3,
  },
  // Three short columns rather than three long paragraphs; they wrap to one column when the
  // panel is narrow, which is also the only layout the Android tab gets.
  tiles: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))',
    gap: tokens.spacingHorizontalXL,
    width: '100%',
    marginBlock: tokens.spacingVerticalS,
  },
  tile: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    maxWidth: '44ch',
  },
  tileIcon: {
    fontSize: '24px',
    color: tokens.colorBrandForeground1,
    marginBlockEnd: tokens.spacingVerticalXS,
  },
  skeleton: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalL,
  },
  skeletonTitle: {
    width: '160px',
  },
  skeletonToolbar: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
  },
  skeletonControl: {
    width: '260px',
  },
  skeletonRows: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
  },
});

interface ScanNotice {
  intent: 'error' | 'info';
  text: string;
}

/**
 * The Hashtag Manager's main screen. The inventory table is the product; freshness and scan
 * progress live in the chrome around it, because an index TodoWerk maintains itself is
 * sometimes stale and the user has to be able to see that (ADR-0003).
 */
export function Workbench() {
  const styles = useStyles();

  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState<HashtagInventoryFilter>('None');
  const [sort, setSort] = useState<HashtagInventorySort>('TaskCount');
  const [descending, setDescending] = useState(true);
  const [page, setPage] = useState(1);
  const [selectedKeys, setSelectedKeys] = useState<string[]>([]);
  const [rescanning, setRescanning] = useState(false);
  const [scanNotice, setScanNotice] = useState<ScanNotice | null>(null);

  // The box updates on every keystroke; the query only when typing pauses.
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setPage(1);
      setSearch(searchInput);
    }, SEARCH_DEBOUNCE_MS);

    return () => window.clearTimeout(timer);
  }, [searchInput]);

  const status = useApiQuery(getIndexStatus, 'index-status');
  const statusData =
    status.state.status === 'ready'
      ? status.state.data
      : status.state.status === 'error'
        ? status.state.stale
        : undefined;
  // Read from a fresh answer only. Deciding "a scan is running" from the last known state would
  // keep the three-second poll going forever once the status endpoint starts failing — the one
  // moment the server least needs two more requests every three seconds.
  const scanning = status.state.status === 'ready' && status.state.data.activity !== 'Idle';

  // The informational notice is news about a moment and leaves by itself; a failure stays until
  // it is dismissed, because nothing has resolved it. Timed rather than tied to the scan's own
  // state, which a failed status poll can misreport mid-scan.
  useEffect(() => {
    if (scanNotice?.intent !== 'info') return undefined;

    const timer = window.setTimeout(() => setScanNotice(null), INFO_NOTICE_MS);

    return () => window.clearTimeout(timer);
  }, [scanNotice]);

  const inventoryKey = JSON.stringify({ search, filter, sort, descending, page });
  const inventory = useApiQuery(
    useCallback(
      (signal: AbortSignal) =>
        getHashtagInventory({ search, filter, sort, descending, page, pageSize: PAGE_SIZE }, signal),
      [search, filter, sort, descending, page],
    ),
    inventoryKey,
  );
  const inventoryData =
    inventory.state.status === 'ready'
      ? inventory.state.data
      : inventory.state.status === 'error'
        ? inventory.state.stale
        : undefined;

  // While a scan runs the numbers move, so the chrome and the table follow it. Polling stops
  // the moment it finishes: a finished index does not change until somebody asks it to.
  const refreshStatus = status.refresh;
  const refreshInventory = inventory.refresh;

  useEffect(() => {
    if (!scanning) return undefined;

    const timer = window.setInterval(() => {
      refreshStatus();
      refreshInventory();
    }, SCAN_POLL_MS);

    return () => window.clearInterval(timer);
  }, [scanning, refreshStatus, refreshInventory]);

  // A shrinking result set can leave the page number past the end — a delta sync applying
  // deletions does it without the user touching anything. Clamped here so the grid never strands
  // them on an empty page whose pager has nothing to show.
  useEffect(() => {
    if (inventory.state.status !== 'ready') return;

    const lastPage = Math.max(1, Math.ceil(inventory.state.data.totalCount / PAGE_SIZE));

    if (page > lastPage) {
      setPage(lastPage);
    }
  }, [inventory.state, page]);

  const onRescan = useCallback(
    async (full: boolean, taskListId?: string) => {
      setScanNotice(null);
      setRescanning(true);

      try {
        const requested = await requestIndexScan(full, taskListId);

        if (!requested.queued) {
          setScanNotice({ intent: 'info', text: 'A scan is already on its way — this request is covered by it.' });
        }

        refreshStatus();
      } catch (error) {
        // The scan button is how a new account gets indexed at all, so its failure must not be
        // an unhandled rejection and a button that quietly stops spinning.
        if (error instanceof ApiError) {
          setScanNotice({
            intent: 'error',
            text:
              error.status === 401
                ? 'Your session has expired. Sign in again to start a scan.'
                : (error.problem.detail ?? error.message),
          });
        } else {
          setScanNotice({ intent: 'error', text: 'Could not reach TodoWerk. Check your connection and try again.' });
        }
      } finally {
        setRescanning(false);
      }
    },
    [refreshStatus],
  );

  const rows = useMemo(() => inventoryData?.rows ?? [], [inventoryData]);
  const totalCount = inventoryData?.totalCount ?? 0;

  // Stable, so the grid — fifty rows of cells — is left alone by the renders a poll causes.
  const onSortChange = useCallback((nextSort: HashtagInventorySort, nextDescending: boolean) => {
    setPage(1);
    setSort(nextSort);
    setDescending(nextDescending);
  }, []);

  // Only rows on this page can be acted on: a selection that survived a page turn would let
  // somebody confirm a Merge whose other half they can no longer see.
  const selected = useMemo(
    () => rows.filter((row) => selectedKeys.includes(row.key)),
    [rows, selectedKeys],
  );

  // A finished Change leaves the index one scan behind, and the follow-up scan it queued is what
  // catches it up — so both the inventory and the freshness want re-reading when one completes.
  const markers = useMarkerRules();
  const refreshMarkers = markers.refresh;

  const onChangeCompleted = useCallback(() => {
    refreshStatus();
    refreshInventory();
    // A finished Rename carries its rule to the new name and a finished Apply forgets the marker
    // it retired, so the rules on screen are as much out of date as the counts beside them.
    refreshMarkers();
    setSelectedKeys([]);
  }, [refreshStatus, refreshInventory, refreshMarkers]);

  const changes = useChangeWorkflow(onChangeCompleted);

  // Keyed by the folded key, which is what a row and a rule agree on: two spellings of one Hashtag
  // are one row and one rule.
  const markersByKey = useMemo(
    () => new Map(markers.rules.map((rule) => [rule.key, rule])),
    [markers.rules],
  );

  // Stable for the same reason `onSortChange` is: the grid memoises on its props, and an arrow
  // made afresh on every poll would rebuild fifty rows of cells every three seconds of a scan.
  const editMarker = markers.edit;
  const onSetMarker = useCallback(
    (row: HashtagInventoryRow) => editMarker({ spelling: row.canonicalSpelling, rule: markersByKey.get(row.key) }),
    [editMarker, markersByKey],
  );
  const behind = indexIsBehind(statusData, changes.history);

  // The Change that has just finished, announced where the person watched it run. The running
  // panel above the table leaves the moment it ends, and its outcome — a failure and the way out
  // of it, skips, or plain success — would otherwise be found only in the history below the
  // table, which a person who saw the progress bar vanish has no reason to scroll to.
  const [finished, setFinished] = useState<Change | null>(null);
  const previousActive = useRef<Change | null>(null);

  useEffect(() => {
    const was = previousActive.current;
    previousActive.current = changes.active;

    if (was === null || changes.active !== null) return;

    const outcome = changes.history.find((change) => change.id === was.id);

    if (outcome) setFinished(outcome);
  }, [changes.active, changes.history]);

  // A failed Change that needs a fresh sign-in offers it here, the way the freshness chrome does:
  // in the tab it must open a popup rather than navigate the frame to a page that refuses framing.
  const reconnect = useEntraRoundTrip(() => refreshStatus());

  if (status.state.status === 'signedOut' || inventory.state.status === 'signedOut') {
    return <SignInPrompt />;
  }

  if (status.state.status === 'loading') {
    return <WorkbenchSkeleton />;
  }

  if (statusData === undefined) {
    // No status has ever loaded, so there is nothing worth keeping on screen — only the error
    // and the way to try again.
    return (
      <MessageBar intent="error">
        <MessageBarBody>
          {status.state.status === 'error'
            ? (status.state.error.problem.detail ?? status.state.error.message)
            : 'Loading…'}
        </MessageBarBody>
        <MessageBarActions>
          <Button onClick={() => refreshStatus()}>Try again</Button>
        </MessageBarActions>
      </MessageBar>
    );
  }

  const neverScanned = statusData.listCount === 0 && statusData.activity === 'Idle';
  const lastPage = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  // Something is visibly happening whenever the rows on screen are about to be replaced: a
  // changed sort, filter or page, or a retry after a failed read. A poll during a scan is
  // neither — its rows are current — so it never flickers the table.
  const busy = inventory.stale || (inventory.fetching && inventory.state.status === 'error');
  const countKnown = inventory.state.status === 'ready' && !inventory.stale;

  const notices: ReactElement[] = [];

  if (status.state.status === 'error') {
    notices.push(
      <MessageBar key="status" intent="warning">
        <MessageBarBody>
          The freshness shown is the last known state — TodoWerk could not refresh it just now.
        </MessageBarBody>
        <MessageBarActions>
          <Button onClick={() => refreshStatus()} disabled={status.fetching}>
            Try again
          </Button>
        </MessageBarActions>
      </MessageBar>,
    );
  }

  if (scanNotice) {
    notices.push(
      <MessageBar key="scan" intent={scanNotice.intent}>
        <MessageBarBody>{scanNotice.text}</MessageBarBody>
        <MessageBarActions containerAction={<DismissButton onClick={() => setScanNotice(null)} />} />
      </MessageBar>,
    );
  }

  if (changes.notice) {
    notices.push(
      <MessageBar key="change" intent="error">
        <MessageBarBody>{changes.notice}</MessageBarBody>
        <MessageBarActions containerAction={<DismissButton onClick={changes.dismissNotice} />} />
      </MessageBar>,
    );
  }

  if (finished) {
    notices.push(
      <MessageBar key="finished" intent={outcomeIntent(finished)}>
        <MessageBarBody>
          <MessageBarTitle>{outcomeTitle(finished)}</MessageBarTitle> {outcomeSentence(finished)}
        </MessageBarBody>
        <MessageBarActions containerAction={<DismissButton onClick={() => setFinished(null)} />}>
          {finished.failureCode === 'ReconnectRequired' && (
            <Button
              as="a"
              href={signInUrl()}
              onClick={reconnect.intercept}
              appearance="primary"
              disabled={reconnect.running}
            >
              {reconnect.running ? 'Waiting for Microsoft…' : 'Sign in again'}
            </Button>
          )}
        </MessageBarActions>
      </MessageBar>,
    );
  }

  // The index catches up through Graph rather than through index writes from the Changes module,
  // so for one scan the table disagrees with what the user just watched succeed. Shown rather
  // than hidden: an inventory that silently contradicts them reads as a bug.
  if (behind) {
    notices.push(
      <MessageBar key="behind" intent="info">
        <MessageBarBody>
          The table below is one scan behind the change you just made — TodoWerk is re-reading
          the lists it touched, and the counts will catch up on their own.
        </MessageBarBody>
      </MessageBar>,
    );
  }

  return (
    <div className={styles.root}>
      <Title2 as="h2">Hashtags</Title2>

      {/* Inside the Workbench, above the table, so the Tenant Overview does not repeat it: what it
          says is about one person's own Licence, and that screen is about the organisation. */}
      <LicenceBanner />

      <IndexFreshness
        status={statusData}
        onRescan={(full, taskListId) => void onRescan(full, taskListId)}
        rescanning={rescanning}
      />

      {/* One group, so bars arrive and leave with a motion rather than a jump, and every one that
          is news about a moment can be waved away. */}
      {notices.length > 0 && <MessageBarGroup animate="both">{notices}</MessageBarGroup>}

      {/* The running Change stays above the table: it is transient, and the person who started it
          wants to watch it finish and be able to stop it. Its history goes below the table, because
          the table is the product. */}
      <ActiveChangePanel change={changes.active} onCancel={changes.cancel} busy={changes.busy} />

      <ChangeConfirmDialog
        open={changes.dialog.open}
        sources={changes.dialog.sources}
        target={changes.dialog.target}
        onTargetChange={changes.setTarget}
        preview={changes.dialog.preview}
        previewing={changes.dialog.previewing}
        error={changes.dialog.error}
        confirming={changes.dialog.confirming}
        onConfirm={changes.confirm}
        onDismiss={changes.dismiss}
        markerScope={changes.dialog.markerScope}
        survivingMarker={changes.dialog.survivingMarker}
        onChooseSurvivingMarker={changes.chooseSurvivingMarker}
      />

      <SetMarkerDialog
        open={markers.editing !== null}
        spelling={markers.editing?.spelling ?? ''}
        current={markers.editing?.rule?.marker}
        error={markers.editError}
        saving={markers.busy}
        onSave={markers.save}
        onDismiss={markers.cancelEdit}
      />

      {neverScanned ? (
        <FirstRun onRescan={() => void onRescan(false)} rescanning={rescanning} />
      ) : (
        <>
          <div className={styles.toolbar}>
            <SearchBox
              className={styles.search}
              value={searchInput}
              placeholder="Find a hashtag"
              aria-label="Find a hashtag"
              onChange={(_event, data) => setSearchInput(data.value)}
            />

            <Dropdown
              aria-label="Show"
              value={FILTERS.find((option) => option.value === filter)?.label}
              selectedOptions={[filter]}
              onOptionSelect={(_event, data) => {
                setPage(1);
                setFilter((data.optionValue as HashtagInventoryFilter) ?? 'None');
              }}
            >
              {FILTERS.map((option) => (
                <Option key={option.value} value={option.value}>
                  {option.label}
                </Option>
              ))}
            </Dropdown>

            {/* Only once the count answers the filter on screen: a number under the wrong label
                is a wrong number. */}
            {countKnown && (
              <Text className={styles.count}>
                {totalCount === 1 ? '1 hashtag' : `${totalCount} hashtags`}
              </Text>
            )}
          </div>

          {inventory.state.status === 'error' && (
            <MessageBar intent="error">
              <MessageBarBody>
                {inventory.state.error.problem.detail ?? inventory.state.error.message}
              </MessageBarBody>
              <MessageBarActions>
                <Button onClick={() => refreshInventory()} disabled={inventory.fetching}>
                  Try again
                </Button>
              </MessageBarActions>
            </MessageBar>
          )}

          {/* Above whatever stands in for the rows — the table, the empty placeholder — so a
              changed filter says something is happening whichever of them is on screen. */}
          <div className={styles.progress}>
            {busy && <ProgressBar thickness="medium" aria-label="Loading the hashtags" />}
          </div>

          {/* The table's shape whenever rows are on their way and none are on screen: the first
              read, and a retry after a read that failed — which would otherwise be a click that
              changed nothing until the answer landed. */}
          <div aria-busy={busy} className={busy ? `${styles.rows} ${styles.rowsStale}` : styles.rows}>
            {inventoryData === undefined ? (
              inventory.fetching && <InventorySkeleton />
            ) : rows.length === 0 ? (
              // An empty answer to the previous question is not a verdict on this one: while the
              // rows for the new filter are on their way, the shape of a table rather than "No
              // hashtags found" over a search that may well find some.
              inventory.stale ? (
                <InventorySkeleton />
              ) : (
                <Empty
                  scanning={scanning}
                  filtered={search.length > 0 || filter !== 'None'}
                  onRescan={() => void onRescan(false)}
                  rescanning={rescanning}
                />
              )
            ) : (
              <div className={styles.columns}>
                <div className={styles.table}>
                  <HashtagInventoryTable
                    rows={rows}
                    sort={sort}
                    descending={descending}
                    onSortChange={onSortChange}
                    selectedKeys={selectedKeys}
                    onSelectionChange={setSelectedKeys}
                    markers={markersByKey}
                    onSetMarker={onSetMarker}
                  />
                </div>

                <HashtagDetailPanel
                  rows={selected}
                  onStartChange={changes.start}
                  changeInFlight={changes.inFlight}
                />
              </div>
            )}
          </div>

          {totalCount > PAGE_SIZE && (
            <div className={styles.pager}>
              <Button appearance="subtle" disabled={page === 1} onClick={() => setPage(page - 1)}>
                Previous
              </Button>
              <Text className={busy ? `${styles.range} ${styles.rangeStale}` : styles.range}>
                {describeRange({ page, pageSize: PAGE_SIZE, totalCount })}
              </Text>
              <Button appearance="subtle" disabled={page >= lastPage} onClick={() => setPage(page + 1)}>
                Next
              </Button>
            </div>
          )}
        </>
      )}

      {/* Below the table, because the table is the product — and above the history, because a
          person who has just given a hashtag a marker is looking for where to apply it. */}
      <MarkerRulesPanel
        rules={markers.rules}
        abandoned={markers.abandoned}
        loading={markers.loading}
        busy={markers.busy}
        notice={markers.notice}
        onDismissNotice={markers.dismissNotice}
        onEdit={(rule) => markers.edit({ spelling: rule.spelling, rule })}
        onMove={markers.move}
        onRemove={markers.remove}
        onApplyAll={() => changes.startMarkers({ action: 'apply', ruleKey: null, label: 'Apply all markers' })}
        onApplyOne={(rule) =>
          changes.startMarkers({
            action: 'apply',
            ruleKey: rule.key,
            label: `Apply ${rule.marker} to #${rule.spelling}`,
          })
        }
        onRemoveAll={() =>
          changes.startMarkers({ action: 'remove', marker: null, label: 'Remove stale markers' })
        }
        onRemoveOne={(rule) =>
          changes.startMarkers({
            action: 'remove',
            marker: rule.marker,
            label: `Remove stale ${rule.marker} markers`,
          })
        }
        onRemoveAbandoned={(marker) =>
          changes.startMarkers({
            action: 'remove',
            marker: marker.marker,
            label: `Remove the ${marker.marker} markers left behind`,
          })
        }
        changeInFlight={changes.inFlight}
      />

      <ChangeHistoryPanel history={changes.history} onUndo={changes.undo} busy={changes.busy} />
    </div>
  );
}

function outcomeIntent(change: Change): 'success' | 'warning' | 'error' | 'info' {
  switch (change.state) {
    case 'Completed':
      return 'success';
    case 'CompletedWithSkips':
      return 'warning';
    case 'Failed':
      return 'error';
    default:
      return 'info';
  }
}

function outcomeTitle(change: Change): string {
  switch (change.state) {
    case 'Failed':
      return 'The change did not finish.';
    case 'Cancelled':
      return 'The change was cancelled.';
    default:
      return 'The change is done.';
  }
}

/** How it ended, in numbers, and where the way back is — when there is one. */
function outcomeSentence(change: Change): string {
  if (change.state === 'Failed') return failureSentence(change);

  const written = tasks(change.writtenCount);
  const undo = change.canUndo ? ' Undo is in the list below the table, for 30 days.' : '';

  switch (change.state) {
    case 'CompletedWithSkips':
      return `${written} changed and ${tasks(change.skippedCount)} skipped, because they had moved on since the preview.${undo}`;
    case 'Cancelled':
      return change.writtenCount === 0 ? 'Nothing was changed.' : `${written} changed before it stopped.${undo}`;
    default:
      return `${written} changed.${undo}`;
  }
}

function tasks(count: number): string {
  return count === 1 ? '1 task' : `${count} tasks`;
}

/**
 * The page's shape while the first read is out — a title, the freshness line, the toolbar and a
 * few rows — so the layout arrives once rather than jumping from a spinner to a full screen.
 */
function WorkbenchSkeleton() {
  const styles = useStyles();

  return (
    <Skeleton aria-label="Loading your hashtags" className={styles.skeleton}>
      <SkeletonItem shape="rectangle" size={32} className={styles.skeletonTitle} />
      <SkeletonItem shape="rectangle" size={48} />
      <div className={styles.skeletonToolbar}>
        <SkeletonItem shape="rectangle" size={32} className={styles.skeletonControl} />
        <SkeletonItem shape="rectangle" size={32} className={styles.skeletonControl} />
      </div>
      <SkeletonRows />
    </Skeleton>
  );
}

/** The table's shape while the inventory itself is still out. */
function InventorySkeleton() {
  return (
    <Skeleton aria-label="Loading the inventory">
      <SkeletonRows />
    </Skeleton>
  );
}

function SkeletonRows() {
  const styles = useStyles();

  return (
    <div className={styles.skeletonRows}>
      {Array.from({ length: SKELETON_ROWS }, (_, row) => (
        <SkeletonItem key={row} shape="rectangle" size={40} />
      ))}
    </div>
  );
}

/**
 * The first thing a new user sees, and a first scan is minutes long — so it says what will
 * happen rather than showing an empty grid and letting them wonder.
 */
function FirstRun({ onRescan, rescanning }: { onRescan: () => void; rescanning: boolean }) {
  const styles = useStyles();
  const { prose } = useProseStyles();

  return (
    <section className={styles.placeholder}>
      <Subtitle1>No hashtags indexed yet</Subtitle1>
      <Body1 className={prose}>
        TodoWerk reads your Microsoft To Do lists and collects the{' '}
        <Text font="monospace">#hashtags</Text> in your task titles. The first pass goes through
        every task, so it takes a few minutes on a busy account — after that it keeps itself up to
        date.
      </Body1>
      <Button appearance="primary" onClick={onRescan} disabled={rescanning}>
        Scan my task lists
      </Button>
    </section>
  );
}

/**
 * What stands where the table would be when the index has nothing in it. The unfiltered case is
 * the one a new user meets who has never written a hashtag, so it teaches the habit — where a
 * hashtag goes in a title and what it buys them in To Do — rather than only reporting the count.
 */
function Empty({
  scanning,
  filtered,
  onRescan,
  rescanning,
}: {
  scanning: boolean;
  filtered: boolean;
  onRescan: () => void;
  rescanning: boolean;
}) {
  const styles = useStyles();
  const { prose } = useProseStyles();

  return (
    <section className={styles.placeholder}>
      {scanning ? (
        <>
          <Subtitle1>Still scanning</Subtitle1>
          <Body1 className={prose}>
            Hashtags appear here as the lists are read. Nothing has turned up yet, which is normal
            in the first moments of a scan.
          </Body1>
        </>
      ) : filtered ? (
        <>
          <Subtitle1>Nothing matches</Subtitle1>
          <Body1 className={prose}>
            No hashtag matches that search and filter. Try widening one of them.
          </Body1>
        </>
      ) : (
        <>
          <Subtitle1>No hashtags found</Subtitle1>
          <Body1 className={prose}>
            TodoWerk read your task lists and found no <Text font="monospace">#hashtags</Text> in
            any task title. Here is what one looks like, and what it is good for.
          </Body1>

          {/* Three task titles the way To Do shows them: the hashtag is the coloured word. The
              third row repeats a hashtag in another casing on purpose, because that drift is
              the first thing this page is for. */}
          <figure className={styles.mockList} aria-label="Three example tasks in Microsoft To Do">
            <ExampleTask title="Renew the passport" tags={['#admin']} />
            <ExampleTask title="Draft the budget" tags={['#Q4', '#finance']} />
            <ExampleTask title="Call the supplier" tags={['#Q4', '#Finance']} />
            <figcaption className={styles.mockCaption}>
              <Caption1>
                <Text font="monospace">#finance</Text> and <Text font="monospace">#Finance</Text>{' '}
                are one hashtag written two ways. TodoWerk would flag that.
              </Caption1>
            </figcaption>
          </figure>

          <div className={styles.tiles}>
            <div className={styles.tile}>
              <TagRegular className={styles.tileIcon} aria-hidden />
              <Subtitle2>Write one</Subtitle2>
              <Body1>
                Put <Text font="monospace">#</Text> in front of a word in the task title, at the
                start or after a space. Letters, digits, underscores and hyphens make up the
                name, so <Text font="monospace">#Prüfung</Text> and{' '}
                <Text font="monospace">#follow-up</Text> both count. A task can carry as many as
                you like.
              </Body1>
            </div>
            <div className={styles.tile}>
              <SearchRegular className={styles.tileIcon} aria-hidden />
              <Subtitle2>Find by it</Subtitle2>
              <Body1>
                A task lives in exactly one To Do list, so a list can only say where a task is,
                not what it is about. Select a hashtag in To Do and every task carrying it shows
                up, whichever list it sits in. Tag by customer or by project and your lists can
                stay short.
              </Body1>
            </div>
            <div className={styles.tile}>
              <BroomRegular className={styles.tileIcon} aria-hidden />
              <Subtitle2>Keep them tidy</Subtitle2>
              <Body1>
                Once you have a few, this page shows how often each one is used and catches the
                ones that drifted, such as <Text font="monospace">#work</Text> next to{' '}
                <Text font="monospace">#Work</Text>. Rename or merge them across every task in
                one go, previewed first and undoable.
              </Body1>
            </div>
          </div>

          <Body1 className={prose}>Add a hashtag to a task, then check for changes to see it here.</Body1>
          <Button appearance="primary" onClick={onRescan} disabled={rescanning}>
            Check for changes
          </Button>
        </>
      )}
    </section>
  );
}

/**
 * One row of the example list: a task the way To Do draws it, with the hashtags coloured as To
 * Do colours them. Static, and not a real task — the circle is drawn, not a checkbox.
 */
function ExampleTask({ title, tags }: { title: string; tags: string[] }) {
  const styles = useStyles();

  return (
    <div className={styles.mockRow}>
      <CircleRegular className={styles.mockCircle} aria-hidden />
      <Body1>
        {title}
        {tags.map((tag) => (
          <span key={tag}>
            {' '}
            <span className={styles.mockTag}>{tag}</span>
          </span>
        ))}
      </Body1>
    </div>
  );
}
