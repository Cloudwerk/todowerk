# One SQL database holds everything: the hashtag index TodoWerk maintains itself, sync state, and the job queue

Microsoft Graph offers no server-side text search over To Do tasks, so an inventory of hashtags cannot be computed on demand — TodoWerk maintains its own index: tag occurrences extracted from task titles, kept current by delta sync, with freshness shown in the UI and a manual re-scan available. That index, the delta-sync tokens, and the background job queue (including the per-task old-title/new-title journal that makes undo possible) all live in one SQL Server database, accessed through EF Core.

## Considered options

- **A second store for the index** (Redis, Cosmos DB) — rejected: the index is thousands of rows per tenant, not millions; filtered indexes and plain SQL queries cover every access pattern the Workbench has. A second store adds operational surface for the Hosted Service and, worse, for every Self-Host installation's compose file, with no v1 payoff.
- **An external job system** (Hangfire, a message broker) — rejected: jobs here are few, long-running, and tightly coupled to the journal rows they write; a database-backed queue drained by a hosted service does this with zero extra infrastructure, and the job's journal and its queue entry commit in one transaction.
- **No index — query Graph live** — not actually an option; listed for completeness. Graph cannot answer "which tags exist across 1,500 tasks" without fetching every task on every page load.

## Consequences

- Index freshness is a first-class UI concern (indicator plus re-scan), because an owned index is by definition sometimes stale.
- Writes never run against a half-scanned list; rename/merge enables per list as its scan completes.
- Self-Host needs exactly one backing service: a SQL Server. Nothing else to operate.

## Amendment (2026-09-22): freshness is maintained for people who are present

"Index freshness is a first-class concern" was written without a bound on *whose* index, and the
scheduler took it literally: every (tenant, user) whose lists had aged past `Indexing:SyncInterval`
was queued for a delta sync, every half hour, from the first sign-in to the end of the deployment.
Two things made that wrong once the Hosted Service was connected to its licensing authority
([ADR-0001](0001-single-entitlement-authority.md)). The worker asks whether a person is licensed
before it runs their scan, so an idle index cost one licence resolution per person per interval,
for a tenant nobody was in. And an unlicensed person's scan is held and re-queued rather than
dropped, so a person whose Licence had lapsed kept costing one too, indefinitely.

The bound is `Indexing:IdleAfter`. The scheduled delta sync is queued only for people whose Tenant
Member record carries an interactive sign-in inside that window; everybody else is Idle
([CONTEXT.md](../../CONTEXT.md)) and their index waits for them. Nothing else changes: a scan a
person requests, and the follow-up scan a Change queues, are human-initiated and run as before.
Returning needs no code, because the sign-in stamp lands in `OnTokenValidated` before the first
page renders and the next poll queues the overdue sync; the freshness indicator this decision
already required covers the seconds in between.

What this makes load-bearing is the sentence on `TenantMember` that a background scan is not
somebody using the product. It was written for the statistics ([ADR-0009](0009-what-todowerk-stores-about-a-person.md));
the schedule now reads the same record for the same reason, and a write to `LastSignedInAt` from
anywhere but a human sign-in would silently keep an absent person's index — and their Licence
checks — alive. The window must exceed the sync interval, which startup validation enforces, and
is deliberately a number no other setting uses, so that a test reading the log line can tell a
crossed wire from a coincidence.

The consequence above stands, narrowed: an owned index is by definition sometimes stale, and for
somebody who is present it is stale by at most the sync interval. For somebody who is not, it is
as stale as their absence, which is the honest number.
