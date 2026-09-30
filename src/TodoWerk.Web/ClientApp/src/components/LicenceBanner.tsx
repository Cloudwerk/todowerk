import { useState } from 'react';
import {
  Link,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import type { LicenceBannerState } from '../api/types';
import { DismissButton } from './DismissButton';
import { useTeamsHost } from '../host';
import { useLicence } from '../licence';
import { formatDate } from './formatDate';

/**
 * Where a dismissal is remembered: this browser, and nowhere else. It never reaches the server —
 * TodoWerk stores nothing about a person that it does not have to (ADR-0009), and which banner
 * somebody has waved away is the clearest possible example of something it does not have to.
 */
const DISMISSED_KEY = 'todowerk.licence-banner.dismissed';

const useStyles = makeStyles({
  links: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
});

/**
 * What the Workbench says to one person about their own Licence, when there is anything to say.
 *
 * Two states and a silence, and the silence is the common case: under a Tenant Licence or a
 * Personal Licence in term there is nothing to tell anybody, an unreachable portal is a log line
 * rather than a user's problem until the fail-open window runs out, and a Self-Host never has a
 * Licence to talk about. It never counts days aloud — it names the date.
 */
export function LicenceBanner() {
  const styles = useStyles();
  const licence = useLicence();
  const inTeams = useTeamsHost() !== null;

  // Read once into state rather than on every render: what matters is whether it was dismissed
  // when the screen was opened, and re-reading would make the bar flicker back as it is dismissed.
  const [dismissed, setDismissed] = useState(readDismissed);

  if (licence.status !== 'licensed') {
    return null;
  }

  const { banner, endsAt, purchaseUrl } = licence.licence;

  if (banner === 'None' || dismissed === banner) {
    return null;
  }

  const dismiss = () => {
    setDismissed(banner);
    writeDismissed(banner);
  };

  return (
    // Warning rather than info once it is close, and the same sentence either way: the colour is
    // what changed, not the news. An earlier dismissal of the running state does not carry over —
    // `dismissed === banner` is per state — so the warning comes back exactly once and can then be
    // dismissed on its own.
    <MessageBar intent={banner === 'TrialEndingSoon' ? 'warning' : 'info'}>
      <MessageBarBody>
        {endsAt === null
          ? 'You are trying TodoWerk.'
          : `You are trying TodoWerk until ${formatDate(endsAt)}.`}
      </MessageBarBody>
      <MessageBarActions
        containerAction={<DismissButton onClick={dismiss} />}
      >
        {/*
          Inside the Teams tab the banner shows the sentence and no purchase link, on any device.
          The browser carries the links. That is the tab-versus-browser distinction the client
          already has, and deliberately not a device check (ADR-0012).

          No link at all when the portal delivered no purchase URL: a button that goes nowhere is
          worse than no button.
        */}
        {!inTeams && purchaseUrl !== null && (
          <span className={styles.links}>
            <Link href={purchaseUrl} target="_blank" rel="noreferrer">
              Buy for yourself
            </Link>
            <Link href={purchaseUrl} target="_blank" rel="noreferrer">
              Buy for your organisation
            </Link>
          </span>
        )}
      </MessageBarActions>
    </MessageBar>
  );
}

/**
 * Wrapped, because a browser can refuse. Private windows and blocked site data both throw on the
 * accessor itself, and a Workbench that failed to render over a dismissal it could not look up
 * would be a broken product over a blemish.
 */
function readDismissed(): LicenceBannerState | null {
  try {
    const stored = window.localStorage.getItem(DISMISSED_KEY);

    return stored === 'TrialRunning' || stored === 'TrialEndingSoon' ? stored : null;
  } catch {
    return null;
  }
}

function writeDismissed(state: LicenceBannerState): void {
  try {
    window.localStorage.setItem(DISMISSED_KEY, state);
  } catch {
    // Dismissed for this page, forgotten on the next load. The alternative is refusing to dismiss
    // it at all, which is worse in exactly the browsers that cannot remember.
  }
}
