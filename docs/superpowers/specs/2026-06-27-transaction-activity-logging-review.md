# Transaction & Activity Logging — Comprehensive Review

**Date:** 2026-06-27
**Author:** review commissioned by Stuart (observation: "I don't think I've ever seen items in there — the only thing that works is the bell/inbox")
**Status:** review / findings — not yet a build spec

---

## TL;DR

Sorcha has **five disjoint "history-ish" systems** and **two of them are fully wired while the rest are stubs, ephemeral, or orphaned**. The one the user expects to see ("recent activity" / "transaction history") is the `ActivityEvent` system, which is **orphaned at both ends**: the UI never reads it, and almost nothing writes to it. The notification **Inbox** works because it has writers wired everywhere *and* a UI consumer. The two are parallel, half-built implementations of overlapping ideas.

There is also **no concept of an "off-register" transaction** anywhere in the model.

---

## 1. The five systems (current state)

| # | System | Model / store | Read path | Write path | Status |
|---|--------|---------------|-----------|------------|--------|
| 1 | **Register ledger transactions** | `TransactionModel` + `Docket` (Mongo) | `/api/register/query/wallets/{addr}/transactions` → web **My Transactions** (`/my-transactions`) | Register/validator seal flow | ✅ works (ledger data) |
| 2 | **Wallet transaction history** | `WalletTransaction` (EF; State, Direction, ReceiptId) | — not surfaced in any page found — | wallet domain | ⚠️ exists, unsurfaced |
| 3 | **Encryption Operations** | `EncryptionOperation` (in-memory, **1-hour TTL**) | `/api/operations?wallet=` → web **Operations** (`/operations`) | `EncryptionBackgroundService` during blueprint action exec | ✅ works, but ephemeral & transient (progress tracking, not history) |
| 4 | **Activity feed** | `ActivityEvent` (Tenant, Postgres) + `ActivityEventDto` | `/api/events` via `IActivityLogService` | only 2 writers | ❌ **orphaned both ends** |
| 5 | **Presentation/verification history** | `IVerificationHistoryStore` (IndexedDB), `IPresentationLog`, `ICitizenWalletClient.ListPresentationsAsync` | PWA **Activity** page (`Activity.razor`) | verification/presentation flows | ✅ works (PWA only) |
| — | **Notification Inbox** (for contrast) | inbox stores | `IInboxApiService` → bell drawer | `WalletInboxWriter`, `WalletWorkflowInboxWriter`, `CitizenDeviceInboxWriter`, `TenantSecurityInboxWriter` (wired everywhere) | ✅ works |

## 2. Why the feeds are always empty (root cause)

