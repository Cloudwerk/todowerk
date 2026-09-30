import { useCallback } from 'react';
import { Button } from '@fluentui/react-components';
import type { ButtonProps } from '@fluentui/react-components';
import { browserAppUrl, useTeamsHost } from '../host';

interface OpenInBrowserButtonProps {
  appearance?: ButtonProps['appearance'];
  size?: ButtonProps['size'];
  className?: string;
  children?: React.ReactNode;
}

/**
 * The way out of the frame, as an action any control can run.
 *
 * `app.openLink()` rather than an anchor: a link inside a Teams tab opens inside Teams, which is
 * the frame the person is trying to leave. Outside Teams — which is only ever the browser SPA, and
 * only if somebody puts this there — it falls back to opening a window.
 */
export function useOpenInBrowser(): () => void {
  const host = useTeamsHost();

  return useCallback(() => {
    const url = browserAppUrl();

    if (host === null) {
      window.open(url, '_blank', 'noopener');
      return;
    }

    host.openInBrowser(url);
  }, [host]);
}

/**
 * The way out of the frame, and the only one.
 *
 * Three things point at it: the session menu in the tab's header, where sign-out would be if the
 * tab offered sign-out; the card that appears when the browser refuses to store TodoWerk's session
 * inside Teams; and `websiteUrl` in the app manifest — which is the same target expressed in
 * the one place this component cannot reach. One action rather than three ad-hoc links, because
 * three would drift and the one that mattered would be the one nobody was looking at.
 */
export function OpenInBrowserButton({
  appearance = 'primary',
  size,
  className,
  children,
}: OpenInBrowserButtonProps) {
  const openInBrowser = useOpenInBrowser();

  return (
    <Button appearance={appearance} size={size} className={className} onClick={openInBrowser}>
      {children ?? 'Open TodoWerk in your browser'}
    </Button>
  );
}
