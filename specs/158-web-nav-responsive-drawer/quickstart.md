# Quickstart & Validation: Web Nav Drawer — Responsive (no mini rail)

A run/validation guide proving the feature end-to-end. Behaviour details live in
[contracts/drawer-behavior.md](./contracts/drawer-behavior.md); decisions in
[research.md](./research.md). Implementation steps land in `tasks.md` (via `/speckit-tasks`).

## Prerequisites

- .NET 10 SDK, Docker Desktop
- Repo built: `dotnet restore && dotnet build`
- The change is a single edit in
  `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` (drawer `Variant`
  `Mini` → `Responsive`; drop `OpenMiniOnHover`).

## Run the app

```bash
# Full stack (web UI at http://localhost/app)
docker-compose up -d

# OR Aspire (HTTPS dev ports, breakpoints)
dotnet run --project src/Apps/Sorcha.AppHost
```

Sign in so the authenticated nav menu renders.

## Manual validation

### Scenario A — Desktop reclaim (US1 / FR-001, FR-002, SC-001)
1. Open the app at a desktop-width window, signed in, drawer open.
2. Click the app-bar menu toggle. **Expect**: the entire navigation strip disappears — *no icon
   rail remains* — and page content widens to fill the freed area.
3. Click the toggle again. **Expect**: the full drawer (icons + labels) returns and content is
   pushed aside without overlap.
4. Navigate between pages with the drawer open. **Expect**: it stays open (state persists in
   session).

### Scenario B — Phone overlay (US2 / FR-003, FR-007, SC-002)
1. Resize to a phone-width viewport (or use device emulation) and reload signed in.
2. **Expect**: drawer is closed on first render; content uses full screen width.
3. Open the drawer. **Expect**: it overlays the content (content does not reflow) with a
   scrim/backdrop behind it.
4. Select a nav destination (or tap the scrim). **Expect**: the drawer closes and the destination
   renders at full width.

### Scenario C — Breakpoint crossing (Edge Cases / FR-008, SC-005)
1. With the drawer open, resize from desktop width down to phone width and back.
2. **Expect**: the drawer switches push ⇄ overlay appropriately with no clipped, overlapping, or
   orphaned rail at any width.

### Scenario D — Contents intact (FR-006, SC-004)
1. As an Administrator/Designer, open the drawer. **Expect**: all role-gated sections, dividers,
   and badges (pending credentials, inbox bell) are present and reach the same routes as before.
2. Sign out. **Expect**: the minimal signed-out menu still releases space when closed.

## Automated validation (E2E)

Refresh/extend the existing Playwright Docker suite:

- `tests/Sorcha.UI.E2E.Tests/Docker/NavigationTests.cs` — assert closed-releases-space on desktop
  (no mini-rail element; content width increases) and phone closed-by-default + overlay-on-open.
- `tests/Sorcha.UI.E2E.Tests/PageObjects/NavigationComponent.cs` — update the drawer locator /
  open-detection if it keyed on the mini rail; reuse `ToggleDrawerAsync()` / `IsDrawerOpenAsync()`.

Run the UI E2E tests (Docker-backed):

```bash
dotnet test tests/Sorcha.UI.E2E.Tests --filter "FullyQualifiedName~Navigation"
```

**Expect**: drawer toggle, visibility, overlay, and nav-contents tests pass.

## Done when

- [ ] Closed desktop drawer leaves **zero** navigation footprint (no mini rail).
- [ ] Phone viewport: drawer closed on first render; overlays with scrim when opened; closes on
      nav-item select.
- [ ] All pre-existing nav destinations / role sections / badges present and routable.
- [ ] No clipping/overlap/orphaned-rail when toggling or crossing the breakpoint.
- [ ] `dotnet build` clean (no new warnings); Playwright navigation tests green.
