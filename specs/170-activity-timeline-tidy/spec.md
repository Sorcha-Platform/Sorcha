# Feature Specification: Activity Timeline Tidy

**Feature Branch**: `170-activity-timeline-tidy`

**Created**: 2026-06-28

**Status**: Draft

**Input**: User description: "Activity timeline tidy: now that F169 unified timeline reads the Inbox spine, drop the now-legacy ActivityEvent table + its indexes and remove IActivityLogService and any Feature-125 TransactionHistoryFeed/RecentActivityFeed remnants not used by F169; squash the schema change into the initial EF migrations (pre-release convention; n1 DB reset is acceptable). Spec docs/superpowers/specs/2026-06-27-transaction-activity-logging-review.md tidy phase."

## Context

Feature 169 ("unified activity timeline") established the **Inbox spine** as the single source of truth for what a user sees in their activity timeline. Before F169, activity was assembled from several parallel channels:

- A server-side **ActivityEvent** store in the Tenant Service (its own table, indexes, retention worker, and `/api/events` REST surface), written to by the persona lifecycle and the blueprint encryption pipeline.
- An orphaned admin-facing **activity log** client service in the web UI.
- Feature-125 **transaction/recent-activity feed** UI components that merged verification history, presentation history, and a device-local log.

With F169 in place, the same events now flow into and are read from the Inbox spine, leaving the older channels as redundant, dead, or superseded code. This feature is the **tidy phase** that removes the now-legacy surface so there is exactly one activity pipeline to reason about and maintain.

This is a pre-release housekeeping change. No production data migration is required — a fresh database reset of the `n1` development network is acceptable, so the schema removal is folded directly into the existing initial migration rather than added as a new forward migration.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - No regression in what the user sees (Priority: P1)

A citizen (in the wallet) and an administrator (in the web app) continue to see their complete activity timeline after the legacy channels are removed. Every event that used to appear — persona changes, encryption-pipeline outcomes, presentations, verifications — still appears, because it is now sourced from the Inbox spine that F169 reads.

**Why this priority**: The single hard constraint of a tidy is "no user-visible loss." Removing a parallel channel must not silently drop any event class from the timeline. If this fails, the cleanup has broken the product.

**Independent Test**: With F169 merged, trigger each event that previously wrote to the legacy ActivityEvent store (replace a persona, delete a persona, complete and fail an encryption job) and confirm each one appears in the user's unified timeline both before and after the legacy channel is removed.

**Acceptance Scenarios**:

1. **Given** the unified timeline (Inbox spine) is the live source and the legacy ActivityEvent channel has been removed, **When** a user replaces or deletes a persona, **Then** the corresponding event appears in their unified timeline.
2. **Given** the legacy channel has been removed, **When** an encryption job completes or fails, **Then** the corresponding event appears in the relevant timeline exactly as it did before removal.
3. **Given** the legacy channel has been removed, **When** the user opens their activity timeline, **Then** the set of events shown is the same as (or a superset of) what was shown before removal — no event class is missing.

---

### User Story 2 - Reduced maintenance surface for the team (Priority: P2)

A developer working on activity-related features finds exactly one activity pipeline (the Inbox spine) and no dead parallel code. The legacy ActivityEvent store, its server-side service/endpoints/retention worker, its HTTP client, the orphaned UI activity-log service, and any superseded Feature-125 feed components have been deleted. The solution builds and all tests pass.

**Why this priority**: The whole point of the tidy is to eliminate the confusion and upkeep cost of duplicate channels. It is second only to "don't break the user."

**Independent Test**: Search the codebase for the legacy types (the activity-event entity, its service and client interfaces, the admin activity-log service, and the superseded feed components) and confirm they no longer exist; build the solution and run the test suite to confirm green.

**Acceptance Scenarios**:

1. **Given** the tidy is complete, **When** a developer searches for the legacy activity-event service, its HTTP client, and the orphaned UI activity-log service, **Then** none of them remain in the codebase.
2. **Given** the tidy is complete, **When** the solution is built, **Then** it compiles with no references to the removed types and with no new build warnings introduced by the change.
3. **Given** the tidy is complete, **When** the full test suite runs, **Then** all tests pass, and tests that existed only to cover the removed legacy surface have been removed rather than left failing or skipped.
4. **Given** a Feature-125 feed component is still referenced by the F169 unified timeline, **When** the tidy is applied, **Then** that component is **retained**; only feed components with no remaining F169 reference are removed.

---

### User Story 3 - Clean schema on a fresh provision (Priority: P3)

An operator provisioning a fresh Tenant Service database (including an `n1` reset) gets a schema with no legacy activity-event table or indexes. The removal is folded into the existing initial migration, so there is no orphan table and no extra "drop" migration in the history.

**Why this priority**: Schema cleanliness matters for long-term clarity but has no user-facing impact in this pre-release window; it can follow the code removal.

**Independent Test**: Provision a fresh Tenant Service database from migrations and confirm the legacy activity-event table and its indexes are absent, and that the migration history contains no separate drop-step for it.

**Acceptance Scenarios**:

1. **Given** the schema change is squashed into the initial migration, **When** a fresh database is provisioned from migrations, **Then** no legacy activity-event table or indexes are created.
2. **Given** the migration model snapshot, **When** it is inspected, **Then** it contains no definition of the legacy activity-event entity.
3. **Given** the team's pre-release reset convention, **When** the change ships, **Then** an `n1` database reset is the accepted upgrade path and no forward data migration is provided.

---

### Edge Cases

