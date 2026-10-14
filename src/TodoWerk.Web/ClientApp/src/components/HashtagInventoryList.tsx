import { memo } from 'react';
import { Button, Caption1, Text, Tooltip, makeStyles, tokens } from '@fluentui/react-components';
import type { HashtagInventoryRow, MarkerRule } from '../api/types';
import { formatDate } from './formatDate';
import { Flags, markerTooltip } from './HashtagInventoryTable';

const useStyles = makeStyles({
  // The same edge as the table it stands in for.
  list: {
    margin: 0,
    padding: 0,
    listStyle: 'none',
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  row: {
    display: 'grid',
    gridTemplateColumns: 'auto minmax(0, 1fr)',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalS,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    ':last-child': {
      borderBottom: 'none',
    },
  },
  // One width whether it holds an emoji or the words that invite one, so the hashtags line up.
  markerButton: {
    width: '88px',
    minWidth: '88px',
  },
  marker: {
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase400,
  },
  unmarked: {
    color: tokens.colorNeutralForeground3,
  },
  coverage: {
    color: tokens.colorNeutralForeground3,
    fontVariantNumeric: 'tabular-nums',
    fontSize: tokens.fontSizeBase200,
    marginInlineStart: tokens.spacingHorizontalXXS,
  },
  about: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  tag: {
    fontFamily: tokens.fontFamilyMonospace,
    overflowWrap: 'anywhere',
  },
  facts: {
    color: tokens.colorNeutralForeground3,
    fontVariantNumeric: 'tabular-nums',
  },
  flags: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
  },
});

interface HashtagInventoryListProps {
  rows: HashtagInventoryRow[];
  /** This person's Marker Rules by folded key, so a row can show the one that is about it. */
  markers: ReadonlyMap<string, MarkerRule>;
  /** Opens the Set Marker dialog for one row. A rule declares; it writes no title (ADR-0014). */
  onSetMarker: (row: HashtagInventoryRow) => void;
}

/**
 * The inventory on a phone. The table's six columns and the detail panel beside it need a desktop
 * screen; here each hashtag is one row, with what the table's counts and date say folded into a
 * line under it. The order is the server's, as in the table.
 *
 * No selection: selecting is how the table starts a rename or a merge, and those are not offered
 * at this width. Setting a marker is, because it writes no task — applying it does, and that is
 * left to a wider screen with the rest.
 */
export const HashtagInventoryList = memo(function HashtagInventoryList({
  rows,
  markers,
  onSetMarker,
}: HashtagInventoryListProps) {
  const styles = useStyles();

  return (
    <ul className={styles.list} aria-label="Hashtags">
      {rows.map((row) => {
        const rule = markers.get(row.key);

        return (
          <li key={row.key} className={styles.row}>
            <Tooltip content={markerTooltip(rule)} relationship="description">
              <Button
                appearance="subtle"
                size="small"
                className={styles.markerButton}
                aria-label={`Marker for #${row.canonicalSpelling}`}
                onClick={() => onSetMarker(row)}
              >
                <span className={rule ? styles.marker : styles.unmarked}>
                  {rule ? rule.marker : 'Set marker'}
                </span>
                {rule && rule.taggedTaskCount > 0 && (
                  <span className={styles.coverage}>
                    {rule.markedTaskCount}/{rule.taggedTaskCount}
                  </span>
                )}
              </Button>
            </Tooltip>

            <div className={styles.about}>
              <Text className={styles.tag} weight="semibold">
                #{row.canonicalSpelling}
              </Text>
              <Caption1 className={styles.facts}>
                {tasks(row.taskCount)} in {lists(row.listCount)}, last used {formatDate(row.lastUsedAt)}
              </Caption1>
              <Flags row={row} className={styles.flags} />
            </div>
          </li>
        );
      })}
    </ul>
  );
});

function tasks(count: number): string {
  return count === 1 ? '1 task' : `${count} tasks`;
}

function lists(count: number): string {
  return count === 1 ? '1 list' : `${count} lists`;
}
