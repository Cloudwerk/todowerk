import { useState } from 'react';
import {
  Badge,
  Body1,
  Button,
  Caption1,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  Subtitle1,
  Text,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  ArrowDownRegular,
  ArrowUpRegular,
  DeleteRegular,
  EditRegular,
  EraserRegular,
  PlayRegular,
} from '@fluentui/react-icons';
import type { AbandonedMarker, MarkerRule, MarkerRuleMove } from '../api/types';
import { DismissButton } from './DismissButton';

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
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  spacer: {
    marginInlineStart: 'auto',
  },
  rows: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
  },
  row: {
    display: 'grid',
    gridTemplateColumns: 'auto minmax(0, 1fr) auto auto',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
  },
  marker: {
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase500,
  },
  tag: {
    fontFamily: tokens.fontFamilyMonospace,
  },
  coverage: {
    color: tokens.colorNeutralForeground3,
    fontVariantNumeric: 'tabular-nums',
  },
  actions: {
    display: 'flex',
    gap: tokens.spacingHorizontalXXS,
  },
  empty: {
    color: tokens.colorNeutralForeground3,
  },
  leftBehind: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    paddingBlockStart: tokens.spacingVerticalS,
    borderBlockStartColor: tokens.colorNeutralStroke2,
    borderBlockStartStyle: 'solid',
    borderBlockStartWidth: '1px',
  },
  leftBehindRow: {
    display: 'grid',
    gridTemplateColumns: 'auto minmax(0, 1fr) auto',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
  },
});

interface MarkerRulesPanelProps {
  rules: MarkerRule[];
  /** Markers left behind by rules the person deleted, and still at the front of tasks. */
  abandoned: AbandonedMarker[];
  loading: boolean;
  busy: boolean;
  notice: string | undefined;
  onDismissNotice: () => void;
  onEdit: (rule: MarkerRule) => void;
  onMove: (ruleId: string, direction: MarkerRuleMove) => void;
  onRemove: (ruleId: string) => void;
  /** Applies every rule, or the one on this row. Both open the same confirm dialog. */
  onApplyAll: () => void;
  onApplyOne: (rule: MarkerRule) => void;
  /**
   * Takes stale markers back out: every one of them, one rule's, or one a deleted rule left behind.
   * All three open the same confirm dialog the other changes do, and are undoable the same way.
   */
  onRemoveAll: () => void;
  onRemoveOne: (rule: MarkerRule) => void;
  onRemoveAbandoned: (marker: AbandonedMarker) => void;
  /** True while a Change is queued or running: one per user at a time (ADR-0006). */
  changeInFlight: boolean;
}

/**
 * The rules view: a person's Marker Rules in the order the block is written in, what each one has
 * reached so far, and the two ways to apply them.
 *
 * Up and down buttons rather than drag. The order matters — it is the order of the block — and it
 * has to be changeable with a keyboard and inside the Teams tab, neither of which a drag handle
 * serves. A rule whose Hashtag has no Occurrences left is still listed, because the rule persists
 * and its Spelling is kept on it for exactly that.
 */
