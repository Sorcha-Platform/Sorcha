# Contract: Shared `ActivityFeed` Component

The single shared timeline (FR-001 / SC-008). One implementation, two hosts.

**Location**: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Activity/ActivityFeed.razor`
**Hosts**: web `/app` (`Sorcha.UI.Web.Client/Components/Pages/Activity.razor`) and PWA (`Sorcha.Wallet.Pwa/Pages/Activity.razor`).
**Bundle**: must depend only on PWA-safe types (`IInboxApiService`, `ActivityClassification`, MudBlazor, shared `EmptyState`) — keep `scripts/check-pwa-bundle.ps1` green.

## Inputs (parameters)

| Parameter | Type | Default | Purpose |
|-----------|------|---------|---------|
| `PageSize` | int | 20 | Entries per incremental load (clamped 1..100). |

(No identity parameter — scope comes from the authenticated `/api/me/inbox` call.)

## Behaviour

1. **Load**: on init, `IInboxApiService.ListAsync(page:1, pageSize:PageSize, actionableOnly:false)` → render newest-first (FR-001, FR-004).
2. **Entry render** (FR-003): title, summary, relative timestamp, a category/severity indicator, and an **Actionable/Informational** affordance derived via `ActivityClassification`. If `DetailHref` is present → navigable; else non-navigable (no dead click — edge case).
3. **Paging** (FR-006): "Load more" requests the next page; show it only while `loadedCount < TotalCount`; never silently truncate.
4. **Empty state** (FR-007): when `TotalCount == 0`, render the shared `EmptyState` with a friendly message.
5. **Responsive** (FR-005 / SC-006): legible at mobile and desktop widths; no horizontal scroll / no truncation of essential info.
6. **Live arrival** (edge case): subscribe to `TenantHubConnection.OnInboxEntryAdded`; on signal, refresh page 1 (consistent with `InboxPanel`). No manual full-page reload required.
7. **No Snackbar** (Pattern #12) — own-action feedback (if any) via `IInlineFeedback` only.

## Outputs

Read-only surface — no events emitted to parents beyond standard navigation.

## Acceptance hooks (map to spec)

- US1 / SC-001 / SC-008: identical entries & component on both hosts → confirmed by reusing the one component + a cross-host Playwright check.
- US2 / SC-002: this surface shows **all** entries (contrast with bell's Actionable-only).
- Edge cases (empty, no-detail, large history, live arrival) handled per items 3–6.
