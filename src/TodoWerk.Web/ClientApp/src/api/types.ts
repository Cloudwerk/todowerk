// Hand-maintained mirrors of the C# API contracts.
// Server counterparts: TodoWerk.Application (DTOs) and TodoWerk.Web endpoints.

/** RFC 7807 problem details, as produced by the backend for every error response. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  [extension: string]: unknown;
}

/** Mirror of TodoWerk.Application.Indexing.TaskListKind. */
export type TaskListKind = 'Normal' | 'Default' | 'FlaggedEmails' | 'Unknown';

/** Mirror of TodoWerk.Application.Indexing.TaskListDto. */
export interface TaskList {
  id: string;
  displayName: string;
  kind: TaskListKind;
  /** Shared with other people, so a rename is visible to all of them. */
  isShared: boolean;
  isOwner: boolean;
  /** Derived server-side from `kind`. */
  isDefault: boolean;
  /** Derived server-side from `kind`: created by the service, so not renameable. */
  isSystemList: boolean;
}

/** Session info returned by GET /api/me. */
export interface SessionUser {
  displayName: string;
  username: string;
}

/** Mirror of TodoWerk.Domain.Indexing.ListScanState. */
export type ListScanState = 'NeverScanned' | 'Scanning' | 'Indexed' | 'Failed';

/**
 * Mirror of TodoWerk.Domain.Failures.FailureCode. What the client switches on; the sentence
 * beside it is what the reader sees. Whether to offer a sign-in button is decided from the code,
 * never by looking for a phrase in that sentence.
 */
export type FailureCode =
  | 'None'
  | 'ReconnectRequired'
  | 'Throttled'
  | 'Unavailable'
  | 'NotFound'
  | 'Unknown';

/** Mirror of TodoWerk.Application.Indexing.IndexActivity. */
export type IndexActivity = 'Idle' | 'Queued' | 'Scanning';

/** Mirror of TodoWerk.Application.Indexing.TaskListStatusDto. */
export interface TaskListStatus {
  taskListId: string;
  displayName: string;
  state: ListScanState;
  tasksIndexed: number;
  lastSuccessfulSyncAt: string | null;
  lastCompletedScanAt: string | null;
  failureReason: string | null;
  failureCode: FailureCode;
}

/** Mirror of TodoWerk.Application.Indexing.IndexStatusDto. */
export interface IndexStatus {
  activity: IndexActivity;
  /** Oldest successful sync across the lists: the index is only as fresh as its stalest list. */
  currentAsOf: string | null;
  hasCompletedFirstScan: boolean;
  listCount: number;
  listsIndexed: number;
  tasksIndexed: number;
  lists: TaskListStatus[];
  /** Why the last scan gave up, when nothing has succeeded since. */
  lastScanFailure: string | null;
  lastScanFailureCode: FailureCode;
}

/** Mirror of TodoWerk.Application.Indexing.HashtagInventoryRow. */
export interface HashtagInventoryRow {
  key: string;
  /** The Spelling shown to the user; the others are in `spellings`. */
  canonicalSpelling: string;
  spellings: string[];
  taskCount: number;
  listCount: number;
  lastUsedAt: string;
  /** One Hashtag written more than one way. Resolving it is Normalise Casing, never a Merge. */
  hasMultipleSpellings: boolean;
  hasNearDuplicates: boolean;
  isStale: boolean;
}

/** Mirror of TodoWerk.Application.Indexing.HashtagInventoryPage. */
export interface HashtagInventoryPage {
  rows: HashtagInventoryRow[];
  /** Rows matching the filter, not rows on this page. */
  totalCount: number;
  page: number;
  pageSize: number;
}

/** Mirror of TodoWerk.Application.Indexing.HashtagInventorySort. */
export type HashtagInventorySort = 'TaskCount' | 'Spelling' | 'LastUsed' | 'ListCount';

/** Mirror of TodoWerk.Application.Indexing.HashtagInventoryFilter. */
export type HashtagInventoryFilter = 'None' | 'MultipleSpellings' | 'NearDuplicates' | 'Stale';

