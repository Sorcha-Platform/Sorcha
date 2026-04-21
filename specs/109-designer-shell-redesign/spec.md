# Feature Specification: AI Designer Unified Shell

**Feature Branch**: `109-designer-shell-redesign`
**Created**: 2026-04-21
**Status**: Draft
**Input**: User description: "AI Designer Layout Redesign — Unified Shell With AI / Diagram / Preview Tabs. Consolidate the two blueprint designer pages into a single shell with three tabs (AI Designer, Diagram, Form Preview) sharing live state. Fix chat layout (full-width, input pinned to bottom). Add a new Form Preview tab that auto-follows AI activity with manual override controls."

**Authoritative design**: A companion engineering design document covering architecture, testing strategy, and non-goals lives at `docs/superpowers/specs/2026-04-21-ai-designer-layout-redesign-design.md`. This specification captures the business-facing behaviour; the design doc captures the implementation approach.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Unified designer shell with fixed chat layout (Priority: P1)

A blueprint designer opens the designer from the left-nav and lands on a single page whose whole viewport is given over to designing one blueprint. The AI chat occupies the full available width. The input box is pinned to the bottom of the viewport and stays visible regardless of how long the conversation gets; only the message history scrolls. A tab strip at the top of the page exposes **AI Designer** and **Diagram** views as peers. Switching between the two tabs preserves the chat session, the blueprint being worked on, the validation badge, and the diagram's camera position and selection. A single shared toolbar at the top of the page carries Save, Load, Export, and the validation summary — these work identically no matter which tab is currently active.

**Why this priority**: This is the core daily-use pain. The current split-pane chat is cramped on both sides, the input drifts out of view after a moderate number of messages, and switching to the diagram view today means a full page navigation that re-hydrates state. Fixing these alone dramatically improves the workflow — before the Form Preview tab even exists. Shipping just this story is already a material upgrade.

**Independent Test**: Open the designer, describe a workflow to the AI, send 40+ messages, verify the input stays visible throughout and messages scroll independently. Switch to the Diagram tab; verify the blueprint the AI built is visible as a graph. Switch back to AI; verify the conversation is still there with live connection. Click Save from the Diagram tab and verify the blueprint persists.

**Acceptance Scenarios**:

1. **Given** a designer opens the designer with no blueprint, **When** they type a description and the AI responds, **Then** the AI chat fills the full width of the content area and the input control remains pinned to the bottom of the viewport.
2. **Given** a conversation has grown to 50+ messages, **When** the designer scrolls up to review history, **Then** the input control remains visible at the bottom and does not scroll with the messages.
3. **Given** the designer has built a blueprint via AI, **When** they click the Diagram tab, **Then** the blueprint renders as a graph with all participants, actions, and routes as they were defined, and the blueprint title shows in the shared toolbar.
4. **Given** the designer has edited a blueprint in both tabs, **When** they click Save from either tab, **Then** the full current blueprint persists and a "saved" confirmation appears.
5. **Given** a designer hand-edits an action in the Diagram tab, **When** they switch back to AI and ask a follow-up question, **Then** the AI's response takes the hand-edit into account (visible through the AI correctly referring to the new value).
6. **Given** the designer is on the AI tab with an unsaved blueprint, **When** they attempt to navigate away via the browser back button, **Then** a confirm-discard prompt appears.

---

### User Story 2 — Form Preview tab with auto-cursor (Priority: P2)

The tab strip exposes a third tab — **Form Preview** — alongside AI Designer and Diagram. Preview renders one action of the blueprint at a time, exactly as the participant of that action will see it when they submit in production. The rendered form includes all fields, sections, rules, file inputs, review pages, and persona autofills — whatever the real citizen experience will contain. The form is interactable for exploration (fields can be focused, sections can be expanded, conditional logic fires) but the submit button is present-but-disabled with a "Preview — submission disabled" hint so the designer sees the final visual layout truthfully.

