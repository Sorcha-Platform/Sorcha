# Quickstart & Validation: Fix Inbox/Bell Drawer Overflowing Phone Width

A run/validation guide proving the inbox/bell drawer fits the viewport on phones, stays a 420px side panel on larger screens, and never resizes any other drawer — in **both** hosts. Implementation details live in `tasks.md`; design rationale in `research.md`.

## Prerequisites

- .NET 10 SDK, Docker Desktop
- The change applied to the four touch points (see `plan.md` → Source Code):
  - PWA global stylesheet: `src/Apps/Sorcha.Wallet.Pwa/wwwroot/css/app.css`
  - Web host loaded stylesheet: `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/app/index.html` (inline `<style>` or a newly linked global css)
  - Dead rule removed from `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Inbox/InboxPanel.razor.css`
  - (Optional) `Class="inbox-drawer"` added to the `<MudDrawer>` in `InboxPanel.razor` if the class-scoped selector is chosen over `data-testid`

The global rule, scoped to the inbox drawer only:

```css
.mud-drawer[data-testid="inbox-drawer"] {
    width: min(420px, 100vw) !important;
    max-width: 100vw;
}
```

## Build

```bash
dotnet build src/Apps/Sorcha.UI/Sorcha.UI.sln
dotnet build src/Apps/Sorcha.Wallet.Pwa/Sorcha.Wallet.Pwa.csproj
```

## Run

```bash
# Full stack (web host bell drawer lives at /app, behind the gateway)
docker-compose up -d
# Web app:  http://localhost/app
# PWA:      serve/run the Sorcha.Wallet.Pwa host (see sorcha-app skill)
```

## Manual validation (fast local check)

For each host (PWA and web `/app`):

1. Open browser DevTools device toolbar; set viewport to **390 × 844** (phone).
2. Tap the **bell / Notifications** icon to open the inbox drawer.
3. **Expect (SC-001, FR-001, FR-004)**: drawer spans the full viewport width; header, category chips, and every entry title + timestamp are fully visible — nothing clipped off the left edge.
4. **Expect (SC-003)**: no horizontal page scrollbar appears.
5. Resize viewport to **1280 × 800** (desktop), reopen the drawer.
6. **Expect (SC-002, FR-003, FR-009)**: drawer is a fixed **420px** right-side panel, not full-width.
7. Open the host's **navigation** drawer (hamburger / mini drawer).
8. **Expect (FR-007, SC-005)**: the nav drawer's width is unchanged from before the fix.

Repeat at the boundary and extremes:
- **320 × 568** → drawer == 320px wide, no clipping (edge case).
- **exactly 420px wide** → drawer == 420px, no overflow / no scrollbar (edge case).
- Rotate 390↔844 (portrait↔landscape crossing 420px) and reopen → adapts: full-width when narrow, 420px when wide (edge case).

## Automated validation (Playwright E2E — regression guard)

Add an E2E (per the `playwright` + `sorcha-ui` skills, Docker test infra) that, for each host:

- Selects the drawer via `[data-testid="inbox-drawer"]`.
- For viewports `[320, 390, 420, 768, 1280]`:
  - opens the drawer,
  - asserts `drawerBox.width === Math.min(420, viewportWidth)` (within 1px rounding) — covers SC-001/SC-002,
  - asserts `drawerBox.x >= 0` (left edge on-screen) — covers FR-004,
  - asserts no horizontal overflow: `document.scrollingElement.scrollWidth <= innerWidth` — covers SC-003.
- Asserts the navigation drawer's width is unchanged when opened (a second `.mud-drawer` without the inbox testid) — covers FR-007/SC-005.

```bash
# Existing bUnit regression must stay green
dotnet test tests/Sorcha.UI.Core.Tests --filter "FullyQualifiedName~InboxPanel"
# Playwright E2E via Docker test infra (see sorcha-ui / playwright skills)
```

## Done when

- [ ] Manual checks pass in **both** hosts at all listed viewports.
- [ ] Playwright E2E asserts width == min(420, viewport) and no horizontal overflow across the viewport set, in both hosts (SC-001…SC-004).
- [ ] Navigation drawer width unchanged (SC-005).
- [ ] Dead `::deep .mud-drawer` rule removed from `InboxPanel.razor.css` (FR-008).
- [ ] `InboxPanelTests.cs` (bUnit) green.
