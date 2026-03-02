# Feature Specification: UI Polish & Blueprint Designer

**Feature Branch**: `046-ui-polish-designer`
**Created**: 2026-03-02
**Status**: Draft
**Input**: User description: "UI Polish and Blueprint Designer - fix dashboard wizard bug, welcome GUID bug, notification panel CSS, dark mode hardcoded colors, stale coming-soon labels, snackbar-to-ActivityLog migration, i18n wiring, plus blueprint visual designer load/save functionality"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Dashboard Correctly Welcomes Returning Users (Priority: P1)

A user who has already created a wallet and set it as default navigates to the home dashboard. The system recognizes them, displays their friendly display name (not an internal identifier), and shows the dashboard content — not the first-time setup wizard.

**Why this priority**: This is a critical usability bug. Every returning user sees a broken experience — either a GUID as their name or a repeated setup wizard prompting them to create a wallet they already have. This undermines trust in the platform.

**Independent Test**: Can be verified by creating a wallet, returning to the dashboard, and confirming the wizard does not appear and the display name is human-readable.

**Acceptance Scenarios**:

1. **Given** a user with one wallet exists, **When** they navigate to the home dashboard, **Then** the dashboard content is displayed (not the setup wizard) and their display name appears in the welcome greeting.
2. **Given** a user creates their first wallet via the setup wizard, **When** creation completes, **Then** the wallet is automatically set as the default and the user is returned to the dashboard showing normal content.
3. **Given** a user with multiple wallets but no explicit default, **When** they navigate to the dashboard, **Then** the system auto-selects a wallet and shows the dashboard (not the wizard).
4. **Given** a user with zero wallets, **When** they navigate to the dashboard, **Then** the setup wizard is shown exactly once, guiding them to create their first wallet.
5. **Given** any authenticated user, **When** the dashboard welcome greeting is rendered, **Then** their human-readable name is displayed (not a GUID, internal ID, or cryptographic address).

---

### User Story 2 - Notification Panel Stays Hidden When Closed (Priority: P1)

The notification/activity log panel on the right side of the screen is fully hidden when closed. Users do not see it when scrolling horizontally, and no horizontal scrollbar appears due to the panel.

**Why this priority**: A layout bug that affects every page. Visible off-screen content and unwanted horizontal scrolling degrades the entire platform experience.

**Independent Test**: Can be verified by closing the notification panel and confirming no horizontal scroll exists and the panel is not visible at any scroll position.

**Acceptance Scenarios**:

1. **Given** the notification panel is closed, **When** the user scrolls horizontally on any page, **Then** the panel is not visible and no extra horizontal scroll area exists.
2. **Given** the notification panel is open, **When** the user clicks outside it or closes it, **Then** it slides out of view completely with no residual visual artifacts.
3. **Given** any viewport width (desktop, tablet, narrow), **When** the notification panel is closed, **Then** no horizontal overflow is caused by the panel.

---

### User Story 3 - Dark Mode Renders All Pages Readably (Priority: P1)

A user who enables dark mode sees all pages rendered with appropriate contrast and readability. No page shows hardcoded light backgrounds, white boxes, or unreadable text against dark surfaces.

**Why this priority**: Dark mode is a core theme feature already exposed in Settings. Users who enable it currently encounter unreadable content on key pages (wallet creation, blueprint designer, wallet details), making those features unusable in dark mode.

**Independent Test**: Can be verified by enabling dark mode and navigating through every major page, confirming all text is readable and no hardcoded light backgrounds appear.

**Acceptance Scenarios**:

1. **Given** dark mode is enabled, **When** the user views the wallet creation page (including mnemonic word display), **Then** all text is readable with appropriate contrast against dark surfaces.
2. **Given** dark mode is enabled, **When** the user views the blueprint visual designer (action nodes, properties panel, viewer diagram), **Then** all node cards, panels, and labels use theme-appropriate colors.
3. **Given** dark mode is enabled, **When** the user views the wallet detail page (including signature results), **Then** all content areas use dark-mode-compatible backgrounds and text colors.
4. **Given** the user switches between light and dark mode, **When** any page is viewed, **Then** all visual elements adapt to the selected theme without requiring a page refresh.

---

