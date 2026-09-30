import { memo, useMemo } from 'react';
import {
  Badge,
  Button,
  DataGrid,
  DataGridBody,
  DataGridCell,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridRow,
  Text,
  Tooltip,
  createTableColumn,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import type { DataGridProps, TableColumnDefinition, TableColumnSizingOptions } from '@fluentui/react-components';
import { ClockRegular, CopyRegular, TextCaseTitleRegular } from '@fluentui/react-icons';
import type { HashtagInventoryRow, HashtagInventorySort, MarkerRule } from '../api/types';
import { formatDate } from './formatDate';

const useStyles = makeStyles({
  // The same edge every other panel on the screen has, so the grid reads as one of them.
  grid: {
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  tag: {
    fontFamily: tokens.fontFamilyMonospace,
  },
  flags: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
  },
  // Right-aligned and in tabular figures, so a column of counts can be scanned down its ones
  // column. A cell is a flex box, so the alignment is the cell's rather than the text's — and a
  // header cell's content sits in a sort button that fills the cell, so there it is the button's.
  numericCell: {
    justifyContent: 'flex-end',
  },
  numeric: {
    fontVariantNumeric: 'tabular-nums',
  },
  // Big enough to read as the emoji it is rather than as a glyph in a sentence.
  marker: {
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase400,
  },
  unmarked: {
    color: tokens.colorNeutralForeground3,
  },
  markerCell: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  coverage: {
    color: tokens.colorNeutralForeground3,
    fontVariantNumeric: 'tabular-nums',
    fontSize: tokens.fontSizeBase200,
  },
});

/**
 * Room by what each column holds: the spelling and the notes need it, a count does not. Ideal
 * widths are scaled to the container; the minimums are what a column keeps when it is dragged.
 */
const columnSizing: TableColumnSizingOptions = {
  marker: { minWidth: 96, idealWidth: 112, defaultWidth: 112 },
  Spelling: { minWidth: 200, idealWidth: 320 },
  TaskCount: { minWidth: 88, idealWidth: 96, defaultWidth: 96 },
  ListCount: { minWidth: 80, idealWidth: 88, defaultWidth: 88 },
  LastUsed: { minWidth: 120, idealWidth: 128 },
  flags: { minWidth: 180, idealWidth: 220 },
};

const numericColumns = new Set(['TaskCount', 'ListCount']);

interface HashtagInventoryTableProps {
  rows: HashtagInventoryRow[];
  sort: HashtagInventorySort;
  descending: boolean;
  onSortChange: (sort: HashtagInventorySort, descending: boolean) => void;
  /** Folded keys, because a Merge takes several sources and the server matches on the key. */
  selectedKeys: readonly string[];
  onSelectionChange: (keys: string[]) => void;
  /** This person's Marker Rules by folded key, so a row can show the one that is about it. */
  markers: ReadonlyMap<string, MarkerRule>;
  /** Opens the Set Marker dialog for one row. A rule declares; it writes no title (ADR-0014). */
  onSetMarker: (row: HashtagInventoryRow) => void;
}

/**
 * The inventory table, which CONTEXT.md calls the product itself — not a report and not a
 * dashboard. Memoised, because the page around it re-renders on every poll during a scan and
 * every prop here is stable between two polls that returned the same rows.
 *
 * Sorting is the server's: every sortable column's `compare` returns 0, so the grid renders the
 * order the query returned rather than re-sorting the page it can see. Sorting a page would sort
 * the wrong fifty rows. The comparators still declare two parameters, because Fluent decides
 * whether a header is clickable from the comparator's arity — a zero-argument `() => 0` silently
 * turns every header into a plain label and makes the server sort unreachable.
 */
export const HashtagInventoryTable = memo(function HashtagInventoryTable({
  rows,
  sort,
  descending,
  onSortChange,
  selectedKeys,
  onSelectionChange,
  markers,
  onSetMarker,
}: HashtagInventoryTableProps) {
  const styles = useStyles();

  const columns: TableColumnDefinition<HashtagInventoryRow>[] = useMemo(
    () => [
      // Not sortable: the server sorts, and "by marker" is not one of the orders it knows. The
      // column is where a rule is both shown and written, so a row without one still has a
      // control rather than an empty cell.
      createTableColumn<HashtagInventoryRow>({
        columnId: 'marker',
        renderHeaderCell: () => 'Marker',
        renderCell: (row) => {
          const rule = markers.get(row.key);

          return (
            <Tooltip content={markerTooltip(rule)} relationship="description">
              <Button
                appearance="subtle"
                size="small"
                className={styles.markerCell}
                aria-label={`Marker for #${row.canonicalSpelling}`}
                onClick={() => onSetMarker(row)}
              >
                <span className={rule ? styles.marker : styles.unmarked}>
                  {rule ? rule.marker : 'Set marker'}
                </span>
                {/* On the row as well as in the tooltip: how far a rule has got is a figure to be
                    scanned down a column, and a hover is not a place a figure can be scanned. */}
                {rule && rule.taggedTaskCount > 0 && (
                  <span className={styles.coverage}>
                    {rule.markedTaskCount}/{rule.taggedTaskCount}
                  </span>
                )}
              </Button>
            </Tooltip>
          );
        },
      }),
      createTableColumn<HashtagInventoryRow>({
        columnId: 'Spelling',
        compare: (_a: HashtagInventoryRow, _b: HashtagInventoryRow) => 0,
        renderHeaderCell: () => 'Hashtag',
        renderCell: (row) => (
          <Text className={styles.tag} weight="semibold">
            #{row.canonicalSpelling}
          </Text>
        ),
      }),
      createTableColumn<HashtagInventoryRow>({
        columnId: 'TaskCount',
        compare: (_a: HashtagInventoryRow, _b: HashtagInventoryRow) => 0,
        renderHeaderCell: () => 'Tasks',
        renderCell: (row) => <Text className={styles.numeric}>{row.taskCount}</Text>,
      }),
      createTableColumn<HashtagInventoryRow>({
        columnId: 'ListCount',
        compare: (_a: HashtagInventoryRow, _b: HashtagInventoryRow) => 0,
        renderHeaderCell: () => 'Lists',
        renderCell: (row) => <Text className={styles.numeric}>{row.listCount}</Text>,
      }),
      createTableColumn<HashtagInventoryRow>({
        columnId: 'LastUsed',
        compare: (_a: HashtagInventoryRow, _b: HashtagInventoryRow) => 0,
        renderHeaderCell: () => 'Last used',
        renderCell: (row) => <Text>{formatDate(row.lastUsedAt)}</Text>,
      }),
      createTableColumn<HashtagInventoryRow>({
        columnId: 'flags',
        renderHeaderCell: () => 'Notes',
        renderCell: (row) => <Flags row={row} className={styles.flags} />,
      }),
    ],
    [styles, markers, onSetMarker],
  );

  const sortState: DataGridProps['sortState'] = useMemo(
    () => ({ sortColumn: sort, sortDirection: descending ? 'descending' : 'ascending' }),
    [sort, descending],
  );

  const selectedItems = useMemo(() => [...selectedKeys], [selectedKeys]);

  const handleSortChange: DataGridProps['onSortChange'] = (_event, next) => {
    onSortChange(next.sortColumn as HashtagInventorySort, next.sortDirection === 'descending');
  };

  const handleSelectionChange: DataGridProps['onSelectionChange'] = (_event, data) => {
    onSelectionChange([...data.selectedItems].map(String));
  };

  const numeric = (columnId: string | number) => numericColumns.has(String(columnId));

  return (
    <DataGrid
      className={styles.grid}
      items={rows}
      columns={columns}
      sortable
      sortState={sortState}
      onSortChange={handleSortChange}
      // Several rows, because a Merge takes several sources. The single-row path is unchanged:
      // selecting one row is still what fills the detail panel.
      selectionMode="multiselect"
      selectedItems={selectedItems}
      onSelectionChange={handleSelectionChange}
      getRowId={(row) => row.key}
      focusMode="composite"
      subtleSelection
      columnSizingOptions={columnSizing}
      resizableColumns
    >
      <DataGridHeader>
        <DataGridRow selectionCell={{ 'aria-label': 'Select hashtag' }}>
          {({ renderHeaderCell, columnId }) => (
            <DataGridHeaderCell button={numeric(columnId) ? { className: styles.numericCell } : undefined}>
              {renderHeaderCell()}
            </DataGridHeaderCell>
          )}
        </DataGridRow>
      </DataGridHeader>
      <DataGridBody<HashtagInventoryRow>>
        {({ item, rowId }) => (
          <DataGridRow<HashtagInventoryRow> key={rowId} selectionCell={{ 'aria-label': 'Select hashtag' }}>
            {({ renderCell, columnId }) => (
              <DataGridCell className={numeric(columnId) ? styles.numericCell : undefined}>
                {renderCell(item)}
              </DataGridCell>
            )}
          </DataGridRow>
        )}
      </DataGridBody>
    </DataGrid>
  );
});

/**
 * What the marker button says it is for, and — where there is a rule — how far it has got. The
 * figure is as fresh as the last scan, which the freshness chrome above the table already says.
 */
function markerTooltip(rule: MarkerRule | undefined): string {
  if (!rule) return 'Give this hashtag a marker';

  return rule.taggedTaskCount === 0
    ? `${rule.marker} — no tagged tasks yet`
    : `${rule.markedTaskCount} of ${rule.taggedTaskCount} tagged tasks carry ${rule.marker}`;
}

/**
 * Flags read as advice, never as alarms. Somebody with 200 tags and 40 near-duplicate pairs
 * must not open TodoWerk to a wall of red, so these are tinted badges in informative and
 * warning tones — nothing here is an error, and none of it is wrong on purpose. Each carries a
 * glyph as well as a word, so a column of them can be told apart at a glance.
 *
 * Each explanation lives in a Fluent Tooltip rather than a bare `title`: the tooltip is wired
 * to the badge as its accessible description, reachable by keyboard focus, and works on touch —
 * three things a `title` attribute does not do.
 */
function Flags({ row, className }: { row: HashtagInventoryRow; className: string }) {
  return (
    <div className={className}>
      {row.hasMultipleSpellings && (
        <Tooltip
          content={row.spellings.map((spelling) => `#${spelling}`).join(', ')}
          relationship="description"
        >
          <Badge appearance="tint" color="brand" icon={<TextCaseTitleRegular />} tabIndex={0}>
            {row.spellings.length} spellings
          </Badge>
        </Tooltip>
      )}
      {row.hasNearDuplicates && (
        <Tooltip content="Another hashtag is one character away." relationship="description">
          <Badge appearance="tint" color="warning" icon={<CopyRegular />} tabIndex={0}>
            similar
          </Badge>
        </Tooltip>
      )}
      {/* Not "unused", which the count of the tasks carrying it on the same row would contradict.
          All the flag knows is when those tasks were last edited, so the tooltip says since when
          and the badge stays the one word a column can be scanned down. */}
      {row.isStale && (
        <Tooltip
          content={`No task carrying this hashtag has been edited since ${formatDate(row.lastUsedAt)}.`}
          relationship="description"
        >
          <Badge appearance="tint" color="informative" icon={<ClockRegular />} tabIndex={0}>
            stale
          </Badge>
        </Tooltip>
      )}
    </div>
  );
}