By default, the preview cursor auto-follows AI activity: whenever the AI creates or modifies an action via a tool call, Preview jumps to show that action. A pager above the form ("Previous · Action N of M · Next", plus a jump dropdown listing all actions by title + participant, plus keyboard shortcuts `[` / `]`) lets the designer take manual control at any moment. Once the designer clicks any pager control, the cursor becomes manual-override — subsequent AI edits no longer yank the view away. A prominent "Follow AI" toggle resumes auto-cursor on demand, snapping Preview to whatever action the AI most recently edited.

**Why this priority**: The feedback loop "I just asked the AI for X, can I see what X looks like?" is the highest-value capability this redesign adds. It closes the gap between editing a blueprint and knowing how it will land with real users. Priority P2 (not P1) because the unified shell in Story 1 is the prerequisite enabler — the Preview tab is meaningless without the shared state and tab strip.

**Independent Test**: With the shell already shipped, load or build a blueprint of 3+ actions. Open Preview, verify it renders the first action (or the action the AI most recently edited) as a form. Click Next twice; verify the form changes. Ask the AI to rename a field on Action 1 and check Preview has jumped to Action 1 showing the new name. Click Next manually; verify the next AI tool call does NOT move the cursor. Click Follow AI; verify the cursor snaps to whatever action the AI is discussing.

**Acceptance Scenarios**:

1. **Given** a blueprint has at least one action, **When** the designer opens the Preview tab, **Then** the form for one action renders with the participant name in the header and all configured fields visible.
2. **Given** the AI edits Action 2 via a tool call, **When** auto-cursor is active, **Then** Preview switches to Action 2 within one update cycle.
3. **Given** the designer has clicked Next once (manual cursor engaged), **When** the AI subsequently edits Action 3, **Then** the Preview cursor stays where the designer put it.
4. **Given** the designer is in manual cursor mode, **When** they click "Follow AI", **Then** the cursor moves to the action the AI most recently edited and auto-follow resumes.
5. **Given** the designer is on the last action, **When** they click Next, **Then** the Next button is disabled and no navigation occurs.
6. **Given** a blueprint has zero actions, **When** the designer opens Preview, **Then** an empty-state message explains that adding an action via AI or Diagram will populate Preview.
7. **Given** the Preview tab is focused, **When** the designer presses `[` or `]`, **Then** the cursor moves to the previous or next action respectively.
8. **Given** a blueprint action uses advanced form features (sections, conditional rules, file fields, review pages), **When** Preview renders it, **Then** those features behave the same way a citizen will experience them on real submission.

---

### User Story 3 — Legacy URL compatibility during rollout (Priority: P3)

Existing bookmarks, saved deep-links, and in-app links that point at the old designer URLs (`/designer/chat`, `/designer/chat/{blueprintId}`, `/designer`) continue to work for one release cycle after the new shell ships. Navigating to any of these legacy URLs transparently lands the user on the equivalent view inside the new shell with no interruption or error.

**Why this priority**: This is rollout polish — important for a clean transition but not an MVP blocker. Most designers will discover the new shell via the left-nav entry; only users with stale bookmarks or external documentation links rely on the redirects. P3 reflects its narrow audience and temporary lifespan.

**Independent Test**: After the shell is deployed, visit each legacy URL in a browser and confirm the new shell loads with the correct tab pre-selected and the correct blueprint (if an ID was in the URL).

**Acceptance Scenarios**:

1. **Given** a saved bookmark points at `/designer/chat`, **When** the designer clicks it, **Then** the new shell opens with the AI tab selected.
2. **Given** a saved bookmark points at `/designer/chat/{blueprintId}`, **When** the designer clicks it, **Then** the new shell opens with the AI tab selected AND the named blueprint loaded.
3. **Given** a saved bookmark points at `/designer`, **When** the designer clicks it, **Then** the new shell opens with the Diagram tab selected.
4. **Given** the rollout window has ended, **When** legacy redirect shims are removed in a follow-up release, **Then** visiting a legacy URL returns a 404 (expected — bookmarks should have been updated by then).

---

### Edge Cases