### User Story 4 - Stale "Coming Soon" Labels Removed (Priority: P2)

Features that have been fully implemented on the backend but are still labeled "Coming Soon" or disabled in the UI are updated to reflect their actual availability. Users can access post-quantum wallet algorithms (ML-DSA-65, ML-KEM-768), see transaction history on wallet detail, and use TOTP two-factor authentication setup.

**Why this priority**: These labels mislead users about platform capabilities and block access to completed features. Removing them is a quick polish task that instantly expands perceived functionality.

**Independent Test**: Can be verified by navigating to each previously-labeled page and confirming the feature is accessible and functional (or, where the backend is genuinely incomplete, that the label accurately reflects status).

**Acceptance Scenarios**:

1. **Given** the wallet creation form, **When** the user selects an algorithm, **Then** ML-DSA-65 and ML-KEM-768 are available as selectable options (not disabled/grayed out).
2. **Given** the wallet detail page, **When** the user views the Transactions tab, **Then** transaction history is displayed (or a meaningful empty state if no transactions exist) — not a "Coming Soon" placeholder.
3. **Given** the Settings page Security tab, **When** the user initiates TOTP 2FA setup, **Then** the system calls the actual TOTP backend service instead of showing a "not yet connected" message.
4. **Given** the Organization Configuration page, **When** the user views security policies and identity providers, **Then** labels accurately reflect the current implementation status (implemented features are not labeled "coming soon").

---

### User Story 5 - Blueprint Visual Designer Load/Save (Priority: P2)

A user working in the blueprint visual designer can save their current blueprint to the server and load previously saved blueprints. This replaces the current behavior where blueprints created in the designer exist only in-memory and are lost on navigation or refresh.

**Why this priority**: The visual designer is a core workflow tool. Without save/load, any blueprint designed visually must be manually exported/imported via JSON — defeating the purpose of the visual tool. This is essential for the designer to be production-usable.

**Independent Test**: Can be verified by creating a blueprint in the designer, saving it, navigating away, returning, loading it, and confirming all elements are preserved.

**Acceptance Scenarios**:

1. **Given** a user has designed a blueprint in the visual designer, **When** they click Save, **Then** the blueprint is persisted to the Blueprint Service and a confirmation is shown.
2. **Given** a user is on the visual designer page, **When** they click Load, **Then** a dialog shows their saved blueprints with title, last modified date, and participant count for selection.
3. **Given** a user selects a blueprint from the load dialog, **When** they confirm, **Then** the blueprint is loaded into the designer with all participants, actions, routes, schemas, and layout positions preserved.
4. **Given** a user has unsaved changes in the designer, **When** they attempt to load a different blueprint or navigate away, **Then** they are prompted to save or discard changes before proceeding.
5. **Given** a user saves a blueprint that already exists on the server, **When** the save completes, **Then** the existing blueprint is updated (not duplicated) and the version/timestamp is refreshed.
6. **Given** a user loads a blueprint and makes modifications, **When** they save, **Then** only the modified version is persisted (overwriting the previous version of that blueprint).

---

### User Story 6 - Integrated Notification System Replaces Toast Popups (Priority: P3)

System events and user feedback are routed through the integrated activity log notification system rather than ephemeral toast popups. Important events (transaction confirmations, action notifications, errors) appear in the activity log panel with persistence, while brief confirmations (clipboard copies) may remain as lightweight inline feedback.

**Why this priority**: The activity log system is already built and functional but unused. Migrating from toasts to the integrated system gives users a persistent record of events they can review, rather than losing information when a 5-second popup disappears. This is a large but mechanical migration.

**Independent Test**: Can be verified by triggering a system event (e.g., submitting an action), confirming it appears in the activity log panel, and verifying the unread badge increments.

**Acceptance Scenarios**:

1. **Given** a system event occurs (action submitted, transaction confirmed, blueprint saved), **When** the event fires, **Then** it appears in the activity log panel with appropriate severity, title, and detail — not as a toast popup.
2. **Given** multiple events occur in sequence, **When** the user opens the activity log panel, **Then** all events are listed chronologically with unread indicators.
3. **Given** a clipboard copy operation succeeds, **When** feedback is shown, **Then** a brief inline confirmation is displayed (toast is acceptable for ephemeral copy confirmations).
4. **Given** an error occurs (API failure, validation error), **When** the error is reported, **Then** it appears in both the activity log (for persistent reference) and as immediate inline feedback on the page where the error occurred.
5. **Given** unused toast/snackbar service injections exist in components, **When** the migration is complete, **Then** all unused `ISnackbar` injections are removed.

