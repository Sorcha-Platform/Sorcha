# Feature Specification: Fix Inbox/Bell Drawer Overflowing Phone Width

**Feature Branch**: `162-fix-inbox-drawer-overflow`

**Created**: 2026-06-25

**Status**: Draft

**Input**: User description: "Fix inbox/bell drawer overflowing phone width: move the .mud-drawer width cap (min(420px,100vw)) from component-scoped InboxPanel.razor.css to a global stylesheet (Sorcha.Wallet.Pwa wwwroot/css/app.css and the web host app.css) so Blazor CSS isolation does not strip it from MudBlazors out-of-tree drawer. Per docs/superpowers/specs/2026-06-25-pwa-mobile-feedback-backlog.md item 2"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Citizen opens the inbox/bell drawer on a phone (Priority: P1)

A citizen using the Sorcha Wallet PWA on a phone (≈360–430px wide) taps the bell icon to open the notification inbox. The drawer slides in and fits entirely within the visible screen: the header, category chips, and every entry's title and timestamp are fully readable, with nothing clipped off the left edge.

**Why this priority**: This is the entire purpose of the fix. Today the drawer is rendered wider than the viewport, pushing its left portion off-screen and clipping primary content, which makes the inbox effectively unusable on a phone. Restoring readability on the most common mobile width is the core deliverable and the MVP.

**Independent Test**: Open the PWA on a phone-sized viewport, open the inbox drawer, and confirm the drawer width never exceeds the screen width and no content is clipped at the edges. Fully demonstrable on its own.

**Acceptance Scenarios**:

1. **Given** a phone viewport narrower than 420px, **When** the citizen opens the inbox/bell drawer, **Then** the drawer occupies the full viewport width and no header, chip, title, or timestamp content is clipped off-screen.
2. **Given** the inbox drawer is open on a phone, **When** the citizen reads any inbox entry, **Then** the entry's title and timestamp are both fully visible without horizontal scrolling.

---

### User Story 2 - User opens the inbox/bell drawer on tablet or desktop (Priority: P2)

A user on a tablet or desktop (viewport wider than 420px) opens the inbox drawer and sees it presented as a fixed-width side panel (the established 420px), not a full-width sheet, preserving the existing larger-screen layout.

**Why this priority**: The fix must not regress the working tablet/desktop presentation. It is second priority because larger screens already render acceptably today; the change only needs to preserve that behaviour while fixing phones.

**Independent Test**: Open the inbox drawer on a viewport wider than 420px and confirm the drawer is capped at the side-panel width rather than stretching full-width.

**Acceptance Scenarios**:

1. **Given** a viewport wider than 420px, **When** the user opens the inbox/bell drawer, **Then** the drawer renders at the fixed side-panel width and does not stretch to the full viewport.

---

### User Story 3 - Inbox drawer behaves consistently across both UI hosts (Priority: P3)

The inbox/bell drawer behaves identically whether the user is in the Sorcha Wallet PWA or in the web host that surfaces the same shared inbox component.

**Why this priority**: The inbox component is shared between the PWA and the web host. The fix must apply in both hosts so behaviour does not diverge. It is third priority because correctness on the phone (P1) is the primary concern; consistency across hosts is the completeness guarantee.

**Independent Test**: Open the inbox drawer in each host at the same phone-sized viewport and confirm both apply the same width cap.

**Acceptance Scenarios**:

1. **Given** the same phone viewport, **When** the drawer is opened in the PWA and separately in the web host, **Then** both apply the identical width cap and clipping behaviour.

---

### Edge Cases

- What happens at exactly 420px viewport width? The drawer width should equal the viewport width with no overflow and no horizontal scrollbar.
- What happens on a very narrow viewport (e.g. 320px)? The drawer must still fill exactly the viewport width with no content clipped.
- What happens when the device is rotated from portrait to landscape (viewport crosses the 420px threshold)? The drawer should adapt to the new viewport on next open, capping at the side-panel width when wide enough and going full-width when narrower.
- What happens to other MudBlazor drawers in the application? The global rule must be scoped so it only governs the inbox/bell drawer and does not unintentionally resize unrelated drawers or navigation panels.
- What happens if the previous component-scoped rule is left in place? The redundant scoped rule (which never applies at runtime) should be removed so the styling has a single source of truth and does not mislead future maintainers.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The inbox/bell drawer MUST never render wider than the device viewport on any screen size.
- **FR-002**: On viewports narrower than the side-panel width (420px), the inbox/bell drawer MUST occupy the full viewport width as a full-width sheet.
- **FR-003**: On viewports at or wider than the side-panel width, the inbox/bell drawer MUST be capped at the established 420px side-panel width.
- **FR-004**: All inbox drawer content (header, category chips, entry titles, entry timestamps) MUST be fully visible and unclipped at all supported viewport widths.
- **FR-005**: The width cap MUST take effect at runtime against the actual rendered drawer element, regardless of where that element is mounted in the document relative to the inbox component.
- **FR-006**: The width cap MUST apply in both the Sorcha Wallet PWA and the web host that render the shared inbox component.
- **FR-007**: The width cap MUST be scoped so it governs only the inbox/bell drawer and does not alter the size or behaviour of other drawers or panels in the application.
- **FR-008**: The now-ineffective component-scoped width rule MUST be removed (or relocated) so that drawer-width styling has a single authoritative source and no dead/misleading rule remains.
- **FR-009**: The fix MUST NOT regress the existing tablet/desktop side-panel presentation.

### Key Entities *(include if feature involves data)*

Not applicable — this is a presentation-layer styling fix with no data model impact.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On every supported phone viewport (320px–430px), the opened inbox/bell drawer width equals the viewport width and 0 pixels of drawer content are clipped off-screen.
- **SC-002**: On viewports wider than 420px, the opened inbox/bell drawer renders at the 420px side-panel width (within rounding), matching the pre-existing larger-screen layout.
- **SC-003**: Opening the inbox drawer produces no horizontal page scrollbar at any supported viewport width.
- **SC-004**: The corrected behaviour is observable in 100% of the UI hosts that surface the inbox component (PWA and web host).
- **SC-005**: No drawer or panel other than the inbox/bell drawer changes size or position as a result of the fix.

## Assumptions

- The referenced backlog document `docs/superpowers/specs/2026-06-25-pwa-mobile-feedback-backlog.md` (item 2) is the source of this work item; the document is not yet present in the repository, so this spec is derived directly from the user-provided description and the current state of the inbox styling.
- The desired width behaviour is the existing intent already captured in the component stylesheet: a 420px side panel on tablet/desktop and a full-width sheet on phones. This fix changes *where* that rule lives so it actually applies at runtime, not the rule's values.
- The root cause is that the drawer element is rendered outside the inbox component's DOM subtree (an out-of-tree overlay/portal), so the component's CSS-isolation attribute is never stamped onto it and the scoped rule is silently dropped. Moving the rule to a non-isolated, global stylesheet is the corrective approach.
- The inbox component is shared between the PWA and the web host, and both hosts load their own global application stylesheet, so the rule must be added to each host's global stylesheet to cover both.
- "420px" and "min(420px, 100vw)" are the established target values and are retained unchanged; only their stylesheet location changes.
- No change to inbox behaviour, content, data, or notification semantics is in scope — this is strictly a layout/width correction.
