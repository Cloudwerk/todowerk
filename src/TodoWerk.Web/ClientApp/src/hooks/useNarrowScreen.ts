import { useSyncExternalStore } from 'react';

/**
 * The width under which the Workbench shows its phone layout. A width rather than the host's
 * device type: an iPad reports itself as a tablet and has room for the table, a phone turned on its
 * side can have room too, and the browser SPA on a phone has no host to ask at all. The sentence
 * the phone layout says — that a change needs a wider screen — is only true if it is the screen
 * that decided.
 *
 * Also the breakpoint the shell's own padding steps down at, so the two cannot drift apart.
 */
export const narrowScreenQuery = '(max-width: 640px)';

function mediaQuery(): MediaQueryList | undefined {
  return typeof window.matchMedia === 'function' ? window.matchMedia(narrowScreenQuery) : undefined;
}

function subscribe(onChange: () => void): () => void {
  const query = mediaQuery();
  query?.addEventListener('change', onChange);

  return () => query?.removeEventListener('change', onChange);
}

function snapshot(): boolean {
  return mediaQuery()?.matches ?? false;
}

/**
 * True while the screen is phone-narrow, and follows the screen when it turns or a window is
 * resized. Without `matchMedia` — a test host — the answer is the wide layout.
 */
export function useNarrowScreen(): boolean {
  return useSyncExternalStore(subscribe, snapshot, () => false);
}