---

### User Story 7 - Localization Wiring Displays Translated Text (Priority: P3)

Users who select a non-English language in Settings see the UI rendered in their chosen language. The localization infrastructure (service, JSON translation files) is already complete — this wires the existing translations into all page components.

**Why this priority**: The infrastructure and translation files exist but are disconnected. Users who change the language setting see no effect, which is confusing. While large in scope (touching 40+ pages), the work is mechanical and each page can be wired independently.

**Independent Test**: Can be verified by switching language to French in Settings and confirming navigation labels, headings, and common text display in French.

**Acceptance Scenarios**:

1. **Given** a user selects French in Settings, **When** they navigate to any page, **Then** all text that has a translation key displays in French.
2. **Given** a user selects German in Settings, **When** they view the dashboard, **Then** the welcome message, navigation labels, and common buttons display in German.
3. **Given** a text string has no translation key defined, **When** the page renders, **Then** the English text is displayed as a fallback (not a raw key like "nav.dashboard").
4. **Given** the user switches language, **When** the change is applied, **Then** all currently visible text updates without requiring a full page reload.

---

### Edge Cases

- What happens when the user preferences API is unavailable when loading the dashboard? The dashboard should gracefully fall back to showing dashboard content (not the wizard) if wallet data can be retrieved even without a stored default preference.
- What happens when a user has wallets but the wallet API is temporarily down? The welcome message should not show a GUID — it should use cached or authentication-derived display name.
- What happens when a blueprint being loaded from the server has been deleted by another user? The load dialog should show an appropriate error and refresh the list.
- What happens when the user's JWT token lacks a `name` claim? The welcome greeting should fall back to a generic "Welcome back" rather than displaying empty text or a GUID.
- What happens when a translation JSON file fails to load? The system should fall back to English without errors.
- What happens when the notification panel is opened while in a narrow/mobile viewport? It should not cause layout shifts or push content off-screen.

## Requirements *(mandatory)*

### Functional Requirements

**Dashboard & Identity**
- **FR-001**: System MUST use smart wallet default selection (auto-select when only one wallet exists, fall back to first wallet when no explicit default is stored) to determine dashboard state.
- **FR-002**: System MUST display the user's human-readable display name in the welcome greeting and account menu, sourced from the authentication token's name claim — never an internal identifier or GUID.
- **FR-003**: System MUST set the newly created wallet as the default when a user completes first-wallet creation via any entry path (wizard or redirect).
- **FR-004**: System MUST NOT show the setup wizard when the user has at least one wallet, regardless of whether an explicit default preference has been stored.

**Notification Panel**
- **FR-005**: The notification panel MUST NOT cause horizontal overflow or scrollbars when in its closed/hidden state.
- **FR-006**: The notification panel MUST render within the application's layout container so that standard overflow clipping applies.

**Dark Mode**
- **FR-007**: All UI components MUST use theme-aware colors that adapt to light and dark mode — no hardcoded color values in inline styles or embedded CSS for backgrounds, text, or borders.
- **FR-008**: The dark mode palette MUST define sufficient color properties (surface, background, appbar, drawer, text) to ensure consistent rendering across all pages.
- **FR-009**: Code-editor-style views (JSON viewer) MAY use a fixed dark theme if they provide their own complete color scheme that is readable regardless of the application theme mode.

**Coming Soon Labels**
- **FR-010**: The wallet creation form MUST offer all implemented cryptographic algorithms (including post-quantum algorithms) as selectable options.
- **FR-011**: The wallet detail Transactions tab MUST display actual transaction data from the backend or an appropriate empty state — not a placeholder message.
- **FR-012**: The Settings 2FA section MUST connect to the actual TOTP backend service for setup, verification, and disable operations.
- **FR-013**: Features that are genuinely not yet implemented (e.g., OIDC/SAML identity provider integration) MUST clearly indicate their status without using the generic "Coming Soon" wording — use specific, informative text (e.g., "External identity provider integration is planned for a future release").

