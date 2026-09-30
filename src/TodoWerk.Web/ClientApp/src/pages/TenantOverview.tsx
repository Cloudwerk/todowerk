import {
  Body1,
  Button,
  Caption1,
  Link,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Subtitle1,
  Title2,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { getTenantOverview, tenantConsentUrl } from '../api/client';
import { useApiQuery } from '../api/useApi';
import { HandbookLink, administratorsPath } from '../components/HandbookLink';
import { SignInPrompt } from '../components/SignInPrompt';
import { useEntraRoundTrip } from '../host';
import { useLicence } from '../licence';
import { formatDate } from '../components/formatDate';
import { useProseStyles } from '../components/prose';
import type { LicenceKind, TenantStatistics } from '../api/types';

/**
 * The notice lives in the repository rather than being served by the app, and the same document
 * covers the Hosted Service and Self-Host — it says explicitly that CloudWerk holds nothing in the
 * second case. So one link is correct for every installation, which a path served from this host
 * would not be.
 */
const privacyNoticeUrl = 'https://github.com/Cloudwerk/todowerk/blob/main/PRIVACY.md';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalL,
    // The same container as the Workbench, so the content edge does not jump between screens.
    maxWidth: '1200px',
  },
  panel: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
    padding: `${tokens.spacingVerticalL} ${tokens.spacingHorizontalL}`,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  tiles: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
    gap: tokens.spacingHorizontalM,
  },
  // One surface deeper than the panel, not the page canvas again: a tile the colour of the
  // ground behind the panel reads as a hole in it.
  tile: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXXS,
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
  },
  figure: {
    fontSize: tokens.fontSizeHero700,
    lineHeight: tokens.lineHeightHero700,
    fontWeight: tokens.fontWeightSemibold,
  },
  // A name and the sentence describing it, close enough together to read as one entry.
  reading: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXXS,
  },
  consentActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
});

/**
 * What this organisation's use of TodoWerk looks like from the inside — counts, and nothing else.
 *
 * Its own route rather than a panel on the Workbench, which is the hashtag inventory as the product.
 * Two panels with two conditions: the consent invitation shows until a grant is recorded, and the
 * statistics show once enough people have signed in for a total to say nothing about an individual.
 * Below that floor the server sends no statistics and this draws nothing in their place.
 */
export function TenantOverview() {
  const styles = useStyles();
  // A query rather than a one-shot read, because the consent round trip now happens without a page
  // load: in the Teams tab it runs in a popup, and what it changed is on the server. Without the
  // re-read the tab would keep showing the invitation to an organisation that has just approved.
  const { state: overview, refresh } = useApiQuery(getTenantOverview, 'tenant-overview');
  const licence = useLicence();

  // Whether TodoWerk may invite anybody to approve it for the organisation. The endpoint decides:
  // true under a Tenant Licence and during a Trial, false under a Personal Licence, and true on a
  // Self-Host, where there is no Licence to make it conditional on.
  //
  // The two states that are not an answer are not treated alike. `unknown` is permissive: the read
  // failed for a reason that is not a denial, which is a failure of TodoWerk's own bookkeeping
  // rather than news about anybody's standing, and hiding an administrator's route over it would be
  // the wrong way to be wrong. `loading` is not: it resolves in a moment, and a permissive default
  // would put the invitation in front of a Personal Licence holder — the one thing ADR-0012 says
  // must never be offered — for exactly as long as the read takes.
  const mayOfferConsent =
    licence.status === 'licensed'
      ? licence.licence.mayOfferTenantConsent
      : licence.status !== 'loading';

  if (overview.status === 'signedOut') {
    return <SignInPrompt />;
  }

  if (overview.status === 'loading') {
    return <Spinner label="Reading your organisation's numbers…" />;
  }

  if (overview.status === 'error' && !overview.stale) {
    return (
      <MessageBar intent="error">
        <MessageBarBody>
          <MessageBarTitle>Could not read your organisation's numbers</MessageBarTitle>
          {overview.error.problem.detail ?? overview.error.message}
        </MessageBarBody>
      </MessageBar>
    );
  }

  const data = overview.status === 'ready' ? overview.data : overview.stale!;

  return (
    <div className={styles.root}>
      <Title2 as="h2">Your organisation</Title2>

      {/* Absent on a Self-Host rather than rendered empty: there is no Licence there, and a panel
          saying so would invite somebody to wonder what they were missing. */}
      {licence.status === 'licensed' && licence.licence.kind !== null && (
        <LicencePanel kind={licence.licence.kind} endsAt={licence.licence.endsAt} />
      )}

      {/* Under a Personal Licence, neither panel: paying for one seat is not standing to approve
          TodoWerk for an organisation, and the "approve again" route inside the second panel is the
          same route by another name. Hiding them hides TodoWerk's route only — an administrator can
          still grant consent in the Entra portal, and TodoWerk still cannot see that they did
          (ADR-0008, ADR-0012). */}
      {mayOfferConsent &&
        (data.tenantConsentGrantedThroughTodoWerk ? (
          <RecordedConsent onApprovedAgain={refresh} />
        ) : (
          <ConsentInvitation onApproved={refresh} />
        ))}

      {data.statistics && <Statistics statistics={data.statistics} />}
    </div>
  );
}

