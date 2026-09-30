/**
 * Whether the host the tab is running in is a phone, from TeamsJS's `context.app.host.clientType`.
 *
 * The three values are TeamsJS's `HostClientType.android`, `.ios` and `.ipados`, spelled out here
 * so this module and its test need nothing from TeamsJS — the browser bundle must not reach it,
 * and `main.tsx` is the one module allowed to. Unknown and missing values count as a desktop: a
 * host that did not say is more likely a desktop client that answered late than a phone, and the
 * cost of being wrong that way is a wide table, not a locked door.
 */
export function needsDesktopScreen(clientType: string | undefined): boolean {
  return clientType === 'android' || clientType === 'ios' || clientType === 'ipados';
}
