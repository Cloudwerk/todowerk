import {
  Badge,
  Body1,
  Button,
  Caption1,
  Divider,
  Subtitle2,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import type { HashtagInventoryRow } from '../api/types';
import { formatDate } from './formatDate';

const useStyles = makeStyles({
  // Sticky, because the table beside it is fifty rows tall: selecting the fortieth row must not
  // leave the facts about it, and the one button that acts on it, far above the viewport.
  root: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    minWidth: '260px',
    position: 'sticky',
    top: tokens.spacingVerticalL,
    alignSelf: 'flex-start',
  },
  tag: {
    fontFamily: tokens.fontFamilyMonospace,
  },
  facts: {
    display: 'grid',
    gridTemplateColumns: 'auto 1fr',
    columnGap: tokens.spacingHorizontalM,
    rowGap: tokens.spacingVerticalXS,
  },
  spellings: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXXS,
  },
  actions: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    alignItems: 'stretch',
  },
  empty: {
    color: tokens.colorNeutralForeground3,
  },
});

interface HashtagDetailPanelProps {
  /** Every row the user has selected. One fills the panel; several make a Merge possible. */
  rows: HashtagInventoryRow[];
  /** Opens the confirmation dialog for these sources, with a spelling suggested. */
  onStartChange: (sources: HashtagInventoryRow[], suggestedTarget: string) => void;
  /** True while a Change is already queued or running: one per user at a time (ADR-0006). */
  changeInFlight: boolean;
}

/**
 * The detail side of the Workbench: what one Hashtag is and where it is used, and the entry point
 * to changing it. The confirmation itself is a dialog — a thousand old-title/new-title pairs do
 * not fit in a side panel, and what gets confirmed has to be readable.
 */
export function HashtagDetailPanel({ rows, onStartChange, changeInFlight }: HashtagDetailPanelProps) {
  const styles = useStyles();

  if (rows.length === 0) {
    return (
      <aside className={styles.root} aria-label="Hashtag details" aria-live="polite">
        <Body1 className={styles.empty}>
          Select a hashtag to see where it is used. Select several to combine them into one.
        </Body1>
      </aside>
    );
  }

  const single = rows.length === 1 ? rows[0] : undefined;

  // Busiest first, so combining several suggests the name most tasks already use.
  const busiest = rows.reduce((left, right) => (right.taskCount > left.taskCount ? right : left));

  return (
    <aside className={styles.root} aria-label="Hashtag details">
      {single ? (
        <SingleHashtag row={single} styles={styles} />
      ) : (
        <>
          <Subtitle2>{rows.length} hashtags selected</Subtitle2>
          <div className={styles.spellings}>
            {rows.map((row) => (
              <Text key={row.key} className={styles.tag}>
                #{row.canonicalSpelling} — {row.taskCount} tasks
              </Text>
            ))}
          </div>
        </>
      )}

      <Divider />

      <div className={styles.actions}>
        {/*
          One button, and the selection decides what it offers. A Merge is by definition several
          sources, so below two selected rows there is no merge entry point at all — a gate kept by
          the absence of a button rather than by a disabled one.
        */}
        {single ? (
          <Button
            appearance="primary"
            disabled={changeInFlight}
            onClick={() => onStartChange([single], single.canonicalSpelling)}
          >
            {/* One span, not loose text + span: the Button root is inline-flex, and the
                space after "Change" collapses when it ends its own anonymous flex item. */}
            <span>
              Change <span className={styles.tag}>#{single.canonicalSpelling}</span>…
            </span>
          </Button>
        ) : (
          <Button
            appearance="primary"
            disabled={changeInFlight}
            onClick={() => onStartChange(rows, busiest.canonicalSpelling)}
          >
            Combine {rows.length} into one…
          </Button>
        )}
        {changeInFlight && (
          <Caption1>One change at a time. Let the running one finish, or cancel it.</Caption1>
        )}
      </div>
    </aside>
  );
}

function SingleHashtag({ row, styles }: { row: HashtagInventoryRow; styles: Record<string, string> }) {
  return (
    <>
      <Subtitle2 className={styles.tag}>#{row.canonicalSpelling}</Subtitle2>

      <div className={styles.facts}>
        <Caption1>Tasks</Caption1>
        <Text>{row.taskCount}</Text>
        <Caption1>Lists</Caption1>
        <Text>{row.listCount}</Text>
        <Caption1>Last used</Caption1>
        <Text>{formatDate(row.lastUsedAt)}</Text>
      </div>

      {row.spellings.length > 1 && (
        <>
          <Divider />
          <div className={styles.spellings}>
            <Caption1>
              Written {row.spellings.length} ways. These are one hashtag, not several — changing it
              without renaming settles them all on the spelling you choose.
            </Caption1>
            {row.spellings.map((spelling) => (
              <Text key={spelling} className={styles.tag}>
                #{spelling}
                {spelling === row.canonicalSpelling && (
                  <>
                    {' '}
                    <Badge appearance="tint" color="brand">
                      most used
                    </Badge>
                  </>
                )}
              </Text>
            ))}
          </div>
        </>
      )}

      {row.hasNearDuplicates && (
        <>
          <Divider />
          <Caption1>
            Another hashtag is spelled almost the same. Worth a look — they may well be two
            different things.
          </Caption1>
        </>
      )}

      {/* The same date "Last used" already shows, said as the sentence it makes: the flag is
          about the silence since then, not about the task count beside it. */}
      {row.isStale && (
        <>
          <Divider />
          <Caption1>
            None of its tasks has been edited since {formatDate(row.lastUsedAt)}. They are all
            still there — this says nothing has happened to them lately, not that there are none.
          </Caption1>
        </>
      )}
    </>
  );
}