export function MarkerRulesPanel({
  rules,
  abandoned,
  loading,
  busy,
  notice,
  onDismissNotice,
  onEdit,
  onMove,
  onRemove,
  onApplyAll,
  onApplyOne,
  onRemoveAll,
  onRemoveOne,
  onRemoveAbandoned,
  changeInFlight,
}: MarkerRulesPanelProps) {
  const styles = useStyles();

  // Asked before a rule goes. It writes no title (ADR-0014), so this is not about data loss — it
  // is that everything else here that destroys something asks first, and a trash icon beside four
  // other icons is one wrong click from taking a rule and its place in the order with it.
  const [doomed, setDoomed] = useState<MarkerRule | null>(null);

  // Anything stale anywhere is what makes "Remove stale markers" worth offering: a rule's own, and
  // the ones whose rule is gone and which have no row of their own to be removed from.
  const stale =
    rules.reduce((total, rule) => total + rule.staleTaskCount, 0) +
    abandoned.reduce((total, marker) => total + marker.staleTaskCount, 0);

  return (
    <section className={styles.root} aria-label="Marker rules">
      <Dialog open={doomed !== null} onOpenChange={(_event, data) => !data.open && setDoomed(null)}>
        <DialogSurface>
          {/* Rendered only while there is a rule to ask about, so the closing transition does not
              show a question about "#" with nothing after it. */}
          {doomed && (
            <DialogBody>
              <DialogTitle>
                Delete the marker rule for <span className={styles.tag}>#{doomed.spelling}</span>?
              </DialogTitle>
              <DialogContent>
                <Body1>
                  Tasks are not changed: any {doomed.marker} already on them stays until you remove
                  it yourself, and TodoWerk keeps a note of the emoji so it still knows it as a
                  marker there. Only the rule goes — and the emoji is listed below afterwards, so
                  you can take it off your tasks whenever you like.
                </Body1>
              </DialogContent>
              <DialogActions>
                <Button appearance="primary" disabled={busy} onClick={() => setDoomed(null)}>
                  Keep the rule
                </Button>
                <Button
                  appearance="secondary"
                  disabled={busy}
                  onClick={() => {
                    onRemove(doomed.id);
                    setDoomed(null);
                  }}
                >
                  Delete rule
                </Button>
              </DialogActions>
            </DialogBody>
          )}
        </DialogSurface>
      </Dialog>

      <div className={styles.header}>
        <Subtitle1 as="h3">Markers</Subtitle1>
        <div className={styles.spacer} />
        <Button
          appearance="primary"
          icon={<PlayRegular />}
          disabled={rules.length === 0 || changeInFlight || busy}
          onClick={onApplyAll}
        >
          Apply all markers…
        </Button>
        <Button
          appearance="secondary"
          icon={<EraserRegular />}
          disabled={stale === 0 || changeInFlight || busy}
          onClick={onRemoveAll}
        >
          Remove stale markers…
        </Button>
      </div>

      <Caption1>
        A marker rule says a hashtag carries an emoji. Nothing is written until you apply your
        markers, which you can preview and undo like any other change. A marker whose hashtag has
        since gone stays on the task until you remove it yourself.
      </Caption1>

      {notice && (
        <MessageBar intent="error">
          <MessageBarBody>{notice}</MessageBarBody>
          <MessageBarActions containerAction={<DismissButton onClick={onDismissNotice} />} />
        </MessageBar>
      )}

      {changeInFlight && (
        <Caption1>One change at a time. Let the running one finish, or cancel it.</Caption1>
      )}

      {loading ? (
        <Body1 className={styles.empty}>Loading your markers…</Body1>
      ) : rules.length === 0 ? (
        <Body1 className={styles.empty}>
          No markers yet. Pick a hashtag in the table and choose “Set marker” to give it one.
        </Body1>
      ) : (
        <div className={styles.rows}>
          {rules.map((rule, position) => (
            <div key={rule.id} className={styles.row}>
              <Text className={styles.marker} aria-label={`Marker ${rule.marker}`}>
                {rule.marker}
              </Text>

              <Text className={styles.tag} weight="semibold">
                #{rule.spelling}
              </Text>

              <Coverage rule={rule} className={styles.coverage} />

              <div className={styles.actions}>
                <Tooltip content="Move up" relationship="label">
                  <Button
                    appearance="subtle"
                    icon={<ArrowUpRegular />}
                    aria-label={`Move #${rule.spelling} up`}
                    disabled={position === 0 || busy}
                    onClick={() => onMove(rule.id, 'Up')}
                  />
                </Tooltip>
                <Tooltip content="Move down" relationship="label">
                  <Button
                    appearance="subtle"
                    icon={<ArrowDownRegular />}
                    aria-label={`Move #${rule.spelling} down`}
                    disabled={position === rules.length - 1 || busy}
                    onClick={() => onMove(rule.id, 'Down')}
                  />
                </Tooltip>
                <Tooltip content="Change marker" relationship="label">
                  <Button
                    appearance="subtle"
                    icon={<EditRegular />}
                    aria-label={`Change the marker for #${rule.spelling}`}
                    disabled={busy}
                    onClick={() => onEdit(rule)}
                  />
                </Tooltip>
                <Tooltip content="Apply this marker" relationship="label">
                  <Button
                    appearance="subtle"
                    icon={<PlayRegular />}
                    aria-label={`Apply the marker for #${rule.spelling}`}
                    disabled={changeInFlight || busy}
                    onClick={() => onApplyOne(rule)}
                  />
                </Tooltip>
                <Tooltip
                  content={
                    rule.staleTaskCount === 0
                      ? `No task carries ${rule.marker} without #${rule.spelling}.`
                      : `Take ${rule.marker} off the tasks that no longer carry #${rule.spelling}.`
                  }
                  relationship="label"
                >
                  <Button
                    appearance="subtle"
                    icon={<EraserRegular />}
                    aria-label={`Remove stale ${rule.marker} markers for #${rule.spelling}`}
                    disabled={rule.staleTaskCount === 0 || changeInFlight || busy}
                    onClick={() => onRemoveOne(rule)}
                  />
                </Tooltip>
                <Tooltip content="Delete rule" relationship="label">
                  <Button
                    appearance="subtle"
                    icon={<DeleteRegular />}
                    aria-label={`Delete the marker rule for #${rule.spelling}`}
                    disabled={busy}
                    onClick={() => setDoomed(rule)}
                  />
                </Tooltip>
              </div>
            </div>
          ))}
        </div>
      )}

      {abandoned.length > 0 && (
        <div className={styles.leftBehind}>
          <Caption1>
            Markers left behind by rules you deleted. TodoWerk still knows these as markers, so they
            are read as part of the block — but nothing puts them on a task any more.
          </Caption1>

          {abandoned.map((marker) => (
            <div key={marker.marker} className={styles.leftBehindRow}>
              <Text className={styles.marker} aria-label={`Marker ${marker.marker}`}>
                {marker.marker}
              </Text>

              <Text className={styles.coverage}>
                on {marker.staleTaskCount} {marker.staleTaskCount === 1 ? 'task' : 'tasks'}, with no
                rule
              </Text>

              <Tooltip content={`Take ${marker.marker} off those tasks.`} relationship="label">
                <Button
                  appearance="subtle"
                  icon={<EraserRegular />}
                  aria-label={`Remove the ${marker.marker} markers left behind`}
                  disabled={changeInFlight || busy}
                  onClick={() => onRemoveAbandoned(marker)}
                />
              </Tooltip>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}

/**
 * How far this rule has been applied, and — where one is waiting — that a marker it replaced is
 * still out there. The figure is as fresh as the last scan, which the freshness chrome above the
 * table already says.
 *
 * The "was 🥖" badge is shown only while some task still carries the old emoji, rather than
 * whenever the rule remembers one: a swap that has already reached everywhere would otherwise go
 * on offering to make a change that would change nothing. The rule keeps remembering it
 * either way — that is what lets a later apply reach a task in a list nobody has read yet, and it
 * is why this hides a badge rather than clearing a rule.
 */
function Coverage({ rule, className }: { rule: MarkerRule; className: string }) {
  return (
    <span className={className}>
      <Text className={className}>
        {rule.taggedTaskCount === 0
          ? 'no tagged tasks'
          : `${rule.markedTaskCount} of ${rule.taggedTaskCount} tagged tasks`}
      </Text>
      {rule.staleTaskCount > 0 && (
        <>
          {' '}
          <Tooltip
            content={`${rule.staleTaskCount} ${
              rule.staleTaskCount === 1 ? 'task carries' : 'tasks carry'
            } ${rule.marker} without #${rule.spelling}.`}
            relationship="description"
          >
            <Badge appearance="tint" color="informative" tabIndex={0}>
              {rule.staleTaskCount} stale
            </Badge>
          </Tooltip>
        </>
      )}
      {rule.retiredMarker && rule.retiredTaskCount > 0 && (
        <>
          {' '}
          <Tooltip
            content={`Applying will replace ${rule.retiredMarker} with ${rule.marker} on ${
              rule.retiredTaskCount === 1 ? '1 task' : `${rule.retiredTaskCount} tasks`
            }.`}
            relationship="description"
          >
            <Badge appearance="tint" color="warning" tabIndex={0}>
              was {rule.retiredMarker}
            </Badge>
          </Tooltip>
        </>
      )}
    </span>
  );
}
