# Phase 1 Data Model: Web Nav Drawer — Responsive (no mini rail)

**Not applicable — no data entities.**

This feature is a presentation/layout change to a single Blazor layout component. It introduces no
persisted data, no DTOs, no domain aggregates, and no API payloads (confirmed by spec §"Key
Entities": *"Not applicable — this is a presentation/layout change with no new data entities."*).

The only stateful element is transient UI component state, documented here for completeness:

| Element | Type | Owner | Lifetime | Notes |
|---------|------|-------|----------|-------|
| `_drawerOpen` | `bool` (default `true`) | `MainLayout.razor` `@code` | In-memory, per session; survives in-app navigation via the persistent layout | Bound to `MudDrawer` via `@bind-Open`. Toggled by `ToggleDrawer()` from the app-bar menu button. Under `DrawerVariant.Responsive`, phone "closed by default" is derived from viewport width by MudBlazor, not from this field. No persistence (FR-005 requires only in-session retention). |

No validation rules, relationships, or state-transition tables apply beyond the binary
open/closed toggle described in [contracts/drawer-behavior.md](./contracts/drawer-behavior.md).
