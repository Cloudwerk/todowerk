import { apiFetch } from './http';
import type {
  Change,
  ChangePreview,
  ChangeQueue,
  Handbook,
  HashtagInventoryPage,
  HashtagInventoryQuery,
  IndexScanRequested,
  IndexStatus,
  Licence,
  MarkerRule,
  MarkerRuleList,
  MarkerRuleMove,
  SessionUser,
  TaskList,
  TenantOverview,
} from './types';

export const getCurrentUser = (signal?: AbortSignal) => apiFetch<SessionUser>('/api/me', { signal });

export const getTaskLists = (signal?: AbortSignal) => apiFetch<TaskList[]>('/api/task-lists', { signal });

/**
 * What the signed-in person is licensed under, read once a session from one place. A denied person
 * never gets a body from this: the server's licence gate answers first with one of two problem
 * codes, and those codes are what the client draws its two cards from.
 */
export const getLicence = (signal?: AbortSignal) => apiFetch<Licence>('/api/licence', { signal });

/**
 * Where the Guide is, or that there is none. Anonymous on the server and read once by the shell:
 * a fact about the deployment rather than about the person.
 */
export const getHandbook = (signal?: AbortSignal) => apiFetch<Handbook>('/api/handbook', { signal });

/** How fresh the index is, and whether a scan is running. Drives the Workbench chrome. */
export const getIndexStatus = (signal?: AbortSignal) => apiFetch<IndexStatus>('/api/index/status', { signal });

/**
 * The inventory. Every knob is a query parameter because the server answers them in SQL —
 * thousands of rows per tenant is too many to sort in the browser.
 */
export function getHashtagInventory(query: HashtagInventoryQuery = {}, signal?: AbortSignal) {
  const parameters = new URLSearchParams();

  if (query.search) parameters.set('search', query.search);
  if (query.filter && query.filter !== 'None') parameters.set('filter', query.filter);
  if (query.sort) parameters.set('sort', query.sort);
  if (query.descending !== undefined) parameters.set('descending', String(query.descending));
  if (query.page) parameters.set('page', String(query.page));
  if (query.pageSize) parameters.set('pageSize', String(query.pageSize));

  const search = parameters.toString();

  return apiFetch<HashtagInventoryPage>(`/api/hashtags${search ? `?${search}` : ''}`, { signal });
}

/**
 * Asks for the index to be brought up to date — the whole of it, or one list. Idempotent:
 * asking for what a queued scan already covers reports `queued: false` rather than doubling
 * the work.
 */
export const requestIndexScan = (fullRescan = false, taskListId?: string) =>
  apiFetch<IndexScanRequested>('/api/index/scan', {
    method: 'POST',
    headers: mutatingHeaders(),
    body: JSON.stringify({ taskListId: taskListId ?? null, fullRescan }),
  });

/**
 * What a Change would do, without doing it. A POST although it writes nothing: the sources are a
 * list of hashtag keys, which is a request body and not a query string.
 */
export const previewChange = (sourceKeys: string[], targetSpelling: string) =>
  apiFetch<ChangePreview>('/api/changes/preview', {
    method: 'POST',
    headers: mutatingHeaders(),
    body: JSON.stringify({ sourceKeys, targetSpelling }),
  });

/**
 * What applying the marker rules would do. The same endpoint and the same answer shape as any
 * other preview — the fourth kind is stated in the request rather than read off it, because it
 * carries no hashtags and no target for anything to be derived from (ADR-0014).
 *
 * `markerRuleKey` null is every rule; a key is one of them. Either way the write is the whole
 * block, so this decides which tasks are covered and not what goes into them.
 */
export const previewMarkerApply = (markerRuleKey: string | null) =>
  apiFetch<ChangePreview>('/api/changes/preview', {
    method: 'POST',
    headers: mutatingHeaders(),
    body: JSON.stringify({ applyMarkers: true, markerRuleKey }),
  });

/**
 * Queues the Change. What is sent is the instruction, not the list of tasks — the server plans
 * again and persists what it computed, so what runs is never a list the browser could edit.
 */