/** The query behind GET /api/hashtags. Sorting and paging are the server's work. */
export interface HashtagInventoryQuery {
  search?: string;
  filter?: HashtagInventoryFilter;
  sort?: HashtagInventorySort;
  descending?: boolean;
  page?: number;
  pageSize?: number;
}

/** Mirror of TodoWerk.Application.Indexing.RequestIndexScan.IndexScanRequestedDto. */
export interface IndexScanRequested {
  /** False when a scan was already in flight — not a failure, the same answer. */
  queued: boolean;
}

/**
 * Mirror of TodoWerk.Domain.Changes.ChangeKind. The first three are derived from the Change's
 * shape, never chosen — one mechanism, three names it reports back (ADR-0006). The last two are
 * stated, because they have no target spelling for anything to be read off (ADR-0014), and they
 * are two names rather than one because everything that reads 'ApplyMarkers' reads it as the
 * promise that applying never removes.
 */
export type ChangeKind = 'NormaliseCasing' | 'Rename' | 'Merge' | 'ApplyMarkers' | 'RemoveMarkers';

/** Mirror of TodoWerk.Domain.Changes.ChangeState. The last four are terminal. */
export type ChangeState =
  | 'Pending'
  | 'Running'
  | 'Completed'
  | 'CompletedWithSkips'
  | 'Failed'
  | 'Cancelled';

/** Mirror of TodoWerk.Application.Changes.ChangePreviewItemDto. */
export interface ChangePreviewItem {
  taskListId: string;
  listDisplayName: string;
  currentTitle: string;
  newTitle: string;
}

/** Mirror of TodoWerk.Application.Abstractions.Indexing.ExcludedTaskList. */
export interface ExcludedTaskList {
  taskListId: string;
  displayName: string;
  /** Why it contributes nothing, worded by the server. */
  reason: string;
}

/** Mirror of TodoWerk.Application.Changes.ChangePreviewSkipDto. */
export interface ChangePreviewSkip {
  taskListId: string;
  listDisplayName: string;
  currentTitle: string;
  /** Why it will be passed over, worded by the server. */
  reason: string;
}

/** Mirror of TodoWerk.Application.Changes.ChangeMarkerRuleDto. */
export interface ChangeMarkerRule {
  key: string;
  spelling: string;
  marker: string;
}

/**
 * Mirror of TodoWerk.Application.Changes.ChangeMarkerRulesDto. What this Change would do to the
 * Marker Rules it involves — nothing here writes a title (ADR-0014).
 */
export interface ChangeMarkerRules {
  sourceRules: ChangeMarkerRule[];
  /** The target's own rule, which wins over any source rule. */
  targetRule: ChangeMarkerRule | null;
  /** The marker the surviving hashtag ends up with, or null while the user has not chosen. */
  survivingMarker: string | null;
  /** Several sources carry a marker and the target carries none: only the user can settle it. */
  requiresSurvivorChoice: boolean;
}

/** Mirror of TodoWerk.Application.Changes.ChangePreviewDto. */
export interface ChangePreview {
  kind: ChangeKind;
  sourceKeys: string[];
  /** Empty for an Apply Markers, which rewrites the block and no hashtag at all. */
  targetSpelling: string;
  items: ChangePreviewItem[];
  /** Advisory: the run re-reads each task and may skip it (ADR-0006). */
  taskCount: number;
  /** A Merge destroys a distinction the user made, so it is the one operation that asks twice. */
  requiresMergeConfirmation: boolean;
  excludedLists: ExcludedTaskList[];
  /** Tasks the plan already knows it will pass over, named before anybody confirms. */
  skips: ChangePreviewSkip[];
  markerRules: ChangeMarkerRules;
}

