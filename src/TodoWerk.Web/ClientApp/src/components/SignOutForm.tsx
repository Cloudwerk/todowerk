import type { Ref } from 'react';
import { readAntiforgeryToken } from '../api/client';

/**
 * A real form POST, not a fetch: sign-out ends with a redirect to the Entra ID end-session
 * endpoint, which only a browser navigation can follow. The antiforgery token rides along as
 * a hidden field so the endpoint's CSRF check passes.
 *
 * Hidden, and submitted through the ref by the session menu's item: a menu item cannot be a
 * submit button, and a form inside the menu would leave the page with the menu.
 */
export function SignOutForm({ ref }: { ref: Ref<HTMLFormElement> }) {
  return (
    <form method="post" action="/auth/sign-out" ref={ref} hidden>
      <input type="hidden" name="__RequestVerificationToken" value={readAntiforgeryToken()} />
    </form>
  );
}
