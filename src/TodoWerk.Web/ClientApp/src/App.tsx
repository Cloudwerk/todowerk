import { Fragment, useRef, useState } from 'react';
import {
  Menu,
  MenuButton,
  MenuDivider,
  MenuGroup,
  MenuGroupHeader,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  Text,
  Title3,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { OpenRegular, QuestionCircle20Regular } from '@fluentui/react-icons';
import { NavLink, Outlet } from 'react-router';
import { getCurrentUser, getHandbook } from './api/client';
import type { HandbookDocument } from './api/types';
import { useApi } from './api/useApi';
import { EraseMeDialog } from './components/EraseMeDialog';
import { HandbookMenuItem } from './components/HandbookLink';
import { LicenceDeniedCard } from './components/LicenceDeniedCard';
import { useOpenInBrowser } from './components/OpenInBrowserButton';
import { SignOutForm } from './components/SignOutForm';
import { useTeamsHost } from './host';
import { LicenceProvider, useLicence } from './licence';

const useStyles = makeStyles({
  root: {
    minHeight: '100vh',
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: tokens.colorNeutralBackground2,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalXXL}`,
    backgroundColor: tokens.colorBrandBackground,
  },
  title: {
    color: tokens.colorNeutralForegroundOnBrand,
  },
  // At full strength: the size and weight beside the name already rank it, and a blend is a
  // colour the contrast suite cannot see.
  subtitle: {
    color: tokens.colorNeutralForegroundOnBrand,
  },
  nav: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalL,
    marginInlineStart: tokens.spacingHorizontalL,
  },
  // On the anchor itself, not on a wrapper: `NavLink` renders the `<a>`, and a class on something
  // around it leaves the browser's own link colours in charge — blue on the brand header, purple
  // once visited. The same trap the session menu documents for a Fluent button's own foreground.
  //
  // The current screen carries a rule under it and hover underlines: two states, two shapes, so
  // hovering the other screen never looks like having arrived at it.
  navLink: {
    color: tokens.colorNeutralForegroundOnBrand,
    textDecorationLine: 'none',
    fontWeight: tokens.fontWeightRegular,
    paddingBlockEnd: tokens.spacingVerticalXXS,
    borderBottom: `${tokens.strokeWidthThick} solid transparent`,
    ':hover': {
      color: tokens.colorNeutralForegroundOnBrand,
      textDecorationLine: 'underline',
    },
    ':visited': {
      color: tokens.colorNeutralForegroundOnBrand,
    },
  },
  navLinkCurrent: {
    fontWeight: tokens.fontWeightSemibold,
    borderBottomColor: tokens.colorNeutralForegroundOnBrand,
  },
  session: {
    marginInlineStart: 'auto',
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  // A Fluent button paints its own neutral foreground — a dark grey meant for neutral surfaces —
  // and that class wins over the colour inherited from the header, leaving the name looking
  // disabled. Every interactive state needs the override, the open menu included. Both buttons in
  // the header take it: the person's name, and the help control beside it.
  headerButton: {
    color: tokens.colorNeutralForegroundOnBrand,
    ':hover': {
      color: tokens.colorNeutralForegroundOnBrand,
    },
    ':hover:active': {
      color: tokens.colorNeutralForegroundOnBrand,
    },
    '&[aria-expanded="true"]': {
      color: tokens.colorNeutralForegroundOnBrand,
    },
  },
  main: {
    flexGrow: 1,
    padding: tokens.spacingHorizontalXXL,
  },
  // Wrapped, and given a measure: a Fluent menu grows to its longest item, and a two-line sentence
  // here would otherwise stretch the menu to the width of the sentence.
  signedInNote: {
    maxWidth: '260px',
    whiteSpace: 'normal',
    lineHeight: tokens.lineHeightBase200,
  },
});

/** The current route gets weight and a rule rather than a colour, because the header has only one colour. */
function navLinkClass(styles: ReturnType<typeof useStyles>) {
  return ({ isActive }: { isActive: boolean }) =>
    isActive ? `${styles.navLink} ${styles.navLinkCurrent}` : styles.navLink;
}

/**
 * The shell every screen is drawn inside, in both hosts.
 *
 * The Licence is read here, once, and given to everything below: the banner on the Workbench, the
 * panel and the consent invitation on the Tenant Overview, and the card that replaces all three
 * when somebody is denied. One read and one shape, so no two surfaces can disagree about the same
 * person.
 */
export function App() {
  return (
    <LicenceProvider>
      <Shell />
    </LicenceProvider>
  );
}

function Shell() {
  const styles = useStyles();
  const user = useApi(getCurrentUser);
  const handbook = useApi(getHandbook);
  const inTeams = useTeamsHost() !== null;
  const licence = useLicence();

  return (
    <div className={styles.root}>
      <header className={styles.header}>
        {/* The name and the subtitle only outside a host: Teams, Outlook and the Microsoft 365 app
            already print the app's name above the frame, so the tab does not repeat it. The nav is
            then the first thing in the bar. */}
        {!inTeams && (
          <>
            <Title3 as="h1" className={styles.title}>
              TodoWerk
            </Title3>
            <Text className={styles.subtitle}>Hashtag Manager</Text>
          </>
        )}
        {user.status === 'ready' && (
          <nav className={styles.nav}>
            {/* Named by what the screen shows. "Workbench" is CONTEXT.md's word for the screen,
                and a person using it never needs it. */}
            <NavLink to="/" className={navLinkClass(styles)} end>
              Hashtags
            </NavLink>
            <NavLink to="/tenant" className={navLinkClass(styles)}>
              Your organisation
            </NavLink>
          </nav>
        )}
        {/*
          The screens and the session controls belong to somebody signed in; the documents do not.
          ADR-0013 insists the About Page and the page for administrators are anonymous *because*
          they are read by people who have not signed in and may never, such as an administrator
          deciding whether to allow the product. So the help control is outside the gate and the
          rest is inside it. `/api/handbook` is anonymous already, so there is nothing to arrange
          for that.
        */}
        <div className={styles.session}>
          <HelpMenu documents={handbook.status === 'ready' ? handbook.data.documents : []} />
          {user.status === 'ready' && (
            <SessionMenu displayName={user.data.displayName} inTeams={inTeams} />
          )}
        </div>
      </header>
      <main className={styles.main}>
        {/*
          In place of the screens, not on top of them, and inside the shell rather than instead of
          it: the header above carries the way to ask for erasure, which is an obligation and
          cannot depend on a Licence, and inside the tab it carries the way out to a browser.
        */}
        {licence.status === 'denied' ? (
          <LicenceDeniedCard
            code={licence.code}
            detail={licence.detail}
            contact={licence.contact}
            purchaseUrl={licence.purchaseUrl}
          />
        ) : (
          <Outlet />
        )}
      </main>
    </div>
  );
}

/**
 * The Handbook and the legal documents, behind one control.
 *
 * Reading is not a third screen, so the Guide is not in the screen nav. The page for
 * administrators is here as well as on the two surfaces that ask for an approval, because a tenant
 * that approved TodoWerk long ago still needs a route to it; and the About Page is the one the App
 * Package names as this installation's support link. They are one kind of thing, so they are in
 * one place, apart from the two screens and apart from the session controls: a place to read
 * rather than a screen to work on or something to do to your account.
 *
 * What is in it is the server's answer and nothing else — the set, the order, the wording and the
 * addresses all arrive from `GET /api/handbook`, which builds them from the same `DocumentNav` the
 * header of every served document is built from. This component holds no paths and no order of
 * its own, so the menu and the served pages cannot disagree.
 *
 * Absent entries need no handling of their own. A Self-Host has no Guide, so the list does not
 * carry one (ADR-0013); a list that has not arrived yet is empty, and an empty menu is no control
 * rather than a control that opens on nothing.
 */
function HelpMenu({ documents }: { documents: HandbookDocument[] }) {
  const styles = useStyles();

  if (documents.length === 0) {
    return null;
  }

  return (
    <Menu positioning="below-end">
      <MenuTrigger disableButtonEnhancement>
        <Tooltip content="Help and about" relationship="label">
          {/* No menu chevron: at this size the icon is the whole control, and a second glyph
              beside it reads as a disclosure on the header rather than a way in to the reading. */}
          <MenuButton
            appearance="transparent"
            className={styles.headerButton}
            icon={<QuestionCircle20Regular />}
            menuIcon={null}
            aria-label="Help and about"
          />
        </Tooltip>
      </MenuTrigger>
      <MenuPopover>
        <MenuList>
          {documents.map((document, index) => (
            <Fragment key={document.href}>
              {/* Where the kind changes, as in the session menu: above it the product explaining
                  itself, below it what somebody signed in under. Drawn off the group rather than
                  at a counted position, so a sixth document lands on the right side of it without
                  anybody remembering this line exists. */}
              {index > 0 && documents[index - 1].group !== document.group && <MenuDivider />}
              <HandbookMenuItem href={document.href}>{document.text}</HandbookMenuItem>
            </Fragment>
          ))}
        </MenuList>
      </MenuPopover>
    </Menu>
  );
}

/**
 * The session controls, behind the person's name.
 *
 * Erasure is an obligation rather than a convenience, and a surface that hides it is not a
 * complete product — so it is offered in the tab as well as in the browser. It does not have to
 * sit at the same weight as sign-out on every screen, and here it is one deliberate step further
 * away, below a divider, and worded so nobody presses it thinking they are signing out.
 *
 * No sign-out in the Teams tab, and absent rather than disabled. The identity there is the Teams
 * identity: signing out of TodoWerk while staying signed into Teams is a state the next tab load
 * silently undoes, so the control would be a lie about what it did. What takes its place is the
 * way out of the frame — the same route the blocked-cookie card and the manifest's websiteUrl
 * point at (ADR-0010) — and, above it, one sentence saying where sign-out lives, so its absence is
 * not read as a missing control.
 */
function SessionMenu({ displayName, inTeams }: { displayName: string; inTeams: boolean }) {
  const styles = useStyles();
  const signOutForm = useRef<HTMLFormElement>(null);
  const openInBrowser = useOpenInBrowser();
  const [erasing, setErasing] = useState(false);

  return (
    <>
      <Menu positioning="below-end">
        <MenuTrigger disableButtonEnhancement>
          <MenuButton appearance="transparent" className={styles.headerButton}>
            {displayName}
          </MenuButton>
        </MenuTrigger>
        <MenuPopover>
          <MenuList>
            {inTeams ? (
              <MenuGroup>
                <MenuGroupHeader className={styles.signedInNote}>
                  Signed in with the account you opened this tab with. Signing out of Microsoft
                  Teams, Outlook or the Microsoft 365 app signs you out of TodoWerk too.
                </MenuGroupHeader>
                <MenuItem icon={<OpenRegular />} onClick={openInBrowser}>
                  Open in browser
                </MenuItem>
              </MenuGroup>
            ) : (
              <MenuItem onClick={() => signOutForm.current?.requestSubmit()}>Sign out</MenuItem>
            )}
            <MenuDivider />
            <MenuItem onClick={() => setErasing(true)}>Delete my data</MenuItem>
          </MenuList>
        </MenuPopover>
      </Menu>
      {/* Outside the menu, because the menu's contents leave the page when it closes — and the
          menu closes on the click that opens the dialog. */}
      {!inTeams && <SignOutForm ref={signOutForm} />}
      <EraseMeDialog open={erasing} onOpenChange={setErasing} />
    </>
  );
}
