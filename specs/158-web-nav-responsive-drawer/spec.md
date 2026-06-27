# Feature Specification: Web Nav Drawer — Responsive (no mini rail)

**Feature Branch**: `158-web-nav-responsive-drawer`

**Created**: 2026-06-25

**Status**: Draft

**Input**: User description: "Web nav drawer: replace Mini variant with Responsive in MainLayout.razor so closed state releases space (no mini rail); desktop push, phone overlay closed-by-default. Per docs/superpowers/specs/2026-06-25-pwa-nav-and-present-camera-design.md item 1"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reclaim full content width on desktop when nav is closed (Priority: P1)

A signed-in user on a desktop browser is reading or working with content (a register table, a blueprint, a form). They close the navigation drawer to give the content more room. Today the drawer collapses to a narrow icon-only "mini" rail that still occupies a strip down the left edge, so the content never gets the full window width. With this change, closing the drawer removes the rail entirely and the main content expands to fill the reclaimed space.

**Why this priority**: This is the core intent of the feature — the residual mini rail is the specific defect being removed. Delivering only this story already gives users the promised benefit (more usable horizontal space) and is independently demonstrable.

**Independent Test**: On a desktop-width viewport, open the app while signed in, toggle the drawer closed via the menu button, and confirm the navigation strip disappears completely and the page content widens to occupy the freed area. Toggle it open again and confirm the content shifts back to make room for the full drawer.

**Acceptance Scenarios**:

1. **Given** a signed-in user on a desktop-width viewport with the drawer open, **When** they activate the menu toggle to close the drawer, **Then** the navigation area is fully hidden (no icon rail remains) and the main content expands to use the reclaimed width.
2. **Given** a signed-in user on a desktop-width viewport with the drawer closed, **When** they activate the menu toggle to open the drawer, **Then** the full drawer (icons and labels) appears and the main content is pushed aside to make room for it without overlapping.
3. **Given** the drawer is open on desktop, **When** the user navigates between pages, **Then** the drawer remains open and the chosen open/closed state persists across navigation within the session.

---

### User Story 2 - Unobstructed reading on phones with the drawer out of the way by default (Priority: P2)

A signed-in user on a phone-width viewport lands on the app. The navigation is hidden by default so the small screen is dedicated to content. When they need to navigate, they open the drawer and it appears as an overlay on top of the content; selecting a destination (or dismissing) closes it again so the full screen returns to content.

**Why this priority**: Phone behaviour is a distinct, valuable slice but secondary to fixing the desktop rail. It can be built and verified independently and ensures the new variant degrades correctly on small screens.

**Independent Test**: On a phone-width viewport, load the app signed in and confirm the drawer is closed by default with content using the full width. Open the drawer, confirm it overlays the content rather than pushing it, then select a nav item and confirm the drawer closes.

**Acceptance Scenarios**:

1. **Given** a signed-in user first loads the app on a phone-width viewport, **When** the page renders, **Then** the navigation drawer is closed and the content occupies the full screen width.
2. **Given** the drawer is closed on a phone-width viewport, **When** the user opens it, **Then** the drawer appears as an overlay above the content (the content does not reflow) and a scrim/backdrop covers the rest of the screen.
3. **Given** the drawer is open as an overlay on a phone-width viewport, **When** the user selects a navigation destination or taps outside the drawer, **Then** the drawer closes and the content is fully visible again.

---

### Edge Cases

- **Viewport resize across the breakpoint**: When the window is resized from desktop width to phone width (or vice versa) while the drawer is open, the drawer adopts the behaviour appropriate to the new width (push vs. overlay) without leaving the content in an inconsistent or clipped state.
- **Signed-out users**: The same drawer hosts a minimal signed-out menu (e.g. Sign in). Closed state must still release space and behave consistently for signed-out sessions.
- **Long nav lists / scrolling**: With many sections visible (admin / designer roles), the open drawer remains independently scrollable and the content area is unaffected by the drawer's internal scroll.
- **App bar toggle availability**: The menu toggle in the top app bar must remain visible and functional in every state (open and closed, desktop and phone) so a closed drawer can always be reopened — there is no longer a persistent rail to click.
- **No hover-expand expectation**: With the rail removed, the previous "hover the mini rail to peek the menu" affordance no longer exists; opening must be an explicit toggle action.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When the navigation drawer is closed, the system MUST release all horizontal space the navigation previously occupied — no icon-only rail or residual strip may remain visible.
- **FR-002**: On desktop-width viewports, an open drawer MUST push the main content aside (content and drawer coexist without overlap), and a closed drawer MUST allow the content to expand into the reclaimed width.
- **FR-003**: On phone-width viewports, the drawer MUST default to closed on initial load and MUST present as an overlay above the content (with a dismissable backdrop) when opened, rather than pushing the content.
- **FR-004**: The app-bar menu toggle MUST open and close the drawer from any state, and MUST remain reachable in the closed state so users can always reopen navigation.
- **FR-005**: The drawer's open/closed state MUST persist across in-session page navigation, retaining the desktop default of open and the phone default of closed.
- **FR-006**: All existing navigation destinations, role-gated sections, badges, and section dividers MUST remain present and functional; this change affects only the drawer's open/closed spatial behaviour, not its contents or routing.
- **FR-007**: Selecting a navigation destination from an overlay drawer on a phone-width viewport MUST dismiss the drawer so the destination renders at full width.
- **FR-008**: The transition between open and closed states MUST be visually smooth and leave no clipped, overlapping, or orphaned navigation artefacts at any viewport width, including when the viewport crosses the desktop/phone breakpoint.

### Key Entities

*Not applicable — this is a presentation/layout change with no new data entities.*

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a desktop-width viewport, closing the drawer increases the usable content width by the drawer's full footprint, with zero pixels of residual navigation rail remaining.
- **SC-002**: On a phone-width viewport, the drawer is closed on first render in 100% of fresh loads, leaving the entire screen width to content.
- **SC-003**: Users can open and close navigation from every state at any supported viewport width using a single toggle action, with no state in which navigation becomes unreachable.
- **SC-004**: 100% of navigation destinations and role-gated sections available before the change remain available and reachable after it (no regressions in menu contents or routing).
- **SC-005**: No visual defects (clipping, overlap, orphaned rail) are observable when toggling the drawer or resizing across the desktop/phone breakpoint.

## Assumptions

- The referenced design note `docs/superpowers/specs/2026-06-25-pwa-nav-and-present-camera-design.md` (item 1) was not present in the repository at spec time; this specification is derived directly from the user description, which is self-contained. If that design note is later added and conflicts, the design note governs.
- Scope is limited to the **web** host's primary layout (the `Sorcha.UI.Web.Client` `MainLayout`). The Wallet PWA and Verifier layouts are out of scope for this change unless explicitly included in a later item.
- "Desktop" vs "phone" behaviour follows the existing responsive breakpoint already used by the layout framework; no new custom breakpoint is introduced.
- The default open/closed states match the conventional responsive pattern: open by default on desktop, closed by default on phone.
- The existing app-bar menu toggle remains the single control for opening/closing the drawer; the prior hover-to-peek behaviour of the mini rail is intentionally dropped along with the rail.
- The change is purely presentational; no authentication, authorisation, routing, or data-access behaviour changes.
