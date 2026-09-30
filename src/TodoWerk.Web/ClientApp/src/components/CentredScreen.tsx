import { makeStyles, tokens } from '@fluentui/react-components';
import type { ReactNode } from 'react';

const useStyles = makeStyles({
  page: {
    display: 'flex',
    justifyContent: 'center',
    paddingBlock: tokens.spacingVerticalXXL,
  },
});

/**
 * Where a card that stands in for a screen sits: centred, a little way down. The sign-in card and
 * the denied card take turns in the same place, and the one layout keeps them from sitting at two
 * different offsets when the Licence state flips from one to the other.
 */
export function CentredScreen({ children }: { children: ReactNode }) {
  const styles = useStyles();

  return <div className={styles.page}>{children}</div>;
}
