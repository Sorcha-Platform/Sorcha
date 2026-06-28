# Phase 0 Research: Unified Activity Timeline Read-Path

All open questions from the spec's "to be finalised in planning" notes are resolved below. No `NEEDS CLARIFICATION` markers remain.

---

## R1. How is Actionable/Informational represented — derived vs. stored?

- **Decision**: **Derived at read time** from the existing `InboxCategory` + `InboxSeverity`. No new column, no migration.
- **Rationale**: FR-012 requires the classification be "derivable/visible at read time … without requiring a destructive change to the existing category and severity information," and FR-019 bars schema churn this run. The spine already carries enough signal. A pure derivation is the quick win and keeps the bell's unread count server-authoritative without a data backfill.
- **Canonical mapping** (single source of truth, mirrored server + client):

  > **Actionable** ⇔ `Category == Action` **OR** `Severity ∈ { ActionRequired, Critical }`. Everything else (incl. unclassifiable / unknown) is **Informational**.

- **Default fallback** (FR-011): an entry with no explicit signal lands in **Informational** (it still appears on the timeline, never crowds the bell). Security-critical alerts (`Severity == Critical`, or `Category == Security` at `ActionRequired+`) resolve to **Actionable** so they are never hidden from the bell (spec Assumptions).
- **Alternatives considered**:
  - *Add a `Classification` column* — rejected: violates the "no destructive/schema change" intent of FR-012/FR-019 and needs a backfill for historical rows.
  - *Client-only derivation* — rejected: the bell's unread badge is server-authoritative (`/api/me/inbox/unread-count`); the Actionable predicate must be computable in SQL so the count is correct without shipping all rows to the client.

## R2. Where does the Actionable predicate live so the bell count stays server-authoritative?

- **Decision**: Add an `actionableOnly` boolean to `IInboxStore.GetPageAsync` and `GetUnreadCountAsync`; `EfCoreInboxStore` expresses the predicate as `e.Category == InboxCategory.Action || e.Severity >= InboxSeverity.ActionRequired`, which EF Core translates to SQL (enum-to-int comparison). `InboxService` passes the flag through; the unread-count path always sets `actionableOnly: true`.
- **Rationale**: Keeps one query layer authoritative; reuses the existing `(PlatformUserId, OccurredAt)` and `(PlatformUserId, Category, OccurredAt)` indexes. The predicate is a thin filter on already-indexed columns — adequate for the quick-win scale; a dedicated partial index can be added later if profiling shows need (logged, not pre-emptive).
- **Alternatives considered**: client-side filtering of a full page (rejected — wrong unread count, wasted transfer); a materialised `is_actionable` computed column/index (rejected for this run — schema change deferred).

## R3. Bell vs. Activity surface — which calls what?

- **Decision**:
  - **Bell drawer** (`InboxPanel.razor`) → `ListAsync(actionableOnly: true)`; badge already driven by the now-Actionable-scoped `unread-count` endpoint + TenantHub `InboxUnreadCountUpdated`.
  - **Activity timeline** (`ActivityFeed.razor`) → `ListAsync(actionableOnly: false)` (everything), with paging.
- **Rationale**: FR-004 (timeline shows all), FR-009/FR-010 (bell + badge Actionable-only). The live-update pipeline is unchanged; the server simply counts a narrower set.

## R4. Classification of the two rerouted producers

- **Decision** (FR-016), using the R1 mapping:

  | Producer event | InboxCategory | InboxSeverity | Derived class | Appears in bell? |
  |----------------|---------------|---------------|---------------|------------------|
  | Profile **save** (`persona.replaced`) | `System` | `Info` | Informational | No |
  | Profile **delete** (`persona.deleted`) | `System` | `Warning` | Informational | No |
  | Encryption **complete** | `Workflow` | `Info` | Informational | No |
  | Encryption **fail** | `Workflow` | `ActionRequired` | **Actionable** | **Yes** |

- **Rationale**: Profile saves/deletes are awareness-only (Assumptions: "profile saved" = Informational). Encryption completion is informational. Encryption **failure** must "surface so the user is alerted appropriately" (FR-016) — mapping it to `ActionRequired` puts it in the bell where the user will see it and can retry their submission, satisfying the alert requirement via the single canonical mapping rather than a special case. `Category` choices reuse existing enum members (`System` for profile lifecycle, `Workflow` for the encryption pipeline) — no new enum members needed.

## R5. The "relevant user" for encryption complete/fail

