import { useState } from 'react';
import {
  Body1,
  Button,
  Caption1,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import type { DialogProps } from '@fluentui/react-components';
import { erasureUrl, readAntiforgeryToken, teamsEndSessionUrl } from '../api/client';
import { apiFetch } from '../api/http';
import { useSessionEnded, useTeamsHost } from '../host';

const useStyles = makeStyles({
  warning: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
  },
});

/**
 * Asks TodoWerk to forget you, for good.
 *
 * Opened from the session menu, where the way out is offered, because that is where somebody
 * looks for it — and worded so nobody arrives here thinking they are signing out. It is confirmed
 * before it runs and says plainly that it cannot be undone, because it cannot: the index, the
 * change history and the journals are destroyed, and the journals are the only record of what a
 * task used to be called.
 *
 * The way to keep the data is the primary button, and the destruction is the secondary one. Fluent
 * has no danger button by design; its convention for an irreversible confirm is that the safe
 * action is the one the eye and the default focus land on, so a reflexive click keeps the data.
 * Once the destruction has been asked for, though, there is no way back through this dialog — not
 * the button, not Escape, not a click beside it: closing it would only hide a request already on
 * its way, and a person who then saw the tab announce their erasure would have been lied to.
 *
 * Offered in the Teams tab too. Erasure is an obligation rather than a convenience, and a surface
 * that hides it is not a complete product — so the tab takes the longer route below rather than
 * leaving people to find a browser.
 */
export function EraseMeDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const host = useTeamsHost();
  const sessionEnded = useSessionEnded();
  const [running, setRunning] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  const close = () => onOpenChange(false);

  const handleOpenChange: DialogProps['onOpenChange'] = (_, data) => {
    if (!data.open && running) return;

    onOpenChange(data.open);
  };

  /**
   * The tab's erasure, and the reason it is a second mechanism rather than a branch inside the
   * browser's form: a form here would be answered with a redirect to a Microsoft sign-in page,
   * which refuses to be rendered in a frame — so the tab does the destruction with a fetch, which
   * deletes the session cookie and answers 204, and then runs the one leg that genuinely needs a
   * top-level navigation in a Teams popup. Both halves matter: the first is what destroys the data,
   * and the second is what makes "you will be signed out" true rather than merely written on the
   * screen ([ADR-0010](../../../../../docs/adr/0010-teams-tab-session-and-framing.md)).
   */
  async function erase() {
    setRunning(true);
    setFailure(null);

    try {
      await apiFetch<void>(erasureUrl, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-CSRF-TOKEN': readAntiforgeryToken(),
        },
      });
    } catch {
      setRunning(false);
      setFailure('Something was still working on your data. Nothing was deleted — try again in a moment.');

      return;
    }

    // Destroyed, and the session cookie is gone. What is left is the Microsoft session, and only a
    // top-level navigation can end one — so it happens in the popup, which lands back on the tab's
    // auth-end page. A popup the person closes leaves them erased and still signed into Microsoft,
    // which is worth saying rather than retrying.
    try {
      await host?.authenticate(teamsEndSessionUrl);
    } catch {
      // The data is gone either way, so the tab must not go back to showing a Workbench: the
      // session cookie was deleted by the request above and every read from here would 401.
    }

    setRunning(false);
    close();
    sessionEnded();
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogSurface>
        {host === null ? (
          <BrowserErasureForm onClose={close} />
        ) : (
          <TeamsErasureForm
            onClose={close}
            onErase={() => void erase()}
            running={running}
            failure={failure}
          />
        )}
      </DialogSurface>
    </Dialog>
  );
}

/**
 * The browser's confirmation. A real form rather than a fetch, for the same reason sign-out is
 * one: the response ends in a redirect to the Entra ID end-session endpoint, and only a browser
 * navigation can follow that.
 */
function BrowserErasureForm({ onClose }: { onClose: () => void }) {
  return (
    <form method="post" action={erasureUrl}>
      <input type="hidden" name="__RequestVerificationToken" value={readAntiforgeryToken()} />
      <DialogBody>
        <DialogTitle>Delete everything TodoWerk holds about you?</DialogTitle>
        <ErasureWarning />
        <DialogActions>
          <KeepMyDataButton onClose={onClose} />
          <Button appearance="secondary" type="submit">
            Delete everything
          </Button>
        </DialogActions>
      </DialogBody>
    </form>
  );
}

/** The tab's confirmation: the same words, and while the destruction runs, no way out but through. */
function TeamsErasureForm({
  onClose,
  onErase,
  running,
  failure,
}: {
  onClose: () => void;
  onErase: () => void;
  running: boolean;
  failure: string | null;
}) {
  return (
    <DialogBody>
      <DialogTitle>Delete everything TodoWerk holds about you?</DialogTitle>
      <ErasureWarning />
      {failure !== null && <Caption1>{failure}</Caption1>}
      <DialogActions>
        <KeepMyDataButton onClose={onClose} disabled={running} />
        <Button appearance="secondary" disabled={running} onClick={onErase}>
          {running ? 'Deleting…' : 'Delete everything'}
        </Button>
      </DialogActions>
    </DialogBody>
  );
}

/** The same words in both surfaces, because it is the same irreversible thing. */
function ErasureWarning() {
  const styles = useStyles();

  return (
    <DialogContent className={styles.warning}>
      <Body1>
        This destroys your hashtag index, every change you have made and the journals behind them,
        and the stored permission TodoWerk uses to reach your tasks in the background. You will be
        signed out.
      </Body1>
      <Body1>
        <strong>It cannot be undone.</strong> Once the journals are gone, no change can be reversed
        — including ones you made in the last thirty days. Your Microsoft To Do tasks themselves are
        not touched: they stay exactly as they are now.
      </Body1>
    </DialogContent>
  );
}

/** The way out of the dialog, shared by both forms so the wording cannot drift between them. */
function KeepMyDataButton({ onClose, disabled = false }: { onClose: () => void; disabled?: boolean }) {
  return (
    <Button appearance="primary" type="button" onClick={onClose} disabled={disabled}>
      Keep my data
    </Button>
  );
}
