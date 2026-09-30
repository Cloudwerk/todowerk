import { Fragment } from 'react';
import {
  Body1,
  Button,
  Caption1,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Subtitle2,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import type { ChangeMarkerRules, ChangePreview } from '../api/types';
import type { MarkerScope } from '../hooks/useChangeWorkflow';
import { diffBlocks, diffTitles } from './diffTitles';
import type { TitlePart } from './diffTitles';
import { validateSpelling } from './validateSpelling';

/** As many pairs as the dialog renders at once. The rest are counted rather than drawn. */
const VISIBLE_PAIRS = 200;

const SPELLING_HINT = 'Letters, digits, hyphens and underscores. No spaces, and no leading #.';

const useStyles = makeStyles({
  surface: {
    maxWidth: '760px',
  },
  target: {
    maxWidth: '320px',
  },
  pairs: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    maxHeight: '320px',
    overflowY: 'auto',
    padding: tokens.spacingHorizontalS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },
  pair: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 1fr) auto minmax(0, 1fr)',
    alignItems: 'baseline',
    columnGap: tokens.spacingHorizontalS,
  },
  before: {
    color: tokens.colorNeutralForeground3,
    overflowWrap: 'anywhere',
  },
  after: {
    overflowWrap: 'anywhere',
  },
  // The one token in each title the Change touches. Brand tokens rather than the browser's
  // yellow, so the mark inverts with the rest of the theme.
  mark: {
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground2,
    borderRadius: tokens.borderRadiusSmall,
    paddingInline: '2px',
  },
  struck: {
    textDecorationLine: 'line-through',
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
  },
  excluded: {
    margin: 0,
    paddingInlineStart: tokens.spacingHorizontalL,
  },
  survivors: {
    display: 'flex',
    gap: tokens.spacingHorizontalXS,
    flexWrap: 'wrap',
  },
  marker: {
    fontSize: tokens.fontSizeBase500,
  },
  tag: {
    fontFamily: tokens.fontFamilyMonospace,
  },
});

interface ChangeConfirmDialogProps {
  open: boolean;
  /** The hashtags being rewritten, by canonical spelling — what the user picked in the table. */
  sources: string[];
  target: string;
  onTargetChange: (target: string) => void;
  preview: ChangePreview | undefined;
  previewing: boolean;
  /** A refusal from the server, already worded for a reader. */
  error: string | undefined;
  confirming: boolean;
  onConfirm: () => void;
  onDismiss: () => void;
  /** Set when this is an Apply Markers: there is no spelling to type, only a scope to confirm. */
  markerScope: MarkerScope | undefined;
  /** The marker the user has picked to survive a Merge, or undefined. */
  survivingMarker: string | undefined;
  onChooseSurvivingMarker: (marker: string) => void;
}

/**
 * Where a Change is confirmed. A dialog rather than the side panel, because up to a thousand
 * old-title/new-title pairs, the lists left out of the plan and the merge warning need the room —
 * and because what is being confirmed is a set of specific tasks rather than an intention.
 *
 * In each pair the Hashtag that changes is marked, and the one it replaces struck: the reader's
 * job is to find the one token that differs in each of up to two hundred titles, and this is the
 * moment the product's trust rests on.
 */