- **Decision**: Attribute to `workItem.UserId`.
- **Rationale**: The `EncryptionWorkItem` already carries `UserId`, and it is already used as the recipient for `NotifyEncryptionComplete`/failure notifications (`EncryptionBackgroundService` lines 275, 376). It is the operation's owner/initiator — exactly the spec's "relevant user" (Assumptions). No wallet→participant resolution hop is required. If `UserId` is `Guid.Empty` (system-originated), the writer logs and skips (best-effort), never blocking the pipeline.

## R6. Inbox-writer pattern for the reroutes (best-effort + idempotency)

- **Decision**: Add two thin writers following the established idiom:
  - `PersonaInboxWriter` (Tenant Service) — injects `IInboxService` (in-process), writes `System` category entries. PersonaService already lives in Tenant Service, so DI is direct.
  - `EncryptionInboxWriter` (Blueprint Service) — injects `IPlatformInboxClient` (cross-service HTTP to `POST /api/internal/inbox`), writes `Workflow` category entries. Mirrors `BlueprintInboxWriter`.
  - Both wrap the write in `try` / `LogWarning` / swallow (FR-015 best-effort); the legacy `ActivityEvent` emit is **kept in place** alongside the new write (no behaviour removed this run).
- **Idempotency** (FR-018): `SourceEventId` is a deterministic GUID derived (SHA-1 fold, as in `TenantSecurityInboxWriter`/`BlueprintInboxWriter`) from a stable key:
  - Persona: `sorcha.inbox.persona.{replaced|deleted}:{platformUserId:N}:{occurredAtUnixSeconds}`
  - Encryption: `sorcha.inbox.encryption.{complete|fail}:{operationId}` (operationId is unique per operation — retries collapse to one entry).
- **Rationale**: Reuses the proven `(PlatformUserId, SourceEventId)` unique-index dedup in `EfCoreInboxStore.AddOrFindAsync`. Re-emission of the same source event is absorbed by the spine.
- **Alternatives considered**: reusing `TenantSecurityInboxWriter` for persona (rejected — wrong `Security` category); a single shared writer (rejected — the two live in different services with different transports).

## R7. Single shared component placement & host wiring

- **Decision**: One `ActivityFeed.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Activity/`. Web `/app` gets a new `Activity.razor` page hosting it; the PWA's existing `/activity` page is re-pointed from `TransactionHistoryFeed` (verification/presentation merge) to `<ActivityFeed/>` reading the spine.
- **Rationale**: `Sorcha.UI.Components.User` is the lowest user-facing library both hosts reference (PWA directly; web via `Sorcha.UI.Core` re-export) — satisfies SC-008 (single implementation) and keeps the PWA bundle gate (`scripts/check-pwa-bundle.ps1`) green (no designer/admin deps introduced). The component depends only on `IInboxApiService` + the client-side `ActivityClassification` helper, both already PWA-safe.
- **Note**: The PWA's prior verification/presentation merge is out of this feature's read source (the spine is authoritative — Assumptions); whether to retain that merge alongside the spine timeline is a follow-up product decision and is flagged, not silently dropped. For this quick win the `/activity` surface reads the spine.

## R8. Responsive & paging behaviour

- **Decision**: Reuse MudBlazor responsive primitives already used by `InboxPanel`/`TransactionHistoryFeed`; incremental "load more" against `GetPageAsync` (newest-first, page size ≤100) with an explicit "more available" affordance derived from `TotalCount` vs. loaded count (FR-006). Empty state via the existing `EmptyState` shared component (FR-007). No Snackbar (Pattern #12).
- **Rationale**: Matches existing patterns; satisfies FR-005/FR-006/FR-007 and SC-006 without new infrastructure.

## R9. Idempotency & duplicate suppression for retried producers

- **Decision**: Rely entirely on the spine's existing `(PlatformUserId, SourceEventId)` unique constraint plus the deterministic `SourceEventId` keys from R6. No new dedup logic.
- **Rationale**: FR-018 / edge case "Duplicate suppression" — the spine already returns the existing entry on a duplicate write (`AddOrFindAsync` → `IsIdempotent`), so retried encryption notifications or repeated saves within the same second collapse correctly.

---

### Resolved unknowns summary

| Spec open item | Resolution |
|----------------|-----------|
| Exact category/severity → Actionable mapping | R1 + R4 table |
| Default for unclassified entry | Informational (R1) |
| Where the bell's Actionable count is computed | Server-side SQL predicate (R2) |
| "Relevant user" for encryption events | `workItem.UserId` (R5) |
| Encryption-fail classification ("alert appropriately") | `Workflow` + `ActionRequired` → Actionable (R4) |
| Component location / shared-ness | `Sorcha.UI.Components.User/Components/Activity` (R7) |
