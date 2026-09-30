import { Body1, Caption1, Card, Link, Title2, makeStyles, tokens } from '@fluentui/react-components';
import { licenceEndedCode } from '../api/types';
import { useTeamsHost } from '../host';
import type { DeniedCode } from '../licence';
import { CentredScreen } from './CentredScreen';

const useStyles = makeStyles({
  card: {
    maxWidth: '560px',
    padding: tokens.spacingHorizontalXL,
    rowGap: tokens.spacingVerticalM,
  },
});

/**
 * What a denied person sees, in place of the Workbench rather than on top of it.
 *
 * Two problem codes and two cards, and keeping them apart is the whole point: a paying customer
 * meets the second one during a portal outage, and reading it as the first would send them to buy
 * something they already own.
 *
 * Neither card offers a way to sign in again — a session that is perfectly good is not what is
 * wrong, and the button would loop. Erasure stays reachable in both hosts, because it is an
 * obligation rather than a feature: it lives in the shell around this card, which is why this is a
 * card inside the layout rather than a screen that replaces it.
 *
 * @param detail The server's own sentence for this refusal, which is the portal's where it sent one.
 * @param contact The operator's contact address, when the server carried one. Browser only.
 * @param purchaseUrl
 *   Where TodoWerk is bought, when ManagementPortal delivered an address. The ended card alone, and
 *   in the browser alone. Null is the ordinary state — the portal sends none where it sells nothing
 *   — and the card then simply carries no link rather than a dead one.
 */
export function LicenceDeniedCard({
  code,
  detail,
  contact,
  purchaseUrl,
}: {
  code: DeniedCode;
  detail: string | null;
  contact: string | null;
  purchaseUrl: string | null;
}) {
  const styles = useStyles();

  // The one distinction this card draws, and it is the tab-versus-browser one the client already
  // has — deliberately not a device check. Inside the tab the card states the fact and nothing
  // more: no purchase link inside the Teams tab, on any device. The tab's "Open in browser"
  // control, in the header above, is the route to the version with the contact (ADR-0012).
  const inTeams = useTeamsHost() !== null;
  const ended = code === licenceEndedCode;

  return (
    <CentredScreen>
      <Card className={styles.card}>
        <Title2 as="h2">
          {ended ? 'Your access to TodoWerk has ended' : 'TodoWerk could not confirm your licence'}
        </Title2>
        {/* With no sentence from the server, the body still says something the heading did not. */}
        <Body1>
          {detail ??
            (ended
              ? 'Your licence has run out, so TodoWerk has closed for you. Your colleagues are not affected.'
              : "TodoWerk could not confirm your organisation's licence. It keeps trying.")}
        </Body1>
        {!ended && (
          <Caption1>
            Nothing needs doing. This clears itself as soon as TodoWerk can check again, and your
            hashtags and change history are untouched in the meantime.
          </Caption1>
        )}
        {/*
          The one screen in the product whose reader may want to buy something right now, which is
          why ManagementPortal delivers the address on a refusal at all: somebody who has just been
          told their Trial ended is exactly who wants it.

          The ended card alone. The other one is met by people who have paid, during a portal
          outage, and offering them a way to buy is the mistake the two cards exist to prevent —
          the server sends no address with that refusal in any case. And the browser alone: no
          purchase link inside the Teams tab, on any device (ADR-0012). No link where the portal
          delivered no address, which is a legitimate state rather than a fault.
        */}
        {ended && !inTeams && purchaseUrl !== null && (
          <Body1>
            <Link href={purchaseUrl} target="_blank" rel="noreferrer">
              Buy TodoWerk
            </Link>
          </Body1>
        )}
        {ended && !inTeams && contact !== null && (
          <Caption1>You can reach whoever runs this installation at {contact}.</Caption1>
        )}
      </Card>
    </CentredScreen>
  );
}
