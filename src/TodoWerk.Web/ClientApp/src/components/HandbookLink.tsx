import { Link, MenuItem, makeStyles, tokens } from '@fluentui/react-components';
import { OpenRegular } from '@fluentui/react-icons';
import type { ReactNode } from 'react';
import { useTeamsHost } from '../host';

/**
 * The Administrator's Guide, which two surfaces link to directly rather than as one of the
 * documents: the sign-in card, when a consent decision is what stopped somebody, and the Tenant
 * Consent invitation on the Tenant Overview. Both are deep links to a particular page for a
 * particular reason, so neither reads the document list. The address is written once here rather
 * than in each of them, so a path change has one place to reach, and `HandbookPageTests` asserts
 * the server still serves it.
 *
 * Server half: `HandbookPages.AdministratorsPath`.
 */
export const administratorsPath = '/administrators';

// The pop-out glyph after a link that leaves the frame, so the link says so before it is clicked.
// In the browser SPA the same link opens a new tab, which is the same promise.
const useStyles = makeStyles({
  popOut: {
    fontSize: '14px',
    verticalAlign: '-2px',
    marginInlineStart: tokens.spacingHorizontalXXS,
  },
});

interface HandbookLinkProps {
  /** A Handbook page: a path on this host, or the Guide's absolute address. */
  href: string;
  className?: string;
  children: ReactNode;
}

/** Where a Handbook page is, and the way to it that leaves the frame it was asked for from. */
interface HandbookDestination {
  url: string;
  /**
   * Null in the browser, where an anchor already opens a new tab. Inside the Teams tab it is the
   * way out of the frame: a link there opens inside Teams, and the pages this points at refuse to
   * be framed (ADR-0010, ADR-0013).
   */
  outOfFrame: (() => void) | null;
}

function useHandbookDestination(href: string): HandbookDestination {
  const host = useTeamsHost();
  const url = new URL(href, window.location.origin).toString();

  return { url, outOfFrame: host === null ? null : () => host.openInBrowser(url) };
}

/**
 * A link to a Handbook page — the Guide, the About Page, or the page for administrators — that
 * leaves the frame it is clicked in.
 *
 * In the browser it opens a new tab, because the reader is in the middle of something and the
 * Handbook is a thing to read beside it. In the Teams tab it opens the person's own browser,
 * through the same host call the way out of the frame uses.
 */
export function HandbookLink({ href, className, children }: HandbookLinkProps) {
  const styles = useStyles();
  const { url, outOfFrame } = useHandbookDestination(href);

  return (
    <Link
      href={url}
      target="_blank"
      rel="noopener"
      className={className}
      onClick={
        outOfFrame === null
          ? undefined
          : (event) => {
              event.preventDefault();
              outOfFrame();
            }
      }
    >
      {children}
      <OpenRegular className={styles.popOut} aria-hidden />
    </Link>
  );
}

/**
 * The same destination, reached from a menu.
 *
 * It opens the address itself rather than wrapping an anchor, because Fluent renders a `MenuItem`
 * as a `div` and an anchor inside one would be a second interactive element inside a `menuitem` —
 * two things to activate where the menu presents one. Everything else is identical to the link
 * above, the tab's way out of the frame included.
 */
export function HandbookMenuItem({ href, children }: Omit<HandbookLinkProps, 'className'>) {
  const { url, outOfFrame } = useHandbookDestination(href);

  return (
    <MenuItem
      icon={<OpenRegular />}
      onClick={
        outOfFrame ??
        (() => {
          window.open(url, '_blank', 'noopener');
        })
      }
    >
      {children}
    </MenuItem>
  );
}