/**
 * The invitation to approve TodoWerk for everybody. Subject to no floor: a lone user in a small
 * tenant still sees it, and that is how the feature gets found at all, since the statistics beside
 * it do not render at that size.
 *
 * The wording says the grant was not made *through TodoWerk*, because that is all TodoWerk knows.
 * Claiming the organisation has not approved would be a defect report waiting to happen from every
 * administrator who approved in the Entra portal instead.
 */
function ConsentInvitation({ onApproved }: { onApproved: () => void }) {
  const styles = useStyles();
  const { prose } = useProseStyles();
  const consent = useEntraRoundTrip(onApproved);

  return (
    <section className={styles.panel}>
      <Subtitle1 as="h3">Approve TodoWerk for everyone</Subtitle1>
      <Body1 className={prose}>
        An administrator can approve TodoWerk once on behalf of the whole organisation, so nobody
        else is asked to decide at sign-in. The approval covers exactly the permission each person
        already grants individually — read and write your own Microsoft To Do tasks — and nothing
        wider. TodoWerk gains no standing access to anybody's mailbox: it still acts as each person
        with that person's own sign-in, and still reaches nobody who has not signed in.
      </Body1>
      {/* The page written for exactly this decision, on this host, for every deployment — so the
          administrator being asked to approve can read what it grants before they do. Named and
          then described, rather than a link whose own text has to carry the description: the same
          page is in the help menu in the header under the same name, and an administrator who
          meets it twice should recognise it the second time. */}
      <div className={styles.reading}>
        <Body1>
          <HandbookLink href={administratorsPath}>For administrators</HandbookLink>
        </Body1>
        <Caption1 className={prose}>
          What TodoWerk asks for, what approving it for the organisation grants and what it does
          not, what it stores about a person, and how somebody is forgotten.
        </Caption1>
      </div>
      <div className={styles.consentActions}>
        <Button
          as="a"
          href={tenantConsentUrl}
          onClick={consent.intercept}
          appearance="primary"
          disabled={consent.running}
        >
          {consent.running ? 'Waiting for Microsoft…' : 'Approve for the organisation'}
        </Button>
        <Caption1>{consent.failure ?? 'You will be asked to sign in as an administrator.'}</Caption1>
      </div>
      <Caption1 className={prose}>
        No approval has been recorded <em>through TodoWerk</em>. If an administrator approved
        TodoWerk in the Microsoft Entra admin center instead, TodoWerk cannot see that and this
        invitation stays — approving here again is harmless.
      </Caption1>
    </section>
  );
}

/**
 * What replaces the invitation once a grant has been recorded. A line rather than a panel — the
 * call to action is gone — but the route stays open, and that is deliberate.
 *
 * TodoWerk cannot verify that a callback came from Microsoft: the admin-consent flow returns no
 * signed response, so a recorded grant is TodoWerk's own note that somebody came back through its
 * redirect, not proof of an approval. Presenting it as a settled state with no way back would make
 * that note irreversible — and anybody who reaches the callback URL directly could then hide the
 * invitation from a whole organisation permanently. Keeping "approve again" here costs a sentence
 * and makes the wrong note recoverable.
 */
