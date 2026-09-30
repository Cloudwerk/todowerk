import { useState } from 'react';
import {
  Body1,
  Button,
  Caption1,
  Card,
  Spinner,
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { DesktopRegular } from '@fluentui/react-icons';
import { teamsConsentUrl } from '../api/client';
import { OpenInBrowserButton } from '../components/OpenInBrowserButton';
import { teamsAuthEndPath, useTeamsHost } from '../host';

const useStyles = makeStyles({
  page: {
    minHeight: '100vh',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingHorizontalXXL,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  card: {
    maxWidth: '520px',
    padding: tokens.spacingHorizontalXL,
    rowGap: tokens.spacingVerticalM,
  },
  actions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  deviceIcon: {
    fontSize: '32px',
    color: tokens.colorBrandForeground1,
  },
});

function Frame({ children }: { children: React.ReactNode }) {
  const styles = useStyles();

  return (
    <div className={styles.page}>
      <Card className={styles.card}>{children}</Card>
    </div>
  );
}

/** While the token is being acquired and exchanged. Nothing of the Workbench renders before this. */
export function StartingCard() {
  return (
    <Frame>
      <Spinner label="Signing you in…" />
    </Frame>
  );
}

/**
 * The first run of every tenant that has not granted Tenant Consent — which is most first runs,
 * not an edge case.
 *
 * A control rather than a spinner, and never an automatic popup: a window opened without a click
 * meets the pop-up blocker, and the person is left looking at a tab that is doing nothing for a
 * reason nothing on screen explains.
 */
export function ConsentRequiredCard({ onGranted }: { onGranted: () => void }) {
  const styles = useStyles();
  const host = useTeamsHost();
  const [running, setRunning] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  return (
    <Frame>
      <Title2 as="h1">One approval, and this tab signs you in silently</Title2>
      <Body1>
        TodoWerk reads and changes your own Microsoft To Do tasks, with your own account. Microsoft
        asks you — or an administrator, once, for everybody — to approve that before it will let
        TodoWerk act for you.
      </Body1>
      <div className={styles.actions}>
        <Button
          appearance="primary"
          disabled={running || host === null}
          onClick={() => {
            if (host === null) {
              return;
            }

            setRunning(true);
            setFailure(null);

            host
              .authenticate(teamsConsentUrl(teamsAuthEndPath))
              .then(() => onGranted())
              .catch(() =>
                setFailure(
                  'That window closed before it finished. Nothing was approved — try again.',
                ),
              )
              .finally(() => setRunning(false));
          }}
        >
          {running ? 'Waiting for Microsoft…' : 'Approve and continue'}
        </Button>
        {running && <Spinner size="tiny" />}
      </div>
      {failure !== null && <Caption1>{failure}</Caption1>}
      <Caption1>
        Approving does not give TodoWerk access to anybody else's tasks, and it reaches nobody who
        has not signed in.
      </Caption1>
    </Frame>
  );
}

/**
 * The card behind ADR-0010's known cost. The exchange succeeded and the request straight after it
 * came back 401, which means this browser did not keep the cookie the exchange set. Inside Teams
 * that cookie is a third-party cookie, and more than one thing refuses those: Safari outright,
 * Edge InPrivate through its default strict tracking prevention, and any browser whose privacy
 * settings block cookies in frames. The card only knows the symptom, so it names the causes
 * rather than guessing the browser.
 *
 * No retry: retrying produces exactly the same result every time, and a spinner that never
 * finishes is worse than a sentence that is true.
 */
export function CookiesBlockedCard() {
  const styles = useStyles();

  return (
    <Frame>
      <Title2 as="h1">
        This browser is not keeping TodoWerk signed in inside Microsoft Teams, Outlook or the
        Microsoft 365 app
      </Title2>
      <Body1>
        Signing in with Microsoft worked, but the browser then dropped the session because TodoWerk
        is running inside a frame of the app you opened it from and this browser's privacy settings
        refuse cookies in frames. InPrivate and private windows, strict tracking prevention and Safari all do this,
        and TodoWerk cannot change it from in here.
      </Body1>
      <Body1>
        Everything works normally in a browser tab of its own, signed in with the same account. To
        stay inside Microsoft Teams, Outlook or the Microsoft 365 app, use their desktop apps or a
        normal window in Microsoft Edge or Chrome.
      </Body1>
      <div className={styles.actions}>
        <OpenInBrowserButton />
      </div>
      <Caption1>
        In Edge, InPrivate windows use strict tracking prevention by default. Turning that off
        under Privacy settings lets this tab keep its session.
      </Caption1>
    </Frame>
  );
}

/**
 * The last thing somebody sees in the tab. No retry and no sign-in: their data is gone, and every
 * control that could put something back would be one that quietly signed them in again.
 */
export function ErasedCard() {
  return (
    <Frame>
      <Title2 as="h1">Everything TodoWerk held about you has been deleted</Title2>
      <Body1>
        Your hashtag index, your change history and the journals behind it are gone, and so is the
        stored permission TodoWerk used to reach your tasks. Your Microsoft To Do tasks themselves
        are untouched.
      </Body1>
      <Caption1>
        You can close this tab. Opening it again would sign you in as a new arrival and start over.
      </Caption1>
    </Frame>
  );
}

/** Everything else, with the reason and a way to try again. */
export function TabFailureCard({ message, onRetry }: { message: string; onRetry: () => void }) {
  const styles = useStyles();

  return (
    <Frame>
      <Title2 as="h1">TodoWerk could not sign you in</Title2>
      <Body1>{message}</Body1>
      <div className={styles.actions}>
        <Button appearance="primary" onClick={onRetry}>
          Try again
        </Button>
        <OpenInBrowserButton appearance="secondary" />
      </div>
    </Frame>
  );
}

/**
 * What somebody sees who opened the tab's address in an ordinary browser. `app.initialize()` has
 * no host to shake hands with there and simply never resolves, so the bootstrap gives it a bounded
 * moment and then says this rather than spinning forever.
 */
export function OutsideTeamsCard() {
  const styles = useStyles();

  return (
    <Frame>
      <Title2 as="h1">
        This page is TodoWerk's tab for Microsoft Teams, Outlook and the Microsoft 365 app
      </Title2>
      <Body1>
        It only works inside one of those, which is what tells it who you are. TodoWerk itself runs
        perfectly well in an ordinary browser tab.
      </Body1>
      <div className={styles.actions}>
        <OpenInBrowserButton />
      </div>
    </Frame>
  );
}

/**
 * What a phone gets instead of the Workbench. The hashtag workbench is a wide table with a detail
 * panel beside it, and at phone width it is a horizontal scroll over columns nobody can read. A
 * capability the device cannot carry says so instead of dead-ending, and this is the saying so.
 * No sign-in happens first: there is nothing to sign in for.
 * No way out to a browser either — the same workbench on the same phone would be no better.
 */
export function SmallScreenCard() {
  const styles = useStyles();

  return (
    <Frame>
      <DesktopRegular className={styles.deviceIcon} aria-hidden />
      <Title2 as="h1">TodoWerk needs a desktop or laptop screen</Title2>
      <Body1>
        The hashtag workbench is a wide table with a panel beside it, and it does not fit on a
        phone. Open TodoWerk in Microsoft Teams, Outlook or the Microsoft 365 app on your computer,
        or in a browser there, signed in with the same account.
      </Body1>
      <Caption1>Your tasks and hashtags are untouched. Nothing on this screen changes them.</Caption1>
    </Frame>
  );
}
