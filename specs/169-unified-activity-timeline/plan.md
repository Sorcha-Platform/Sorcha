# Implementation Plan: Unified Activity Timeline Read-Path

**Branch**: `169-unified-activity-timeline` | **Date**: 2026-06-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/169-unified-activity-timeline/spec.md`

## Summary

Deliver a single, shared, responsive **Activity timeline** on both the web `/app` host and the Citizen Wallet PWA that reads the existing **Inbox spine** (Feature 118/151 — `InboxEntry` in the Tenant Service, surfaced via `/api/me/inbox`). Introduce a high-level **Actionable vs Informational** classification, *derived at read time* from the entry's existing `InboxCategory` + `InboxSeverity` (no destructive schema change), so the **bell** shows only the Actionable subset (and its unread badge counts only unread Actionable entries) while the **Activity timeline** shows everything. Reroute the two surviving legacy `ActivityEvent` producers — `PersonaService` (profile save/delete) and `EncryptionBackgroundService` (encryption complete/fail) — to *also* emit into the Inbox spine via best-effort inbox writers so no history is lost. The legacy `ActivityEvent` table and migrations are retained unchanged, and the `/operations` Encryption Operations page is untouched.

**Technical approach (one line):** add a pure `Actionable` derivation + an `actionableOnly` read filter in the Tenant Service inbox read-path, build one shared `ActivityFeed` Blazor component in `Sorcha.UI.Components.User` consumed by both hosts, point the bell at the Actionable subset, and add two best-effort inbox writers that mirror the legacy producers into the spine.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: .NET Aspire 13+, Blazor (web `/app` + Wallet PWA), MudBlazor, SignalR (TenantHub), EF Core (Postgres) for the inbox store, `Sorcha.ServiceClients.Http` (`IPlatformInboxClient`)

**Storage**: PostgreSQL via `IInboxStore` → `EfCoreInboxStore` (the Inbox spine, Tenant Service). **No new tables, no migrations** — classification is derived, not persisted. Legacy `ActivityEvent` table retained unchanged.

**Testing**: xUnit + FluentAssertions + Moq for service/store/writer unit tests; bUnit / component tests for the shared `ActivityFeed`; Playwright (Docker infra) for responsive E2E on both hosts.

**Target Platform**: Linux server (services), Blazor web (`/app`) and Blazor WASM PWA (`/wallet`).

**Project Type**: Multi-service web platform (microservices + two Blazor hosts sharing a component library).

**Performance Goals**: Read-path parity with the existing bell drawer — inbox page query already indexed on `(PlatformUserId, OccurredAt)`; Actionable filter adds a translatable predicate on existing `Category`/`Severity` columns (no new index strictly required for the quick win; see research). Incremental paging keeps the timeline responsive for long histories.

**Constraints**:
- No destructive change to `InboxEntry` schema (FR-012); classification is derived.
- Best-effort producer writes — never roll back the underlying operation (FR-015).
- Idempotency preserved via the existing `(PlatformUserId, SourceEventId)` unique constraint (FR-018).
- Single shared component (FR-001 / SC-008) — no per-host duplication; must stay within `Sorcha.UI.Components.User` so the PWA bundle gate (`check-pwa-bundle.ps1`) passes.
- No Snackbar in new UI (Pattern #12); empty state via existing `EmptyState` component.

**Scale/Scope**: Per-user read scope (existing `/api/me/inbox`). Two producers rerouted. One shared component + one new web `/app` route; the PWA `/activity` page is re-pointed to the shared component. Out of scope: historical legacy-row migration, dropping the legacy table, `/operations`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | ✅ PASS | Changes are local to Tenant Service (inbox read filter, persona writer), Blueprint Service (encryption writer using existing `IPlatformInboxClient`), and the UI layer. No new upward dependencies. |
| II. Security First | ✅ PASS | Read path stays on the per-user `/api/me/inbox` scope (caller's `platform_user_id` claim). No new external boundary; reroute writes use the existing authenticated internal inbox channel. |
| III. API Documentation | ✅ PASS (action) | The two read endpoints gain an `actionableOnly` filter / changed unread-count semantics — XML docs + `.WithSummary()`/`.WithDescription()` updated; OpenAPI stays current. |
| IV. Testing (>85% new code) | ✅ PASS (action) | Unit tests for the classification mapping, the `actionableOnly` store predicate, both reroute writers (incl. fault-injection parity), component tests for `ActivityFeed`, Playwright responsive checks. |
| V. Code Quality | ✅ PASS | Nullable on, async I/O, DI, no new warnings; matches existing inbox-writer idiom. |
| VI. Blueprint Standards | ✅ N/A | No blueprint changes. |
| VII. Domain-Driven Design | ✅ PASS | Reuses spine ubiquitous terms (Inbox entry, Category, Severity); adds the read-time "Actionable / Informational" grouping as a derived view concept, not a new aggregate. |
| VIII. Observability | ✅ PASS | Best-effort writes use structured `LogWarning`/`LogError` on failure (no string interpolation); reuses existing TenantHub live-update telemetry. |

**Result: PASS — no violations. Complexity Tracking left empty.**

## Project Structure

### Documentation (this feature)

```text
specs/169-unified-activity-timeline/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output — decisions resolved
├── data-model.md        # Phase 1 output — entities & classification view
├── quickstart.md        # Phase 1 output — validation guide
├── contracts/           # Phase 1 output — read-API + component + classification contracts
│   ├── inbox-read-api.md
│   ├── classification-mapping.md
│   └── activity-feed-component.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root — real paths)