- **Designer reloads the page mid-conversation.** The chat session, selected tab, and active blueprint are restored from the URL and server-side session store. The designer lands back where they left off.
- **Designer has unsaved edits and tries to close the browser tab.** The browser's native "leave page" confirm appears (wired from the shell's unsaved-changes state).
- **Two browser tabs open the same blueprint.** Both tabs see their own in-memory state; the last save wins. This is existing behaviour, not changed by the redesign.
- **The AI tool call references an action that was deleted in the Diagram tab since.** Preview's cursor logic gracefully falls back to the first available action and Preview renders that instead of a blank state.
- **A blueprint has more than 20 actions** and the jump dropdown becomes unwieldy. Dropdown remains single-select with scroll; search-within-dropdown is deferred as a future enhancement.
- **The designer switches to Preview before the AI has produced any blueprint.** Preview tab is disabled in that case (greyed out) and a tooltip explains "Build or load a blueprint first".
- **SignalR disconnects during use.** The connection chip in the shared toolbar turns amber/red; chat input is disabled with an explanatory message; existing tabs keep rendering cached state so the designer isn't locked out.
- **The designer clicks Save while validation shows errors.** Save proceeds — the draft persists — but the validation pill remains red. A follow-up publish action (out of scope here) is what validation gates.

## Requirements *(mandatory)*

### Functional Requirements

**Shell & Navigation**

- **FR-001**: The blueprint designer MUST be a single page hosting three tabs: AI Designer, Diagram, and Form Preview, reached via one canonical URL.
- **FR-002**: The URL MUST encode both the blueprint identifier (when one exists) and the currently-active tab, so a refresh or shared link restores the same view.
- **FR-003**: Tab switching MUST preserve all in-flight state: chat history, live chat connection, diagram camera and selection, preview cursor position, unsaved edits.
- **FR-004**: The designer's existing left navbar MUST remain visible and unchanged alongside the new shell.
- **FR-005**: An unsaved-changes guard MUST prompt the designer before any navigation that would discard in-progress edits.

**AI Designer tab**

- **FR-006**: The AI chat MUST occupy the full width of the page's content area (no side-by-side preview column).
- **FR-007**: The chat input control MUST remain pinned to the bottom of the viewport regardless of how many messages have accumulated; only the messages area scrolls.
- **FR-008**: When new messages arrive, the messages area MUST auto-scroll to show them, unless the designer has manually scrolled up — in which case auto-scroll pauses until they return to the bottom.
- **FR-009**: The chat session MUST survive tab switches without losing history or disconnecting.
- **FR-010**: The designer MUST NOT see two competing toolbars — save/load/export controls that previously lived inside the chat page are consolidated into the shared shell toolbar.

**Diagram tab**

- **FR-011**: The Diagram tab MUST render the current blueprint as an interactive graph with all features the existing `/designer` page provides (draggable action nodes, participant assignments, route lines, context menus).
- **FR-012**: Hand-edits made in the Diagram tab (rename, reposition, add, delete) MUST update the shared blueprint so that switching back to AI Designer shows the new state and the AI's next response reflects it.
- **FR-013**: The Diagram tab's own toolbar and navigation guard MUST be removed — those responsibilities live in the shell now.
- **FR-014**: Clicking an action node in the Diagram MUST set the Preview tab's cursor to that action.

**Form Preview tab**

- **FR-015**: The Form Preview tab MUST render exactly one action at a time, using the same rendering pathway that production citizen-facing submission uses.
- **FR-016**: Preview MUST render all schema extensions supported at submission time (sectioned layouts, conditional rules, file inputs, review pages, persona autofills).
- **FR-017**: The submit button MUST be visible but disabled, with a tooltip indicating preview mode.
- **FR-018**: The preview cursor MUST auto-follow AI activity by default — whenever the AI creates or modifies an action via a tool call, Preview switches to that action.
- **FR-019**: The designer MUST be able to override auto-cursor by clicking Previous, Next, or the jump dropdown; once manual override is active, subsequent AI edits MUST NOT move the cursor.
- **FR-020**: A prominent "Follow AI" toggle MUST allow the designer to resume auto-cursor; activating it MUST snap the cursor to the action the AI most recently edited.
- **FR-021**: Keyboard shortcuts `[` (previous) and `]` (next) MUST work when the Preview pane has keyboard focus.
- **FR-022**: The Preview tab MUST be disabled when no blueprint is loaded or the blueprint has zero actions; a hint MUST explain how to populate it.