export function ChangeConfirmDialog({
  open,
  sources,
  target,
  onTargetChange,
  preview,
  previewing,
  error,
  confirming,
  onConfirm,
  onDismiss,
  markerScope,
  survivingMarker,
  onChooseSurvivingMarker,
}: ChangeConfirmDialogProps) {
  const styles = useStyles();

  const merge = preview?.requiresMergeConfirmation ?? false;
  const pairs = preview?.items ?? [];
  const hidden = Math.max(0, pairs.length - VISIBLE_PAIRS);
  const skips = preview?.skips ?? [];
  const hiddenSkips = Math.max(0, skips.length - VISIBLE_PAIRS);
  const rules = preview?.markerRules;
  // Both marker kinds write the block at the front and nothing behind it, so both are read with
  // the diff that marks the block rather than the one that marks hashtags.
  const blockChange = preview?.kind === 'ApplyMarkers' || preview?.kind === 'RemoveMarkers';

  // What the box can say about the spelling as it is typed. While it has something to say, the
  // server's refusal — which would say the same thing a debounce later — stays out of the way.
  // An Apply has no box, so it has nothing to say either.
  const problem = markerScope ? undefined : validateSpelling(target);

  // The one question a Merge cannot answer for itself (ADR-0014). Until it is answered the button
  // is held, the same way an unconfirmed Merge is.
  const needsSurvivor = (rules?.requiresSurvivorChoice ?? false) && survivingMarker === undefined;

  return (
    <Dialog open={open} onOpenChange={(_event, data) => !data.open && onDismiss()}>
      <DialogSurface className={styles.surface}>
        <DialogBody>
          <DialogTitle>{markerScope ? markerScope.label : title(sources)}</DialogTitle>

          <DialogContent className={styles.section}>
            {markerScope?.action === 'apply' && (
              <Caption1>
                Markers go at the front of each task, in the order of your list. Applying adds and
                reorders them; it never takes one away.
              </Caption1>
            )}

            {markerScope?.action === 'remove' && (
              <Caption1>
                This takes markers off the front of tasks that no longer carry their hashtag, and
                off tasks whose rule you deleted. A task that still carries its hashtag keeps its
                marker, and an emoji you typed yourself is never touched. Undo puts them all back.
              </Caption1>
            )}

            {!markerScope && (
            <Field
              label="New spelling"
              hint={problem === undefined ? SPELLING_HINT : undefined}
              validationState={problem === undefined ? undefined : 'error'}
              validationMessage={problem}
              className={styles.target}
            >
              <Input
                value={target}
                onChange={(_event, data) => onTargetChange(data.value)}
                contentBefore={<Text>#</Text>}
                disabled={confirming}
              />
            </Field>
            )}

            {rules && (
              <MarkerRuleNotice
                rules={rules}
                target={preview?.targetSpelling ?? target}
                chosen={survivingMarker}
                onChoose={onChooseSurvivingMarker}
                disabled={confirming}
                styles={styles}
              />
            )}

            {error && problem === undefined && (
              <MessageBar intent="error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}

            {merge && (
              <MessageBar intent="warning">
                <MessageBarBody>
                  <MessageBarTitle>This merges hashtags into one.</MessageBarTitle> Afterwards
                  TodoWerk cannot tell them apart again — renaming them back would not restore which
                  task had which. You can undo the whole change for 30 days.
                </MessageBarBody>
              </MessageBar>
            )}

            {previewing && <Spinner size="tiny" labelPosition="after" label="Working out what would change…" />}

            {preview && (
              <>
                <Subtitle2>
                  {preview.taskCount === 1 ? '1 task would change' : `${preview.taskCount} tasks would change`}
                </Subtitle2>
                <Caption1>
                  A count, not a promise: each task is read again immediately before it is written,
                  so any that have moved on since are skipped and reported.
                </Caption1>

                <div className={styles.pairs}>
                  {/*
                    Keyed by position, because nothing here is unique: two tasks in one list may
                    carry the same title, and the task id deliberately never crosses the wire. The
                    list is replaced wholesale by each new preview and never reordered in place,
                    which is the case position keys are correct for.
                  */}
                  {pairs.slice(0, VISIBLE_PAIRS).map((item, position) => {
                    const titles = blockChange
                      ? diffBlocks(item.currentTitle, item.newTitle)
                      : diffTitles(item.currentTitle, item.newTitle);

                    return (
                      <div key={`${item.taskListId}:${position}`} className={styles.pair}>
                        <Text className={styles.before}>
                          <MarkedTitle parts={titles.before} kind="removed" />
                        </Text>
                        <Text aria-hidden>→</Text>
                        <Text className={styles.after}>
                          <MarkedTitle parts={titles.after} kind="added" />
                        </Text>
                      </div>
                    );
                  })}
                  {hidden > 0 && <Caption1>…and {hidden} more.</Caption1>}
                </div>

                {skips.length > 0 && (
                  <MessageBar intent="info">
                    <MessageBarBody>
                      <MessageBarTitle>
                        {skips.length === 1
                          ? '1 task will be left alone.'
                          : `${skips.length} tasks will be left alone.`}
                      </MessageBarTitle>
                      <ul className={styles.excluded}>
                        {skips.slice(0, VISIBLE_PAIRS).map((skip, position) => (
                          <li key={`${skip.taskListId}:${position}`}>
                            <Body1>
                              {skip.currentTitle} — {skip.reason}
                            </Body1>
                          </li>
                        ))}
                      </ul>
                      {hiddenSkips > 0 && <Caption1>…and {hiddenSkips} more.</Caption1>}
                    </MessageBarBody>
                  </MessageBar>
                )}

                {preview.excludedLists.length > 0 && (
                  <MessageBar intent="info">
                    <MessageBarBody>
                      <MessageBarTitle>Some lists are not included.</MessageBarTitle> TodoWerk only
                      changes lists it has read end to end, so nothing is missed halfway through one.
                      <ul className={styles.excluded}>
                        {preview.excludedLists.map((list) => (
                          <li key={list.taskListId}>
                            <Body1>
                              {list.displayName} — {list.reason}
                            </Body1>
                          </li>
                        ))}
                      </ul>
                    </MessageBarBody>
                  </MessageBar>
                )}
              </>
            )}
          </DialogContent>

          <DialogActions>
            <Button appearance="secondary" onClick={onDismiss} disabled={confirming}>
              Cancel
            </Button>
            <Button
              appearance="primary"
              onClick={onConfirm}
              disabled={
                confirming || previewing || preview === undefined || problem !== undefined || needsSurvivor
              }
            >
              {confirmLabel(preview, merge)}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

/**
 * What this Change would do to the Marker Rules it involves, and — where two marked hashtags are
 * being folded together — the one question nobody but the user can answer (ADR-0014).
 *
 * Nothing here writes a title. A rule follows a Rename to the new name and a Merge's losing rules
 * are deleted, which is worth saying in the moment somebody agrees to it rather than afterwards.
 */
function MarkerRuleNotice({
  rules,
  target,
  chosen,
  onChoose,
  disabled,
  styles,
}: {
  rules: ChangeMarkerRules;
  target: string;
  chosen: string | undefined;
  onChoose: (marker: string) => void;
  disabled: boolean;
  styles: Record<string, string>;
}) {
  if (rules.sourceRules.length === 0 && rules.targetRule === null) return null;

  if (rules.requiresSurvivorChoice) {
    return (
      <MessageBar intent="warning">
        <MessageBarBody>
          <MessageBarTitle>These hashtags carry different markers.</MessageBarTitle> Only one marker
          can stay on the hashtag they become. Choose which one:
          <div className={styles.survivors} role="group" aria-label="Which marker survives">
            {rules.sourceRules.map((rule) => (
              <Button
                key={rule.key}
                appearance={rule.marker === chosen ? 'primary' : 'secondary'}
                aria-pressed={rule.marker === chosen}
                disabled={disabled}
                onClick={() => onChoose(rule.marker)}
              >
                <span className={styles.marker}>{rule.marker}</span>
                <span className={styles.tag}>&nbsp;from #{rule.spelling}</span>
              </Button>
            ))}
          </div>
        </MessageBarBody>
      </MessageBar>
    );
  }

  return (
    <MessageBar intent="info">
      <MessageBarBody>{ruleSentence(rules, target, styles)}</MessageBarBody>
    </MessageBar>
  );
}

/** One sentence about the markers, in the same shape whichever of the three cases it is. */
function ruleSentence(rules: ChangeMarkerRules, target: string, styles: Record<string, string>) {
  const survivor = <span className={styles.marker}>{rules.survivingMarker}</span>;

  if (rules.targetRule !== null) {
    const losing = rules.sourceRules.map((rule) => `#${rule.spelling}`).join(', ');

    return rules.sourceRules.length === 0 ? (
      <>
        The marker {survivor} stays on{' '}
        <span className={styles.tag}>#{rules.targetRule.spelling}</span>.
      </>
    ) : (
      <>
        The marker {survivor} stays, and the marker rule for{' '}
        <span className={styles.tag}>{losing}</span> is deleted. Markers already on tasks are left
        where they are.
      </>
    );
  }

  return (
    <>
      The marker rule {survivor} moves to <span className={styles.tag}>#{target}</span>. Markers
      already on tasks are left where they are until you apply your markers again.
    </>
  );
}

function title(sources: string[]): string {
  if (sources.length === 1) return `Change #${sources[0]}`;

  return `Combine ${sources.length} hashtags`;
}

/** The count the dialog already shows, on the button that commits to it. */
function confirmLabel(preview: ChangePreview | undefined, merge: boolean): string {
  const verb =
    preview?.kind === 'ApplyMarkers'
      ? 'Mark'
      : preview?.kind === 'RemoveMarkers'
        ? 'Clear'
        : merge
          ? 'Merge'
          : 'Change';
  const what = !preview ? 'them' : preview.taskCount === 1 ? '1 task' : `${preview.taskCount} tasks`;

  return `${verb} ${what}`;
}

/**
 * A title with the Hashtags a Change touches picked out: `<mark>` for the one it puts in, `<s>`
 * for the one it takes out — the two elements that mean exactly that.
 */
function MarkedTitle({ parts, kind }: { parts: TitlePart[]; kind: 'added' | 'removed' }) {
  const styles = useStyles();
  const Marked = kind === 'added' ? 'mark' : 's';
  const className = kind === 'added' ? styles.mark : styles.struck;

  return (
    <>
      {parts.map((part, position) =>
        part.changed ? (
          <Marked key={position} className={className}>
            {part.text}
          </Marked>
        ) : (
          <Fragment key={position}>{part.text}</Fragment>
        ),
      )}
    </>
  );
}