The `ActivityEvent` system (#4) is what "Recent Activity" / "Transaction History" feeds are *meant* to show. It is dead on both sides:

- **Read path never connected.** `IActivityLogService` is registered (`ServiceCollectionExtensions.cs:350`) and calls `GET /api/events`, but **nothing injects or calls it.** The home feeds are hardcoded `Array.Empty<…>` (`Sorcha.Wallet.Pwa/Pages/Index.razor:248-249`) with a comment deferring real wiring to "PR-E + later PRs" — Feature 125 PR-B deliberately shipped empty.
- **Write path near-empty.** Only **2 producers** write `ActivityEvent`: `PersonaService` (persona save/delete) and `EncryptionBackgroundService` (encryption complete/fail). Every other domain event (credentials, presentations, membership, security) writes to the **Inbox**, not Activity.

The Inbox works because it has the opposite: writers fired from many call sites + a UI that consumes them. The PWA Activity page (#5) has data only because it reads *different* stores, not `ActivityEvent`.

## 3. The Feature-125 components are abandoned

`TransactionHistoryFeed.razor` and `RecentActivityFeed.razor` (Feature 125) are parameter-driven (`[Parameter] Entries/Events`), render correctly when given data, and are:
- **Unused on web** — web `/app` Home shows a static "no recent activity" placeholder and doesn't even reference them.
- Used **only** by the PWA: `TransactionHistoryFeed` on `Activity.razor` (fed by presentation/verification), and `RecentActivityFeed` on `Index.razor` but **hardcoded empty**.

Their `Kind` enums (`Issuance, Presentation, Verification, Submission, Revocation`) describe a **credential-lifecycle** feed — a *third* vocabulary, distinct from both register `TransactionType` (10 values) and `ActivityEvent.EventType`.

## 4. "Encryption Operations" is NOT transaction history

`/operations` tracks **in-progress async encryption** during blueprint action execution (`Pending → ResolvingKeys → Encrypting → BuildingTransaction → Submitting → Complete/Failed`), stored in-memory with a 1-hour TTL. It is **live progress UX**, not an audit trail — if an op ages out or fails post-cleanup it's gone. It happens to also write one `ActivityEvent` on complete/fail, which is the right instinct (see recommendations).

## 5. Off-register transactions: the concept does not exist

No `off-register` / `off-ledger` / `IsOnChain` / `OnLedger` marker exists anywhere. Closest neighbours: `IsLocalRegisterAsync()` (about a *register*, not a transaction) and `TransactionMetaData.TrackingData` (an unused extensible dict). The register model assumes **every transaction is destined for the ledger**.

To represent wallet crypto operations as "transactions marked off-register" you'd need:
1. A **persistent** `WalletOperationLog` (replacing the 1-hour in-memory `EncryptionOperation` store) keyed by wallet + status, surviving failure/rejection.
2. An explicit **`LedgerStatus`** (e.g. `OffRegister | Pending | OnRegister`) or `IsOnChain: bool?` marker (on `WalletOperationLog`, or `TransactionMetaData` if you reuse the register model — not recommended).
3. New `TransactionType` values only if you choose to fold these into the register vocabulary (probably don't).

---

## Findings (ranked)

1. **The activity feed is structurally dead** — orphaned read path + near-empty write path. This is why the user never sees items. *(critical)*
2. **Two overlapping systems** (Inbox vs ActivityEvent) with no defined boundary — duplicated effort, neither covers the "passive audit feed" need. *(high)*
3. **Five vocabularies for "a thing that happened"** — register `TransactionType`, `WalletTransaction`, `EncryptionOperation` status, `ActivityEvent.EventType`, Feature-125 `Kind`. No canonical event taxonomy. *(high)*
4. **Web ↔ PWA wildly inconsistent** — PWA Activity is feature-complete (own sources); web has no activity surface; home feeds are stubs on both. Violates companion-first (shared component reading server state). *(high)*
5. **`WalletTransaction` history exists but is surfaced nowhere.** *(medium)*
6. **`EncryptionOperation` is ephemeral** — no durable record of wallet crypto ops. *(medium)*
7. **No off-register concept** — blocks the "wallet ops as off-register transactions" idea. *(medium — depends on direction)*

---

## Recommendations

**A. Pick one activity spine and finish it.** `ActivityEvent` (Tenant/Postgres, `/api/events`, `IActivityLogService`, `ActivityEventDto`) is the natural canonical **passive audit feed** — the table, endpoint, service, DTO and a UI client already exist. Finish both ends:
   - **Read:** wire `IActivityLogService` into one shared feed component on web Home + PWA Home/Activity.
   - **Write:** fan out from existing domain events. Cheapest path: where Inbox writers already fire, **also** emit an `ActivityEvent` (or have a single dispatcher write both). Define the boundary explicitly: **Inbox = actionable / needs attention; Activity = passive "what happened" timeline.**

**B. One shared feed component, companion-first.** Retire or repurpose the abandoned Feature-125 `TransactionHistoryFeed`/`RecentActivityFeed` into a single `ActivityFeed` reading `/api/events`, hosted identically on web and PWA. Settle on **one** event taxonomy (extend `ActivityEvent.EventType`).

**C. Keep Encryption Operations as live progress, but persist a record.** Leave `/operations` as the real-time popover, and on completion/failure emit a durable `ActivityEvent` (already partly done) so crypto ops appear in the unified timeline without conflating progress UX with history.

**D. If you want "off-register transactions":** model them as a **persistent `WalletOperationLog`** with a `LedgerStatus` marker, linking to the register `TxId` once (if) sealed — do **not** overload the register `TransactionModel`. Surface `WalletTransaction` + this log as the wallet's own "transactions" view, with on/off-register clearly marked.

**E. Sequence:** (1) wire ActivityEvent read path to a shared component [quick win — feeds stop being empty]; (2) add ActivityEvent writers across domains / bridge from inbox writers; (3) define Inbox-vs-Activity boundary; (4) optional: WalletOperationLog + off-register marker; (5) retire Feature-125 dual components.

---

## Decisions (2026-06-27, Stuart)

- **Spine = Inbox (Feature 118).** It is the only end-to-end-wired system; finish it rather than the orphaned ActivityEvent. Extend the inbox event with a **`Category: Actionable | Informational`** so one store serves both the **bell** (Actionable subset) and a full **Activity timeline** (all entries).
- **Capture the combined events of both methods.** No event that today reaches *either* the Inbox *or* `ActivityEvent` may be lost. The 2 legacy `ActivityEvent` producers (`PersonaService` save/delete, `EncryptionBackgroundService` complete/fail) must be rerouted to emit into the inbox spine.
- **Single shared, responsive control.** One `ActivityFeed` component that adapts to device view/layout, hosted **identically** on web `/app` and the PWA (companion-first). No per-host forks.
- **Encryption Operations (`/operations`) is out of scope** — it is live progress UX, a genuinely different concern; leave it as-is.
- **Tidy phase (separate, later run):** once the new spine is adopted, **drop the legacy `ActivityEvent` table + its indexes**, remove `IActivityLogService` and the abandoned Feature-125 `TransactionHistoryFeed`/`RecentActivityFeed` stubs, and **squash the schema change into the initial EF migrations** (pre-release convention). A **DB reset on n1 is acceptable**.

### Build sequencing
1. **Quick-win run (enqueue now):** add the shared responsive `ActivityFeed` (web + PWA) reading the inbox spine + show the combined timeline; add the `Actionable|Informational` category (bell = Actionable subset); reroute the 2 ActivityEvent producers into the spine. No deletions, no migration squash yet.
2. **Tidy run (after #1 merges):** drop legacy ActivityEvent table/indexes + `IActivityLogService` + Feature-125 stub components; squash into initial migrations; n1 DB reset.

## Relation to in-flight work

- prodexec run **C** (`8562c205e6d3`, PR #1056) bundled a `TransactionHistoryFeed`/`RecentActivityFeed` rewrite into a passkey-auth-state fix. **Decision: strip those out** — they belong to this larger, unresolved design, not a narrow auth fix.