/** Mirror of TodoWerk.Application.Changes.ChangeDto. */
export interface Change {
  id: string;
  kind: ChangeKind;
  /** For an Apply Markers this is the scope: every rule's hashtag, or the one it was started from. */
  sourceKeys: string[];
  targetSpelling: string;
  /** The Marker Rules this Change carries, or empty. What lets the queue name what it is doing. */
  appliedMarkers: AppliedMarker[];
  state: ChangeState;
  plannedTaskCount: number;
  writtenCount: number;
  skippedCount: number;
  failedCount: number;
  cancelRequested: boolean;
  requestedAt: string;
  completedAt: string | null;
  failureCode: FailureCode;
  failureReason: string | null;
  /** Set when this Change is the undo of another one. Undo is one level deep. */
  undoOfChangeId: string | null;
  /** Decided by the server: whole-Change, one level deep, inside retention, something written. */
  canUndo: boolean;
}

/** Mirror of TodoWerk.Domain.Changes.AppliedMarker. */
export interface AppliedMarker {
  key: string;
  spelling: string;
  marker: string;
  retiredMarker: string | null;
  position: number;
  /**
   * The marker of a rule the person has deleted, carried so the block is still read whole. Never
   * the one an Apply is about, and never something to show.
   */
  abandoned: boolean;
  /** This marker is in a Remove Markers' scope. Always false for an Apply, which removes nothing. */
  removable: boolean;
}

/**
 * Mirror of TodoWerk.Application.Markers.MarkerRuleDto. One person's standing instruction that a
 * hashtag carries an emoji. A rule declares; only an Apply Markers writes (ADR-0014).
 */
export interface MarkerRule {
  id: string;
  key: string;
  spelling: string;
  marker: string;
  /**
   * Waiting for the next Apply to swap it out of the blocks that still carry it. Kept whatever
   * `retiredTaskCount` says — the rule is what lets a later apply reach the emoji.
   */
  retiredMarker: string | null;
  position: number;
  /** Tasks carrying the hashtag, as the last scan left them. */
  taggedTaskCount: number;
  /** How many of those already carry the marker at the front — "n of m tagged tasks carry 🍞". */
  markedTaskCount: number;
  /**
   * Tasks that no longer carry the hashtag and still carry this marker at the front, or the one
   * this rule retired. What a Remove Markers scoped to this rule would take away.
   */
  staleTaskCount: number;
  /**
   * How many tasks the pending swap has still to reach. Nought means it has arrived everywhere the
   * index can see, and the row says nothing about a replacement that would replace nothing.
   */
  retiredTaskCount: number;
}

/**
 * Mirror of TodoWerk.Application.Markers.AbandonedMarkerDto. A marker whose rule is gone and which
 * is still at the front of at least one task.
 *
 * It has no hashtag to name, because the rule that knew which hashtag it was about is deleted. This
 * is the only place in the product those emoji are visible at all — a deleted rule is listed
 * nowhere — so without it they would sit in somebody's titles with nothing offering to take them
 * out.
 */
export interface AbandonedMarker {
  marker: string;
  staleTaskCount: number;
}

/** Mirror of TodoWerk.Application.Markers.MarkerRuleListDto. */
export interface MarkerRuleList {
  rules: MarkerRule[];
  /** Markers left behind by deleted rules. Empty once nothing carries them. */
  abandoned: AbandonedMarker[];
}

/** Mirror of TodoWerk.Application.Markers.MarkerRuleMove. */
export type MarkerRuleMove = 'Up' | 'Down';

/** Mirror of TodoWerk.Application.Changes.ChangeQueueDto. */
export interface ChangeQueue {
  /** The Change that is pending or running. There is at most one per user. */
  active: Change | null;
  history: Change[];
}

/**
 * Mirror of TodoWerk.Application.Onboarding.TenantActivityWindowDto. The window arrives as a
 * number of days so the label is generated from the figure — the longest of the three is the
 * retention window from server configuration, and a hard-coded "last year" would start lying the
 * day somebody changed it.
 */
export interface TenantActivityWindow {
  windowDays: number;
  memberCount: number;
}