export const confirmChange = (
  sourceKeys: string[],
  targetSpelling: string,
  confirmMerge: boolean,
  survivingMarker: string | null = null,
) =>
  apiFetch<Change>('/api/changes', {
    method: 'POST',
    headers: mutatingHeaders(),
    body: JSON.stringify({ sourceKeys, targetSpelling, confirmMerge, survivingMarker }),
  });

/**
 * What removing the stale markers would do. The fifth kind, stated in the request for the same
 * reason the fourth is — and separately from it, because a request that meant one and was read as
 * the other would write the opposite of what was asked (ADR-0014).
 *
 * `marker` null is every stale marker; an emoji is one of them. A marker rather than a hashtag,
 * because a stale marker's hashtag has by definition left the task, and one a deleted rule left
 * behind has no hashtag at all.
 */
export const previewMarkerRemoval = (marker: string | null) =>
  apiFetch<ChangePreview>('/api/changes/preview', {
    method: 'POST',
    headers: mutatingHeaders(),
    body: JSON.stringify({ removeMarkers: true, marker }),
  });

/** Queues the Remove. Its scope is copied out of the rules table here, once. */
export const confirmMarkerRemoval = (marker: string | null) =>
  apiFetch<Change>('/api/changes', {
    method: 'POST',
    headers: mutatingHeaders(),
    body: JSON.stringify({ removeMarkers: true, marker }),
  });

/** Queues the Apply. The markers it writes are copied out of the rules table here, once. */
export const confirmMarkerApply = (markerRuleKey: string | null) =>
  apiFetch<Change>('/api/changes', {
    method: 'POST',
    headers: mutatingHeaders(),
    body: JSON.stringify({ applyMarkers: true, markerRuleKey }),
  });

/** The Change in flight and the history beside it. Polled while one is running. */
export const getChangeQueue = (signal?: AbortSignal) => apiFetch<ChangeQueue>('/api/changes', { signal });

/** Stops the run after the task it is on. There is no pause to resume from (ADR-0006). */
export const cancelChange = (changeId: string) =>
  apiFetch<Change>(`/api/changes/${encodeURIComponent(changeId)}/cancel`, {
    method: 'POST',
    headers: mutatingHeaders(),
  });

/** Puts back what a Change wrote. The answer is the new Change that will do it. */
export const undoChange = (changeId: string) =>
  apiFetch<Change>(`/api/changes/${encodeURIComponent(changeId)}/undo`, {
    method: 'POST',
    headers: mutatingHeaders(),
  });

/**
 * This person's Marker Rules, in the order that is the order of the block, each with how far it
 * has been applied.
 */
export const getMarkerRules = (signal?: AbortSignal) =>
  apiFetch<MarkerRuleList>('/api/marker-rules', { signal });

/** Tells TodoWerk that a hashtag carries a marker. It writes no title. */
export const createMarkerRule = (spelling: string, marker: string) =>
  apiFetch<MarkerRule>('/api/marker-rules', {
    method: 'POST',
    headers: mutatingHeaders(),
    body: JSON.stringify({ spelling, marker }),
  });

/** A new marker for a rule, or a step up or down its owner's list. */
export const updateMarkerRule = (ruleId: string, change: { marker?: string; move?: MarkerRuleMove }) =>
  apiFetch<MarkerRule>(`/api/marker-rules/${encodeURIComponent(ruleId)}`, {
    method: 'PATCH',
    headers: mutatingHeaders(),
    body: JSON.stringify({ marker: change.marker ?? null, move: change.move ?? null }),
  });

/**
 * Forgets a rule. Nothing is written: the markers it put on tasks stay where they are until an
 * explicit Remove Markers takes them away (ADR-0014).
 */
export const deleteMarkerRule = (ruleId: string) =>
  apiFetch<void>(`/api/marker-rules/${encodeURIComponent(ruleId)}`, {
    method: 'DELETE',
    headers: mutatingHeaders(),
  });