```text
src/Services/Sorcha.Tenant.Service/
├── Models/InboxEntry.cs                         # existing — InboxCategory, InboxSeverity, ChannelHints (unchanged)
├── Models/ActivityEvent.cs                      # legacy — RETAINED unchanged
├── Storage/IInboxStore.cs                       # + actionableOnly param on GetPageAsync / GetUnreadCountAsync
├── Storage/EfCoreInboxStore.cs                  # + Actionable predicate (translatable to SQL)
├── Services/InboxClassification.cs              # NEW — canonical pure Actionable derivation (server side)
├── Services/InboxService.cs                     # pass-through actionableOnly; unread count = Actionable-only
├── Services/PersonaInboxWriter.cs               # NEW — best-effort writer for profile save/delete
├── Services/PersonaService.cs                   # reroute: also emit via PersonaInboxWriter (keep legacy emit)
└── Endpoints/MeInboxEndpoints.cs                # + actionableOnly query filter; doc updates

src/Services/Sorcha.Blueprint.Service/
├── Services/Implementation/EncryptionInboxWriter.cs   # NEW — best-effort writer (complete/fail) via IPlatformInboxClient
└── Services/Implementation/EncryptionBackgroundService.cs  # reroute: also emit via EncryptionInboxWriter (keep legacy emit)

src/Apps/Sorcha.UI/Sorcha.UI.Components.User/
├── Components/Activity/ActivityFeed.razor       # NEW — the single shared timeline (used by both hosts)
├── Services/Shared/ActivityClassification.cs    # NEW — client-side mirror of the derivation (for emphasis)
├── Components/Inbox/InboxPanel.razor            # bell: request Actionable subset only
└── Services/User/IInboxApiService.cs            # + actionableOnly param on ListAsync

src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/
├── Components/Pages/Activity.razor              # NEW — /app route hosting <ActivityFeed/>
└── Components/Layout/MainLayout.razor           # bell badge already Actionable-scoped via server count

src/Apps/Sorcha.Wallet.Pwa/
└── Pages/Activity.razor                         # re-point /activity to <ActivityFeed/> (shared)
```

**Structure Decision**: Multi-service web platform. Reuse-first — the Inbox spine, the internal inbox write channel, the inbox-writer idiom, and the TenantHub live-update pipeline already exist; this feature is overwhelmingly *additive read-path + two thin writers*. The single shared component lives in `Sorcha.UI.Components.User` (the lowest user-facing library both hosts reference) to satisfy SC-008 and stay inside the PWA bundle gate.

## Complexity Tracking

> No constitution violations — section intentionally empty.