**Shared toolbar**

- **FR-023**: A single toolbar MUST be visible above the tab strip on every tab, carrying: blueprint title (editable inline), dirty-state indicator, chat-session connection chip (only when a chat session is active), Load, Save, Export (JSON/YAML), validation summary pill, and message-quota counter (only when a chat session is active).
- **FR-024**: Save MUST persist the current blueprint from whichever tab is active.
- **FR-025**: Load MUST open a dialog that lists saved blueprints and lets the designer pick one; selecting a blueprint MUST replace the current shell state (with a confirm-discard prompt if there are unsaved edits).
- **FR-026**: The validation pill MUST show a summary of current validation state; clicking it MUST open a detail popover listing each issue.
- **FR-027**: Validation state MUST NOT block Save — drafts always persist. Validation gates the separate publish action, which is out of scope.

**Legacy URL compatibility**

- **FR-028**: The old designer URLs (`/designer/chat`, `/designer/chat/{blueprintId}`, `/designer`) MUST redirect to the equivalent view inside the new shell for one release cycle after deployment.
- **FR-029**: The left-nav designer entry MUST link to the new canonical URL.
- **FR-030**: Any in-app link or handoff button pointing at the legacy designer URLs MUST be updated to the new URL.

### Key Entities *(include if feature involves data)*

- **Designer Session**: The in-flight state of a blueprint being designed, including the current blueprint definition, its validation result, the active chat session identifier (if any), the currently focused action, the auto-vs-manual cursor mode, and whether there are unsaved edits. Lives for the duration of the designer page load.
- **Blueprint**: Existing domain entity — the workflow being designed. Nothing about its structure or persistence changes in this feature.
- **Chat Session**: Existing domain entity — the AI conversation tied to a blueprint. Nothing about its structure or persistence changes in this feature.
- **Active Action Cursor**: A pointer identifying which action the Preview tab is currently rendering, plus a flag indicating whether the cursor is auto-following AI activity or has been manually taken over by the designer.

## Assumptions

- The AI conversation model, tool set, and schema/credential integrations shipped in Feature 063 are not changing. This work is UI-only.
- The existing citizen-facing form renderer is production-proven and handles the full range of schema extensions without modification. Preview reuses it unchanged.
- One blueprint per designer session is sufficient. Concurrent multi-blueprint editing in a single session is not in scope.
- Designer is an internal-role feature (roles: Administrator, SystemAdmin, Designer). It is desktop-first; no mobile-specific layout is designed.
- The left navbar, its routes, and its entries are owned by the app shell and will be updated to point at the new canonical URL as part of this work but no broader navbar redesign is included.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a conversation of 50 or more messages, the chat input remains visible without the designer needing to scroll.
- **SC-002**: A designer can switch between the three tabs and return to the starting tab without losing any session state (chat history, diagram camera, preview cursor, unsaved edits), verified by every tab round-trip preserving observable state.
- **SC-003**: When the AI edits an action via a tool call and the designer is on Preview with auto-cursor active, the rendered form updates to show that action within one update cycle.
- **SC-004**: A designer can preview every action of a 10-action blueprint using only pager controls — no page reloads, no typing — in under 30 seconds.
- **SC-005**: Save works identically from any tab; the persisted blueprint contains all edits made in any tab during the session.
- **SC-006**: A legacy URL clicked by a user lands on the corresponding new-shell view with no error within one release cycle of deployment.
- **SC-007**: Designers report (qualitative) that the new unified shell is materially less frustrating than the previous split-pane chat plus separate diagram page. Measured via a short feedback check at the end of the first full release cycle using the new shell.
- **SC-008**: Zero browser-console errors during a three-tab round-trip with a populated blueprint.
