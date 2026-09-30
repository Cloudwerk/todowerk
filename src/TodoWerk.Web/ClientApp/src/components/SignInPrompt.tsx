import { Body1, Button, Caption1, Card, Title2, makeStyles, tokens } from '@fluentui/react-components';
import { signInUrl } from '../api/client';
import { useEntraRoundTrip } from '../host';
import { CentredScreen } from './CentredScreen';
import { HandbookLink, administratorsPath } from './HandbookLink';

const useStyles = makeStyles({
  card: {
    maxWidth: '560px',
    padding: tokens.spacingHorizontalXL,
    rowGap: tokens.spacingVerticalM,
  },
  actions: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
  },
});

/** Where an administrator approves TodoWerk for everybody. Server half: `TenantConsentFlow.StartPath`. */
const tenantConsentPath = '/auth/tenant-consent';

/**
 * Which card this is. Four states, and only the first is the ordinary one: the rest are how a
 * round trip through Microsoft ended, told to somebody who is back here with no session.
 */
export type SignInOutcome = 'invitation' | 'not-approved' | 'failed' | 'approved';

/**
 * What the server said about the last round trip, read off the address it sent the browser back
 * to. Server halves: `SignInFailure.ReasonParameter` for a sign-in, `OnboardingEndpoints`'
 * `ConsentParameter` for an approval.
 *
 * A declined approval and a declined sign-in draw the same card deliberately — the person is in
 * the same position either way, and the card's whole job is to name the two ways out.
 */
export function signInOutcome(search: string): SignInOutcome {
  const parameters = new URLSearchParams(search);

  switch (parameters.get('signin')) {
    case 'not-approved':
      return 'not-approved';
    case 'failed':
      return 'failed';
    default:
      break;
  }

  switch (parameters.get('consent')) {
    case 'granted':
      return 'approved';
    case 'not-granted':
      return 'not-approved';
    default:
      return 'invitation';
  }
}

/**
 * The card somebody with no session sees, in both hosts.
 *
 * It is also where a failed round trip lands; declining a consent screen is an ordinary answer,
 * and this is the ordinary reply to it.
 */
export function SignInPrompt() {
  const styles = useStyles();

  // Read from the address rather than from the router: the browser SPA is behind a browser router
  // and the Teams tab behind a memory router, and this parameter only ever exists in the first —
  // the tab's own address never carries one, because a round trip started in the tab ends at the
  // auth-end document instead.
  const outcome = signInOutcome(window.location.search);

  // Reached in the Teams tab too — a session can lapse there like anywhere else — and a plain
  // navigation would take the iframe to a Microsoft sign-in page, which refuses to be framed and
  // would replace the tab with Microsoft's refusal.
  const signIn = useEntraRoundTrip();
  const consent = useEntraRoundTrip();

  const signInButton = (appearance: 'primary' | 'secondary') => (
    <Button
      appearance={appearance}
      as="a"
      href={signInUrl()}
      onClick={signIn.intercept}
      disabled={signIn.running}
    >
      {signIn.running
        ? 'Waiting for Microsoft…'
        : outcome === 'invitation'
          ? 'Sign in with Microsoft'
          : 'Sign in again'}
    </Button>
  );

  return (
    <CentredScreen>
      <Card className={styles.card}>
        <Title2 as="h2">{title(outcome)}</Title2>
        <Body1>{body(outcome)}</Body1>
        {outcome === 'not-approved' && (
          <Caption1>
            Approving covers everyone in the organisation and grants TodoWerk nothing it is not
            already asking each person for.{' '}
            <HandbookLink href={administratorsPath}>What approval grants</HandbookLink>
          </Caption1>
        )}
        <div className={styles.actions}>
          {/*
            The approval is the primary action on the card that needs one, even though the person
            reading it may well not be able to grant it: it is the thing that unblocks everybody in
            the tenant rather than one more attempt at the screen that just refused. Microsoft
            enforces who may approve — somebody without the rights is told so on Microsoft's own
            screen rather than by a button TodoWerk greys out on a guess about their directory role.
          */}
          {outcome === 'not-approved' && (
            <Button
              appearance="primary"
              as="a"
              href={tenantConsentPath}
              onClick={consent.intercept}
              disabled={consent.running}
            >
              {consent.running ? 'Waiting for Microsoft…' : 'Approve for your organisation'}
            </Button>
          )}
          {signInButton(outcome === 'not-approved' ? 'secondary' : 'primary')}
        </div>
        {signIn.failure !== null && <Caption1>{signIn.failure}</Caption1>}
        {consent.failure !== null && <Caption1>{consent.failure}</Caption1>}
      </Card>
    </CentredScreen>
  );
}

function title(outcome: SignInOutcome): string {
  switch (outcome) {
    case 'not-approved':
      return 'TodoWerk has not been approved';
    case 'failed':
      return 'That sign-in did not finish';
    case 'approved':
      return 'TodoWerk is approved for your organisation';
    case 'invitation':
      return 'Connect to Microsoft To Do';
  }
}

function body(outcome: SignInOutcome): string {
  switch (outcome) {
    // One sentence for two situations, because Microsoft's answer does not separate them: a person
    // who declined and a person whose organisation reserves the decision for an administrator come
    // back the same way. Saying which it was would be a guess, and a guess here reads as an
    // accusation to whoever it is wrong about.
    case 'not-approved':
      return 'Nobody approved TodoWerk’s access to your task lists — either the request was declined, or your organisation asks an administrator to approve apps like this one. Nothing has been changed, and you can start again whenever you like.';
    case 'failed':
      return 'The round trip to Microsoft did not complete. That is usually a page left open too long rather than anything wrong with your account.';
    case 'approved':
      return 'An administrator has approved TodoWerk for everyone in your organisation. Sign in and TodoWerk will not ask again.';
    case 'invitation':
      return 'TodoWerk reads your task lists with your work or school account. Nothing is changed until you approve a specific rename or merge.';
  }
}
