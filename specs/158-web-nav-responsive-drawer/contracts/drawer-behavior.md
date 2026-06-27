# UI Contract: Navigation Drawer Behaviour (web host)

This is the externally observable behaviour contract for the `MainLayout` navigation drawer after
switching to `DrawerVariant.Responsive`. It is the source of truth the E2E tests assert against. It
defines *behaviour*, not implementation. Component: `Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor`.

## Controls & states

- **Single control**: the app-bar menu button (`Icons.Material.Filled.Menu`, `OnClick=ToggleDrawer`,
  `aria-label="Toggle navigation menu"`). It is the *only* open/close affordance and MUST be visible
  and clickable in every state (FR-004). There is no hover-to-peek affordance (the mini rail is gone).
- **State**: `open` | `closed`. Defaults — desktop: `open`; phone: `closed` (FR-005).

## Behaviour matrix (states × viewport)

| Viewport | Drawer state | Required observable behaviour | FR / SC |
|----------|--------------|-------------------------------|---------|
| Desktop (≥ breakpoint) | open | Full drawer (icons + labels) visible; main content **pushed** aside, no overlap. | FR-002, SC-001 |
| Desktop | closed | **Zero** navigation footprint — no icon rail, no residual strip; content expands to fill reclaimed width. | FR-001, FR-002, SC-001 |
| Phone (< breakpoint) | closed (default on first load) | Drawer hidden; content uses full screen width. | FR-003, SC-002 |
| Phone | open | Drawer **overlays** content (content does not reflow); a scrim/backdrop covers the rest of the screen. | FR-003, SC-003 |

## Interaction contract

| # | Given | When | Then |
|---|-------|------|------|
| C1 | Desktop, drawer open | menu toggle activated | Drawer fully hidden (no mini rail); content widens. |
| C2 | Desktop, drawer closed | menu toggle activated | Full drawer appears; content pushed aside without overlap. |
| C3 | Desktop, drawer open | navigate between pages | Drawer stays open; open/closed choice persists across in-session navigation. |
| C4 | Phone, fresh load | page renders | Drawer is closed; content full width. |
| C5 | Phone, drawer closed | menu toggle activated | Drawer opens as overlay above content; scrim visible. |
| C6 | Phone, drawer open (overlay) | a nav destination is selected, OR the scrim/outside is tapped | Drawer closes; destination renders at full width. |
| C7 | Any width, drawer open | viewport resized across the desktop/phone breakpoint | Drawer adopts the new width's behaviour (push ⇄ overlay) with no clipped, overlapping, or orphaned artefacts. |

## Invariants (must hold in all states)

- **INV-1 (contents unchanged)**: every navigation destination, role-gated section
  (`Administrator`/`SystemAdmin`/`Designer`/`Auditor`), badge (pending credentials, inbox), and
  section divider present before the change remains present, in the same order, with the same
  routing. This change is spatial only. (FR-006, SC-004)
- **INV-2 (toggle always reachable)**: the app-bar menu toggle is never hidden or disabled by drawer
  state or viewport. (FR-004)
- **INV-3 (smooth, artefact-free)**: open/close and breakpoint-crossing transitions show no
  clipping, overlap, or orphaned rail. (FR-008, SC-005)
- **INV-4 (signed-out parity)**: a signed-out session's minimal menu (e.g. Sign in) obeys the same
  closed-releases-space behaviour. (Edge Cases)
- **INV-5 (independent scroll)**: a long open drawer (admin/designer roles) scrolls independently;
  the content area is unaffected by the drawer's internal scroll. (Edge Cases)

## Out of scope (non-contract)

- Wallet PWA layout (`Sorcha.Wallet.Pwa/MainLayout.razor`, bottom `FloatingTabBar`) and any Verifier
  layout — unchanged.
- Drawer contents, routing, auth/authz — unchanged.
- Cross-session (persisted) drawer state — only in-session retention is contracted.