/** Mirror of TodoWerk.Application.Onboarding.TenantStatisticsDto. Counts only — never a person. */
export interface TenantStatistics {
  /** Cumulative: everybody who ever signed in, the forgotten included (ADR-0009). */
  memberCount: number;
  /** Shortest window first. */
  activity: TenantActivityWindow[];
  firstSignedInAt: string;
  totalOccurrences: number;
  /**
   * Averaged over the people still on record, not over `memberCount` — forgotten members hold no
   * Occurrences, so the cumulative denominator would only water the figure down. The server does
   * the division; the denominator itself is not sent.
   */
  averageOccurrencesPerMember: number;
}

/** Mirror of TodoWerk.Application.Onboarding.TenantOverviewDto. */
export interface TenantOverview {
  /**
   * Null while fewer identifiable people are on record than the server's configured floor — people
   * who have been forgotten count toward `memberCount` but not toward the floor, because the floor
   * protects the people the figures still describe. Absent rather than zeroed, and nothing is
   * drawn in its place: a total across three people is those three people's data wearing a
   * statistics label, and an empty state inviting somebody to wonder why is worse than an absence.
   */
  statistics: TenantStatistics | null;
  /**
   * Whether an administrator approved TodoWerk for this tenant *through TodoWerk*. A grant made in
   * the Entra portal is invisible to it, so false means "not recorded here", never "not approved".
   */
  tenantConsentGrantedThroughTodoWerk: boolean;
}

/** Mirror of TodoWerk.Domain.Licensing.LicenceKind. Who the Licence covers, never a feature set. */
export type LicenceKind = 'Tenant' | 'Personal' | 'Trial';

/** Mirror of TodoWerk.Domain.Licensing.LicenceBannerState. */
export type LicenceBannerState = 'None' | 'TrialRunning' | 'TrialEndingSoon';

/**
 * Mirror of TodoWerk.Web.Documents.DocumentGroup. The product explaining itself, or what somebody
 * signed in under — which is where the help menu draws its divider, rather than at a position it
 * decided for itself.
 */
export type DocumentGroup = 'Handbook' | 'Legal';

/** Mirror of TodoWerk.Web.Endpoints.HandbookDocumentDto. */
export interface HandbookDocument {
  text: string;
  /** A path on this host, or the Guide's absolute address. */
  href: string;
  group: DocumentGroup;
}

/**
 * Mirror of TodoWerk.Web.Endpoints.HandbookDto, from GET /api/handbook. The documents this
 * deployment offers a reader, in the order to offer them — the same list, from the same
 * `DocumentNav`, that the header of every served document is built from, so the two cannot
 * disagree about what the document set is.
 *
 * The client holds no set, no order and no address of its own. A Self-Host has no Guide, so the
 * list simply does not carry one and no entry is drawn rather than a dead one (ADR-0013).
 */
export interface Handbook {
  documents: HandbookDocument[];
}

/** Mirror of TodoWerk.Application.Licensing.LicenceDto, from GET /api/licence. */
export interface Licence {
  /** Null on a Self-Host, where there is no Licence and the panel is absent rather than empty. */
  kind: LicenceKind | null;
  /** Null when the portal named no end. */
  endsAt: string | null;
  banner: LicenceBannerState;
  /**
   * Whether the invitation to grant Tenant Consent may be shown. False under a Personal Licence:
   * paying for one seat is not standing to approve TodoWerk for an organisation (ADR-0012). True
   * on a Self-Host, where the invitation behaves as it always did.
   */
  mayOfferTenantConsent: boolean;
  /** Where the browser's purchase links point, or null for no link at all rather than a dead one. */
  purchaseUrl: string | null;
}

/**
 * The two problem codes a denied person meets, from
 * TodoWerk.Application.Licensing.LicensingErrors. Kept apart because one of them is met by people
 * who have paid, during an outage, and must never read as the other.
 */
export const licenceEndedCode = 'Licensing.Ended';

export const licenceCouldNotBeVerifiedCode = 'Licensing.CouldNotBeVerified';
