import { makeStyles } from '@fluentui/react-components';

/**
 * Running text at a readable measure, the same on every screen. A paragraph left to the panel's
 * edge runs to 150 characters a line at Fluent's body size, twice the comfortable measure; tiles,
 * tables and buttons keep the panel's full width.
 */
export const useProseStyles = makeStyles({
  prose: {
    maxWidth: '68ch',
  },
});
