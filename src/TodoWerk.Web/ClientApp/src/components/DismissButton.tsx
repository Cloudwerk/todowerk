import { Button } from '@fluentui/react-components';
import { DismissRegular } from '@fluentui/react-icons';

/**
 * The glyph Fluent's MessageBar convention expects in the corner, named for the screen reader.
 * One component for every bar that can be waved away, so they cannot drift apart by eye or by ear.
 */
export function DismissButton({ onClick }: { onClick: () => void }) {
  return <Button appearance="transparent" aria-label="Dismiss" icon={<DismissRegular />} onClick={onClick} />;
}