**Blueprint Designer Save/Load**
- **FR-014**: Users MUST be able to save the current visual designer blueprint to the Blueprint Service via a Save action.
- **FR-015**: Users MUST be able to load a previously saved blueprint into the visual designer via a Load dialog.
- **FR-016**: The Load dialog MUST display a list of the user's saved blueprints with title, last modified date, and summary information.
- **FR-017**: System MUST prompt users with unsaved changes before loading a different blueprint or navigating away from the designer.
- **FR-018**: Saving an existing blueprint MUST update the existing record (not create a duplicate).
- **FR-019**: All designer state (participants, actions, routes, schemas, conditions, calculations, layout positions) MUST be preserved through a save/load cycle.

**Snackbar Migration**
- **FR-020**: SignalR-driven system events (action notifications received via hub connections) MUST be routed to the integrated activity log rather than toast popups. Remaining event migration (direct API responses, clipboard operations) is deferred to a follow-up feature.
- **FR-021**: Brief ephemeral feedback (clipboard copy confirmations) MAY remain as lightweight inline notifications.
- **FR-022**: Error feedback from SignalR-driven events MUST be presented in the activity log. Inline error feedback for direct API calls remains unchanged in this feature. Dual-channel error reporting (both inline and activity log) is deferred to a follow-up feature.
- **FR-023**: All unused notification service injections MUST be removed from components after migration.

**Localization**
- **FR-024**: All user-facing text with a defined translation key MUST render in the user's selected language.
- **FR-025**: Text without a translation key MUST fall back to English (not display raw keys).
- **FR-026**: Language changes MUST apply to all visible text without requiring a full page reload.

### Key Entities

- **UserPreference (DefaultWallet)**: The user's stored default wallet address, used to determine dashboard state. Persisted via the user preferences API.
- **Blueprint**: A workflow definition with participants, actions, routes, schemas, and designer layout metadata. Persisted to the Blueprint Service.
- **ActivityEvent**: A logged notification event with severity, title, message, source service, read status, and timestamp. Persisted via the events API.
- **TranslationBundle**: A set of key-value string pairs for a specific language, loaded as a JSON file from the application's static assets.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users with at least one wallet see the dashboard (not the setup wizard) 100% of the time on every visit.
- **SC-002**: The welcome greeting displays a human-readable name for 100% of authenticated users — never a GUID or internal identifier.
- **SC-003**: No horizontal scrollbar appears on any page when the notification panel is closed, verified across standard viewport widths (1024px, 1280px, 1440px, 1920px).
- **SC-004**: All pages are visually readable in both light and dark mode — no hardcoded light backgrounds remain in component styles (verified by visual inspection of all modified files).
- **SC-005**: Zero "Coming Soon" labels remain for features with completed backend implementations (PQC algorithms, TOTP 2FA, transaction history).
- **SC-006**: Users can complete a full blueprint save/load cycle (create in designer, save, navigate away, return, load) with 100% fidelity — all elements preserved.
- **SC-007**: System events appear in the activity log panel within 2 seconds of occurrence, with appropriate severity and detail.
- **SC-008**: Users who select a supported non-English language see navigation labels, page headings, and common UI text displayed in their chosen language.

## Assumptions

- The existing Blueprint Service API (`/api/blueprints`) supports create, update, and list operations needed for designer save/load — no new backend endpoints are required.
- The existing TOTP backend endpoints in the Tenant Service are functional and only need the UI to be wired to call them.
- The PQC algorithm implementations (ML-DSA-65, ML-KEM-768) in Sorcha.Cryptography are production-ready and the Wallet Service API accepts them — only the UI dropdown restriction needs removal.
- The existing 161 translation keys in the i18n JSON files cover the most commonly displayed text. Additional keys may need to be added as components are wired, but the initial translation set provides meaningful coverage.
- The `WalletPreferenceService.GetSmartDefaultAsync()` method correctly handles all wallet-count scenarios and is the intended replacement for raw preference lookups on the dashboard.
- The activity log backend (`/api/events`) is operational and the `ActivityLogPanel` component is fully functional — only the event routing from components needs to change.