function RecordedConsent({ onApprovedAgain }: { onApprovedAgain: () => void }) {
  const styles = useStyles();
  const { prose } = useProseStyles();
  const consent = useEntraRoundTrip(onApprovedAgain);

  return (
    <section className={styles.panel}>
      <Subtitle1 as="h3">Approved for the organisation</Subtitle1>
      <Body1 className={prose}>
        An approval for this organisation was recorded <em>through TodoWerk</em>. Nobody here should
        be asked to consent at sign-in any more.
      </Body1>
      <Caption1 className={prose}>
        TodoWerk records that somebody completed the approval; it cannot check with Microsoft
        afterwards, and it cannot see approvals made in the Microsoft Entra admin center. If people
        are still being prompted,{' '}
        <Link href={tenantConsentUrl} onClick={consent.intercept}>
          approve again
        </Link>{' '}
        — doing so twice is harmless.
      </Caption1>
    </section>
  );
}

/**
 * How each kind is named to a reader. British spelling, and never "plan" or "edition".
 *
 * Sentence case on screen, unlike CONTEXT.md and the ADRs, which capitalise Tenant Licence and
 * Personal Licence as terms of the domain vocabulary. On a page of running prose those capitals
 * read as the name of something bought rather than as the sentence they sit in, and the host UI
 * this page has to look native beside capitalises nothing that is not a product.
 */
const LICENCE_KINDS: Record<LicenceKind, string> = {
  Tenant: 'Tenant licence',
  Personal: 'Personal licence',
  Trial: 'Trial',
};

/**
 * Which kind of Licence the signed-in person is licensed under, and until when. Kind and end date,
 * and deliberately nothing else.
 *
 * There is no seat figure here and there is not going to be one. A count of distinct people is
 * exactly what the statistics floor below withholds, and a page that respected the floor for one
 * figure and not for another would be wrong in one direction or the other.
 */
function LicencePanel({ kind, endsAt }: { kind: LicenceKind; endsAt: string | null }) {
  const styles = useStyles();

  return (
    <section className={styles.panel}>
      <Subtitle1 as="h3">Your licence</Subtitle1>
      <Body1>
        {LICENCE_KINDS[kind]}
        {endsAt === null ? '' : `, until ${formatDate(endsAt)}`}
      </Body1>
    </section>
  );
}

function Statistics({ statistics }: { statistics: TenantStatistics }) {
  const styles = useStyles();
  const { prose } = useProseStyles();

  return (
    <section className={styles.panel}>
      <Subtitle1 as="h3">How TodoWerk is used here</Subtitle1>
      <div className={styles.tiles}>
        <Tile figure={statistics.memberCount} label="people have signed in" />
        {statistics.activity.map((window) => (
          <Tile
            key={window.windowDays}
            figure={window.memberCount}
            label={`signed in within ${window.windowDays} days`}
          />
        ))}
        <Tile figure={statistics.totalOccurrences} label="hashtag occurrences between them" />
        {/* "On record": the headline count keeps the forgotten (ADR-0009), the average divides by
            the people still here — the two would contradict each other under one label. */}
        <Tile figure={statistics.averageOccurrencesPerMember} label="occurrences per person, on average" />
      </div>
      <Caption1 className={prose}>
        First sign-in here: {formatDate(statistics.firstSignedInAt)}. The average counts only the
        people still on record. Counts only — TodoWerk never
        names a colleague on this screen, never shows a per-person row, and never lists or compares
        hashtags across people. What it holds and for how long is in{' '}
        <Link href={privacyNoticeUrl} target="_blank" rel="noreferrer">
          the privacy notice
        </Link>
        .
      </Caption1>
    </section>
  );
}

function Tile({ figure, label }: { figure: number; label: string }) {
  const styles = useStyles();

  return (
    <div className={styles.tile}>
      <span className={styles.figure}>{figure.toLocaleString()}</span>
      <Caption1>{label}</Caption1>
    </div>
  );
}
