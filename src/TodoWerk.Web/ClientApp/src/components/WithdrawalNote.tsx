import { Caption1 } from '@fluentui/react-components';
import { HandbookLink, administratorsRevokingPath } from './HandbookLink';

/**
 * The sentence both consent cards carry: the tab's, and the browser's card after a sign-in nobody
 * approved. It is there before anybody approves anything, because an administrator deciding whether
 * to say yes asks how to undo it first, and an answer only found afterwards is no help with the
 * decision. One component, so the two cards cannot drift into promising different things.
 */
export function WithdrawalNote() {
  return (
    <Caption1>
      You or your administrator can withdraw this at any time.{' '}
      <HandbookLink href={administratorsRevokingPath}>How to withdraw it</HandbookLink>
    </Caption1>
  );
}