/**
 * What this organisation's use of TodoWerk looks like. Computed server-side at request time, so a
 * number somebody just changed is the number they see.
 */
export const getTenantOverview = (signal?: AbortSignal) =>
  apiFetch<TenantOverview>('/api/tenant/overview', { signal });

/**
 * Where an administrator goes to approve TodoWerk for everybody. A browser navigation rather than a
 * fetch: the backend answers with a redirect to Microsoft's admin-consent endpoint, and the round
 * trip ends back here.
 */
export const tenantConsentUrl = '/auth/tenant-consent';

/**
 * Where the erasure form posts. A real form, not a fetch, for the same reason sign-out is one: the
 * response ends in a redirect to the Entra ID end-session endpoint, which only a browser navigation
 * can follow.
 */
export const erasureUrl = '/api/me/erasure';

/** Every mutating request carries the same two headers, so no call site has to remember both. */
function mutatingHeaders(): Record<string, string> {
  return {
    'Content-Type': 'application/json',
    'X-CSRF-TOKEN': readAntiforgeryToken(),
  };
}

/**
 * The Teams tab's whole bootstrap: the token from `getAuthToken()`, traded for the session cookie
 * every other request here already uses. Nothing comes back — no body at all — because no token
 * reaches the client on any path (ADR-0002).
 *
 * No antiforgery header, and that is not an omission. The only credential this call carries is the
 * bearer token, which a cross-site page can neither obtain nor attach; the server records the
 * reason on the endpoint itself.
 */
export const exchangeTeamsSsoToken = (token: string) =>
  apiFetch<void>('/api/teams/session', {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}` },
  });

/**
 * The header the tab puts on the one request that follows the exchange, and nothing else ever
 * sends. It says what the request *is* — the confirmation immediately after an on-behalf-of
 * exchange that succeeded — which is the half of the blocked-cookie signature the server cannot
 * see for itself. A 401 answered to a request wearing this is that signature, and the server logs
 * it as such rather than as one more anonymous call.
 *
 * Asserted by the client and believed by nobody: it decides a log line and no access. Sending it
 * by hand buys a forged log entry and nothing else.
 */
export const teamsBootstrapHeader = 'X-TodoWerk-Teams-Bootstrap';

/**
 * The confirming request. Any authenticated read would do and this is the cheapest one the app
 * has; what makes it this function rather than {@link getCurrentUser} is the header above, which
 * is what puts the Safari diagnosis into the server's log as well as onto the person's screen.
 */
export const confirmTeamsSession = () =>
  apiFetch<SessionUser>('/api/me', { headers: { [teamsBootstrapHeader]: '1' } });

/**
 * Where the Teams tab sends somebody whose tenant has not approved TodoWerk: TodoWerk's own
 * server-side OpenID Connect flow, forced to show the consent screen, returning to the tab's
 * auth-end page. No MSAL.js, no implicit flow, no SPA redirect URI — the popup starts and ends on
 * this domain with a round trip to Microsoft in between, which is what `/auth/sign-in` already did
 * ([ADR-0010](../../../../../docs/adr/0010-teams-tab-session-and-framing.md)).
 */
export const teamsConsentUrl = (returnTo: string) =>
  `/auth/sign-in?returnUrl=${encodeURIComponent(returnTo)}&prompt=consent`;

/**
 * The last leg of erasure inside the tab. The data is already destroyed and the session cookie
 * already deleted by the time this runs; all it does is end the Microsoft session, which only a
 * top-level navigation can do.
 */
export const teamsEndSessionUrl = '/auth/teams-end-session';

/** Where an unauthenticated visitor goes to start the authorization-code flow. */
export const signInUrl = (returnTo: string = window.location.pathname) =>
  `/auth/sign-in?returnUrl=${encodeURIComponent(returnTo)}`;

/** Reads the antiforgery request token the backend publishes on every safe request. */
export function readAntiforgeryToken(): string {
  const match = document.cookie.match(/(?:^|;\s*)XSRF-TOKEN=([^;]*)/);
  return match ? decodeURIComponent(match[1]) : '';
}