- **Coverage gap**: An event class that was written **only** to the legacy ActivityEvent store and is **not** yet emitted into the Inbox spine by F169 would disappear from the timeline on removal. Coverage of every legacy writer by an equivalent Inbox writer MUST be verified before the corresponding writer is removed (see FR-001).
- **External consumers of the legacy REST surface**: Any out-of-process consumer (CLI, MCP, external integration, test harness) still calling the legacy `/api/events*` endpoints would receive 404 after removal. The tidy MUST confirm there are no such remaining consumers before deleting the endpoints.
- **Partially-shared feed component**: A Feature-125 feed component may be partly reused by the F169 timeline (e.g. the home/recent feed). Such a component MUST be kept; only components with no F169 reference are removed.
- **Retention behaviour change**: The legacy channel included a scheduled retention/cleanup worker. Removing it MUST NOT leave a dangling scheduled job registration, and any equivalent retention expectation is now the Inbox spine's responsibility (out of scope here).
- **Dangling DI registrations**: Removing services and hosted/background workers MUST also remove their dependency-injection and endpoint-mapping registrations so the host starts cleanly with no unresolved references.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Before removing any legacy writer, the system MUST confirm that every event class it produced is already emitted into the Inbox spine that the F169 unified timeline reads, so no event class is lost from the user-visible timeline.
- **FR-002**: The legacy server-side activity-event store MUST be removed: the activity-event entity, its data-context registration, and its indexes.
- **FR-003**: The legacy server-side activity-event service, its interface, its REST endpoints, and its scheduled retention/cleanup worker MUST be removed, along with their dependency-injection and endpoint-mapping registrations.
- **FR-004**: The legacy activity-event HTTP client (its interface, implementation, and request/response models) MUST be removed, along with its client registration; remaining callers MUST be re-pointed to the Inbox spine or removed as part of FR-001 verification.
- **FR-005**: The orphaned admin-facing activity-log UI service (its interface, implementation, registration, and its now-unused data-transfer/response models) MUST be removed.
- **FR-006**: Feature-125 transaction/recent-activity feed components and their host pages that are **superseded** by the F169 unified timeline and have **no remaining F169 reference** MUST be removed; any feed component still referenced by the F169 timeline MUST be retained.
- **FR-007**: The schema removal MUST be folded into the existing initial database migration (and its model snapshot) rather than introduced as a separate forward migration, consistent with the pre-release reset convention; an `n1` database reset is the accepted upgrade path.
- **FR-008**: After the tidy, the solution MUST build with no references to any removed type and the full test suite MUST pass; tests that existed solely to cover removed legacy code MUST be removed rather than left failing or skipped.
- **FR-009**: Documentation that describes the removed legacy activity-logging surface (service READMEs, architecture/skill references, API documentation, status docs) MUST be updated to reflect the Inbox spine as the single activity source, per the project documentation-sync policy.
- **FR-010**: The change MUST NOT remove or weaken any code that the F169 unified timeline depends on; only the redundant, dead, or superseded legacy surface is in scope.

### Key Entities *(include if feature involves data)*

- **Legacy ActivityEvent store**: The to-be-removed server-side activity record (per-user / per-organization event rows with type, severity, title, message, source, read-state, and expiry), along with its indexes and scheduled retention worker. Superseded by the Inbox spine.
- **Inbox spine**: The retained single source of truth for the unified activity timeline (established by F169). This feature reads from it but does not modify it.
- **Legacy activity-event REST/HTTP surface**: The to-be-removed `/api/events*` endpoints, the server service behind them, and the cross-service HTTP client plus request/response models that fed the legacy store.
- **Orphaned UI activity-log service**: The to-be-removed admin-facing client service and its data-transfer/response models, which already have no consumers.
- **Feature-125 feed components**: Transaction-history and recent-activity feed UI components; removed only where superseded and unreferenced by F169, retained where F169 still uses them.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After removal, 100% of event classes that previously appeared in the user's activity timeline still appear (zero regression in visible event classes).
- **SC-002**: Exactly one activity pipeline remains in the codebase; a search for the legacy activity-event store, its service, its HTTP client, and the orphaned UI activity-log service returns zero remaining definitions.
- **SC-003**: The solution builds successfully and the full test suite passes, with no tests left failing or skipped as a result of the removal.
- **SC-004**: A fresh database provision (and an `n1` reset) produces a schema with no legacy activity-event table or indexes, and the migration history contains no separate drop-step for it.
- **SC-005**: All documentation references to the removed legacy activity-logging surface are updated to describe the Inbox spine as the single activity source.

## Assumptions

- **Depends on Feature 169**: This tidy assumes the F169 unified-timeline work (Inbox spine as source, plus the Inbox writers that absorb persona-lifecycle and encryption-pipeline events) is merged first. The removal is only safe once those Inbox writers cover every event class the legacy channel produced.
- **Legacy writers are now redundant**: The persona-lifecycle and encryption-pipeline writes to the legacy ActivityEvent store are assumed to be duplicated by equivalent Inbox writes under F169; if any are not yet covered, that coverage is added (or the write re-pointed) as part of FR-001 before deletion.
- **No external consumers of `/api/events*`**: It is assumed no out-of-process consumer (CLI, MCP, external integration) still depends on the legacy activity-event endpoints; this is verified before the endpoints are removed.
- **Pre-release reset is acceptable**: There is no production data to preserve; squashing the schema change into the initial migration with an `n1` reset is the agreed upgrade path, so no forward "drop table" migration is provided.
- **Scope is removal only**: This feature removes the legacy surface and re-points any straggler writers; it does not change the behaviour, schema, or read model of the F169 Inbox spine.
- **Source of truth**: The detailed inventory of what is dead vs. still-used is captured in `docs/superpowers/specs/2026-06-27-transaction-activity-logging-review.md` (the tidy phase of that review); this spec governs the removal work.
