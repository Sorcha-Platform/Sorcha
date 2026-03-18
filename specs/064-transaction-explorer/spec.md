# Feature Specification: Transaction Explorer UX Overhaul

**Feature Branch**: `064-transaction-explorer`
**Created**: 2026-03-18
**Status**: Draft
**Input**: User description: "Transaction Explorer UX Overhaul — bottom-docked detail panel, Raw/Decoded/Tree payload tabs, encryption-aware field display, JSON payload cleanup, tab rename Policy→Governance, new Register Map tab with transaction lineage DAG visualization"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Viewing Transaction Details in Bottom Dock Panel (Priority: P1)

A register administrator opens the register detail page and clicks on a transaction in the list. Instead of a narrow right-side drawer, a full-width panel slides up from the bottom of the screen (like VS Code's integrated terminal). The left side of the panel shows transaction metadata (TX ID, status, type, docket, timestamp, sender, recipients, signature). The right side shows the payload viewer. The panel can be resized by dragging the divider between the transaction list and the detail panel. The panel height is remembered across sessions.

**Why this priority**: The bottom dock is the foundational layout change — every other feature (payload tabs, encryption display, docket drill-through) depends on this panel existing.

**Independent Test**: Can be fully tested by clicking any transaction row and verifying the bottom panel appears with correct metadata. Delivers immediate value by giving payloads the horizontal space they need.

**Acceptance Scenarios**:

1. **Given** the register detail page is loaded with transactions, **When** the user clicks a transaction row, **Then** a full-width panel docks to the bottom of the screen showing transaction metadata on the left and payload content on the right.
2. **Given** the bottom panel is open, **When** the user drags the resize handle between the list and panel, **Then** the panel height adjusts accordingly and the new height is persisted for the next visit.
3. **Given** the bottom panel is open, **When** the user clicks the close button or presses Escape, **Then** the panel closes and the transaction list expands to fill the full height.
4. **Given** the bottom panel is open, **When** the user clicks a different transaction row, **Then** the panel updates to show the newly selected transaction's details.
5. **Given** a narrow viewport (mobile/tablet), **When** the user clicks a transaction, **Then** the panel occupies the full width and adjusts metadata and payload sections to stack vertically.

---

### User Story 2 - Decoded Payload Viewing with Raw/Decoded/Tree Tabs (Priority: P1)

A register user selects a transaction and wants to understand the payload content. The payload viewer area in the bottom panel shows tabbed views: **Raw** (the Base64-encoded string as stored), **Decoded** (the Base64-decoded content, pretty-printed if JSON), and **Tree** (a collapsible JSON tree for JSON payloads). When the transaction has multiple payloads, horizontal pill tabs above the viewer let the user switch between payloads (e.g., `[Payload 0 (2.1 KB)] [Payload 1 (0.8 KB)]`). The Raw tab always shows clean data with no trailing whitespace or line-feed characters.

**Why this priority**: Payload inspection is the core value of the transaction explorer — users need to read and understand what data was committed to the register.

**Independent Test**: Can be tested by selecting any transaction with a JSON payload and verifying all three tabs render correctly, with the Decoded tab showing properly formatted JSON.

**Acceptance Scenarios**:

1. **Given** a transaction with a JSON payload is selected, **When** the user views the Decoded tab, **Then** the JSON is pretty-printed with proper indentation and syntax highlighting.
2. **Given** a transaction with a JSON payload is selected, **When** the user views the Raw tab, **Then** the original Base64 string is displayed in a monospace font with no trailing LF, CR, or whitespace characters.
3. **Given** a transaction with a JSON payload is selected, **When** the user views the Tree tab, **Then** a collapsible/expandable JSON tree is displayed with clickable nodes.
4. **Given** a transaction with multiple payloads, **When** the user views the payload area, **Then** horizontal pill tabs show each payload with its index and size, and clicking a pill switches the viewer content.
5. **Given** a transaction with a non-JSON payload (e.g., binary data), **When** the user views the Decoded tab, **Then** the raw UTF-8 text is displayed, or a message indicates the content cannot be displayed as text.
6. **Given** a payload with trailing LF/CR characters in the Base64 data, **When** the data is loaded, **Then** the trailing characters are stripped before display in any tab.

---

### User Story 3 - Register Map: Transaction Lineage DAG (Priority: P2)

A register investigator wants to understand the flow of transactions through a register. They navigate to the **Register Map** tab which displays a directed acyclic graph (DAG) where each node is a transaction and each edge represents a PrevTxId link. Genesis transactions (with no previous transaction) appear as root nodes. The default layout flows left-to-right (genesis on the left, latest on the right), but the user can toggle to a top-to-bottom layout via a layout toggle button. The chosen direction persists across sessions. Nodes are colour-coded by blueprint instance or transaction type. The user can pan and zoom the graph. Clicking a node highlights its entire chain from genesis to the selected transaction.

From the **Transactions** tab, users can click a "Show in Map" button on any transaction to jump to the Register Map tab with that transaction pre-selected and its chain highlighted.

**Why this priority**: The Register Map provides unique investigative capability — understanding transaction provenance and flow — but it depends on the data model (PrevTxId) and the bottom panel layout being in place first.

**Independent Test**: Can be tested by loading a register with multiple transaction chains and verifying the DAG renders with correct edges, colour-coding, and click-to-highlight behaviour.

**Acceptance Scenarios**:

1. **Given** the Register Map tab is active, **When** transactions are loaded, **Then** a DAG is rendered with genesis transactions as root nodes and edges following PrevTxId links, defaulting to left-to-right layout.
2. **Given** the DAG is displayed, **When** the user clicks a transaction node, **Then** the entire chain from genesis to the clicked transaction is highlighted, and the bottom panel shows that transaction's details.
3. **Given** the DAG is displayed, **When** the user uses scroll/pinch gestures or zoom controls, **Then** the view zooms in or out smoothly while maintaining readability.
4. **Given** the DAG is displayed, **When** the user drags the canvas, **Then** the view pans to reveal off-screen portions of the graph.
5. **Given** a transaction is selected in the Transactions tab, **When** the user clicks "Show in Map", **Then** the view switches to the Register Map tab with that transaction's node centred and its chain highlighted.
6. **Given** a register with transactions from multiple blueprint instances, **When** the DAG renders, **Then** nodes are colour-coded by blueprint instance ID (or by transaction type when no blueprint metadata exists).
7. **Given** a register with many transactions (100+), **When** the Register Map loads, **Then** only the most recent portion is rendered initially with a "Load more" control to expand the view backwards toward genesis.
8. **Given** the DAG is displayed in left-to-right layout, **When** the user clicks the layout toggle button, **Then** the DAG re-renders in top-to-bottom layout and the preference is persisted for the next visit.
9. **Given** the user previously selected top-to-bottom layout, **When** they revisit the Register Map, **Then** the DAG renders in top-to-bottom layout.

---

### User Story 4 - Docket Drill-Through Navigation (Priority: P2)

A register auditor viewing the **Docket Chain** tab clicks on a docket block in the timeline. The bottom panel opens showing the docket's metadata and a list of transactions sealed in that docket. The auditor clicks a transaction within the docket list, and the panel transitions to show the full transaction detail (metadata + payload viewer). A breadcrumb trail at the top of the panel shows the navigation path: `Register > Docket #N > TX abc123...`, allowing the user to click back to the docket's transaction list or the register level.

**Why this priority**: Docket-to-transaction drill-through is essential for audit workflows where users need to verify what was sealed in each docket block.

**Independent Test**: Can be tested by selecting a docket with multiple transactions and verifying the breadcrumb navigation works correctly at each level.

**Acceptance Scenarios**:

1. **Given** the Docket Chain tab is active, **When** the user clicks a docket, **Then** the bottom panel opens showing the docket's metadata and a scrollable list of transactions within it.
2. **Given** the docket's transaction list is shown in the bottom panel, **When** the user clicks a transaction, **Then** the panel transitions to show the full transaction detail (metadata + payload viewer).
3. **Given** a transaction detail is shown from a docket drill-through, **When** the user views the breadcrumb, **Then** it shows `Register > Docket #N > TX [truncated ID]` with each segment clickable.
4. **Given** a breadcrumb is showing `Register > Docket #N > TX ...`, **When** the user clicks "Docket #N", **Then** the panel returns to the docket's transaction list view.
5. **Given** the Transactions tab is showing a confirmed transaction, **When** the user clicks the docket number link in the bottom panel metadata, **Then** the view switches to the Docket Chain tab with that docket pre-selected and its transactions loaded in the bottom panel.

---

### User Story 5 - Encryption-Aware Payload Display (Priority: P3)

A participant views a transaction payload that contains encrypted data. The viewer inspects the transaction's payload metadata (IV presence, WalletAccess list) to determine encryption state. Currently, encryption operates at the payload level — the entire payload is either decryptable (user's wallet is in WalletAccess) or opaque (it is not). The Decoded tab shows the payload's accessibility status: if the user has access, the full decoded content displays with an unlocked indicator; if not, the payload shows as a redacted encrypted block with a locked indicator. A summary bar shows the access status (e.g., "Payload accessible" or "Payload encrypted — you are not in the access list"). The component structure is designed so that when field-level selective encryption is implemented in the future, per-field lock/unlock indicators and a "N/M fields visible" summary can be added without reworking the viewer architecture.

**Why this priority**: Encryption-aware display requires knowledge of the user's wallet address and the payload encryption scheme, which depends on the payload viewer infrastructure being complete.

**Independent Test**: Can be tested by viewing a transaction where the current user's wallet is or is not in the payload's WalletAccess list and verifying the correct accessible/encrypted state displays.

**Acceptance Scenarios**:

1. **Given** a transaction with an encrypted payload (IV present), **When** the current user's wallet address is in the payload's WalletAccess list, **Then** the payload content displays decoded with an unlocked indicator.
2. **Given** a transaction with an encrypted payload (IV present), **When** the current user's wallet address is NOT in the payload's WalletAccess list, **Then** the payload displays as a redacted encrypted block with a locked indicator and a message explaining access is restricted.
3. **Given** a transaction with multiple payloads of mixed accessibility, **When** the Decoded tab is active, **Then** each payload pill tab shows its access status (locked/unlocked icon) and the summary bar reflects overall accessibility (e.g., "2/3 payloads accessible").
4. **Given** a transaction with no encryption (no IV, no WalletAccess restrictions), **When** the Decoded tab is active, **Then** all content displays normally with no lock/unlock indicators.
5. **Given** a future field-level encryption implementation, **When** the viewer component receives per-field access metadata, **Then** the component structure supports showing per-field lock/unlock indicators without architectural rework.

---

### User Story 6 - Schema-Annotated Payload Display (Priority: P3)

When a transaction is associated with a blueprint (has a BlueprintId in metadata), the payload viewer can overlay schema information. Instead of showing raw field names like `applicantName`, the viewer displays the schema-defined label (e.g., "Applicant Name") alongside the field, along with the schema description as a tooltip. This helps users understand the business meaning of data fields without needing to cross-reference the blueprint definition.

**Why this priority**: Schema overlay is an enhancement on top of the decoded payload viewer. It requires the blueprint's action schema to be fetchable, adding a dependency on the Blueprint Service.

**Independent Test**: Can be tested by viewing a transaction that has a BlueprintId and verifying that field labels from the corresponding action schema appear alongside the decoded JSON field names.

**Acceptance Scenarios**:

1. **Given** a transaction with BlueprintId metadata, **When** the Decoded or Tree tab is active, **Then** each field shows both its JSON key and the schema-defined label.
2. **Given** a transaction with BlueprintId metadata, **When** the user hovers over a field label, **Then** a tooltip displays the schema description for that field.
3. **Given** a transaction without BlueprintId metadata, **When** the payload viewer is active, **Then** only raw JSON field names are shown (no schema annotation).
4. **Given** a transaction with BlueprintId but the schema cannot be fetched, **When** the payload viewer is active, **Then** it falls back gracefully to showing raw field names without schema annotation.

---

### User Story 7 - Quick Actions and Keyboard Navigation (Priority: P3)

Power users can navigate the transaction list using keyboard shortcuts: Up/Down arrows move between transactions, Enter opens the bottom panel for the selected transaction, and Escape closes it. The bottom panel header includes a quick actions toolbar with icon buttons for: Copy TX ID, Copy decoded payload, Download raw payload as file, and a "Show in Map" button to jump to the Register Map view.

**Why this priority**: Keyboard navigation and quick actions are quality-of-life improvements that enhance efficiency for power users but are not essential for basic functionality.

**Independent Test**: Can be tested by navigating the transaction list using keyboard only and verifying all quick action buttons produce the correct output.

**Acceptance Scenarios**:

1. **Given** the transaction list has focus, **When** the user presses the Down arrow, **Then** the next transaction row is highlighted.
2. **Given** a transaction row is highlighted, **When** the user presses Enter, **Then** the bottom panel opens with that transaction's details.
3. **Given** the bottom panel is open, **When** the user presses Escape, **Then** the panel closes.
4. **Given** the bottom panel is open, **When** the user clicks the Copy TX ID button, **Then** the full transaction ID is copied to the clipboard with a confirmation toast.
5. **Given** the bottom panel is open with a decoded payload, **When** the user clicks the Download Raw button, **Then** the raw Base64 payload is downloaded as a file.
6. **Given** the bottom panel is open, **When** the user clicks "Show in Map", **Then** the view switches to the Register Map tab with the current transaction highlighted.

---

### Edge Cases

- What happens when a transaction's PrevTxId references a transaction that hasn't been loaded yet in the Register Map? The graph shows a "load more" stub node that can be clicked to fetch the missing portion of the chain.
- What happens when a payload's Base64 data is malformed and cannot be decoded? The Decoded tab shows an error message indicating the data is corrupted, and the Raw tab still displays the original Base64 string.
- What happens when the register has thousands of transactions and the Register Map is opened? Only the most recent N transactions are loaded initially (e.g., latest 50). The user can expand the view backward with a "Load earlier" control. A minimap or overview indicator shows the user's current viewport relative to the full graph.
- How does the bottom panel behave when the browser window is very short? The panel has a minimum height (e.g., 150px) and the transaction list has a minimum height (e.g., 200px). If both minimums cannot be satisfied, the panel scrolls internally.
- What happens when the user navigates to the Register Map tab but the register has zero transactions? An empty state is shown: "No transactions in this register yet."
- What happens when a transaction chain has a very deep lineage (100+ ancestors)? The chain highlight loads ancestors lazily, fetching in batches of 20, with a loading indicator showing progress along the chain.
- What happens when the user resizes the browser window while the bottom panel is open? The panel height ratio is maintained relative to the available space, clamped to min/max bounds.

## Requirements *(mandatory)*

### Functional Requirements

**Data Layer**

- **FR-001**: System MUST trim leading and trailing whitespace, LF (\\n), and CR (\\r) characters from payload Base64 data at the mapping/service layer before it reaches the UI.
- **FR-002**: System MUST decode Base64 payload data to UTF-8 text for the Decoded view, and attempt JSON parsing when the content type is `application/json` or when the decoded text appears to be valid JSON.
- **FR-003**: System MUST pretty-print JSON payloads with proper indentation (2-space or 4-space) and syntax highlighting in the Decoded tab.

**Layout & Panel**

- **FR-004**: System MUST replace the current right-side transaction detail drawer with a full-width bottom-docked panel that spans the entire width of the content area.
- **FR-005**: The bottom panel MUST be resizable via a draggable divider between the transaction list and the panel, with a minimum panel height of 150px and a minimum list height of 200px.
- **FR-006**: The panel height MUST persist across browser sessions using local storage.
- **FR-007**: The bottom panel MUST display transaction metadata (TX ID, status, type, docket, timestamp, sender, recipients, signature, action ID) on the left and the payload viewer on the right.
- **FR-008**: On narrow viewports (below medium breakpoint), the metadata and payload sections MUST stack vertically.

**Payload Viewer**

- **FR-009**: The payload viewer MUST provide three viewing modes via tabs: Raw (Base64 as-is), Decoded (decoded text, pretty-printed if JSON), and Tree (collapsible JSON tree).
- **FR-010**: When a transaction has multiple payloads, the viewer MUST display horizontal pill tabs showing each payload's index and human-readable size (e.g., "Payload 0 (2.1 KB)").
- **FR-011**: The Tree tab MUST render a collapsible/expandable tree view for JSON payloads, reusing or extending the existing JsonTreeView component.
- **FR-012**: Each payload viewer tab MUST include a copy button to copy the displayed content to the clipboard.

**Tab Structure**

- **FR-013**: The register detail page tabs MUST be renamed and reordered as: Transactions, Docket Chain, Governance (renamed from Policy), Register Map.
- **FR-014**: The Register Map tab MUST display a directed acyclic graph (DAG) where nodes represent transactions and directed edges represent PrevTxId links. The default layout MUST be left-to-right; a toggle button MUST allow switching to top-to-bottom layout, with the preference persisted across sessions.
- **FR-015**: Genesis transactions (those with empty or zero PrevTxId) MUST appear as root nodes in the DAG.
- **FR-016**: DAG nodes MUST be colour-coded by blueprint instance ID when available, or by transaction type when no blueprint metadata exists.
- **FR-017**: The DAG MUST support interactive pan (drag) and zoom (scroll/pinch) navigation.
- **FR-018**: Clicking a DAG node MUST highlight the entire ancestor chain from genesis to the clicked transaction and open the bottom panel with that transaction's details.
- **FR-019**: The Register Map MUST implement progressive loading: initially showing the most recent transactions and allowing the user to load earlier transactions on demand.

**Cross-View Navigation**

- **FR-020**: A "Show in Map" action MUST be available from the Transactions tab bottom panel, navigating to the Register Map with the current transaction's node centred and chain highlighted.
- **FR-021**: Clicking a docket number in the Transactions tab detail panel MUST navigate to the Docket Chain tab with that docket pre-selected.
- **FR-022**: The Docket Chain tab MUST use the same bottom panel paradigm: clicking a docket opens the panel with docket metadata and its transaction list; clicking a transaction within shows full transaction detail.
- **FR-023**: A breadcrumb trail MUST appear in the bottom panel header showing the navigation context (e.g., "Register > Docket #N > TX abc123…") with each segment clickable.

**Encryption-Aware Display**

- **FR-024**: When a payload has an IV (encryption indicator) and a WalletAccess list, the viewer MUST determine accessibility by checking whether the current user's wallet address is in the WalletAccess list. Encryption state is currently payload-level (entire payload accessible or not).
- **FR-025**: Accessible payloads MUST display decoded content with an unlocked indicator; inaccessible payloads MUST display as redacted encrypted blocks with a locked indicator and an explanatory message.
- **FR-026**: A summary bar MUST indicate overall payload accessibility (e.g., "2/3 payloads accessible" for multi-payload transactions, or "Payload accessible"/"Payload encrypted" for single-payload transactions).
- **FR-026a**: The encryption display component MUST be structured to support future field-level selective encryption (per-field lock/unlock indicators) without architectural rework.

**Schema Overlay**

- **FR-027**: When a transaction has BlueprintId metadata, the system SHOULD fetch the corresponding action schema and display field labels and descriptions alongside the decoded JSON.
- **FR-028**: Schema fetch failures MUST fall back gracefully to displaying raw JSON field names only.

**Quick Actions & Keyboard**

- **FR-029**: The bottom panel header MUST include a quick actions toolbar with: Copy TX ID, Copy Payload, Download Raw, Show in Map.
- **FR-030**: Up/Down arrow keys MUST navigate between transaction rows when the list has focus. Enter MUST open the bottom panel. Escape MUST close the bottom panel.

### Key Entities

- **TransactionViewModel**: Existing view model extended with computed decoded payload content. Key attributes: TxId, PrevTxId, SenderWallet, RecipientsWallets, Payloads, BlueprintId, InstanceId, TransactionType.
- **PayloadViewModel**: Extended with decoded content, detected content type, and encryption visibility status. Key attributes: Index, Hash, Data (raw Base64), DecodedContent (UTF-8 text), ContentType, IsEncrypted, VisibleFields, EncryptedFields.
- **TransactionGraphNode**: A node in the Register Map DAG. Key attributes: TxId, PrevTxId, TransactionType, BlueprintInstanceId, Position (computed layout coordinates), IsHighlighted, IsGenesis.
- **TransactionGraphEdge**: A directed edge in the DAG. Key attributes: SourceTxId, TargetTxId, IsHighlighted.
- **NavigationContext**: Tracks the breadcrumb state for the bottom panel. Key attributes: CurrentLevel (Register/Docket/Transaction), DocketId, DocketVersion, TransactionId.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can view decoded JSON payload content within 1 click from the transaction list (select transaction → payload is visible immediately in the Decoded tab).
- **SC-002**: The bottom panel provides at least 60% more horizontal space for payload display compared to the current right-side drawer (full width vs 40% width).
- **SC-003**: Users can trace any transaction's complete lineage from genesis in the Register Map with 2 clicks (open Register Map tab, click transaction node).
- **SC-004**: Users can navigate from a docket to its transactions to a specific transaction's payload in 3 clicks or fewer.
- **SC-005**: Payload data displayed in the Raw tab contains zero trailing whitespace, LF, or CR characters.
- **SC-006**: The Register Map renders registers with up to 200 transactions within 2 seconds of tab activation.
- **SC-007**: Panel resize position persists correctly across browser sessions 100% of the time.
- **SC-008**: All keyboard navigation shortcuts (Up, Down, Enter, Escape) work consistently across modern browsers.
- **SC-009**: Encrypted payload fields are clearly distinguishable from visible fields at a glance, with no user confusion about which data they can access.

## Clarifications

### Session 2026-03-18

- Q: Should encryption-aware display design for current payload-level encryption or future field-level? → A: Both — design for payload-level now (check IV + WalletAccess on the transaction to determine accessibility) but structure the component to extend to field-level encryption without rework.
- Q: DAG flow direction — left-to-right, top-to-bottom, or user-toggleable? → A: User-toggleable. Default left-to-right with a toggle button to switch to top-to-bottom. Preference persisted across sessions.

## Assumptions

- The existing `JsonTreeView.razor` component can be reused or extended for the Tree tab without a rewrite.
- `PrevTxId` is always a 64-character hex string when set, or empty/all-zeros for genesis transactions. The DAG can be built by following these links.
- For encryption-aware display, the current user's wallet address is available from the authentication context (JWT claims). Current encryption operates at the payload level (IV + WalletAccess determines whole-payload accessibility). The component is designed to extend to field-level encryption when that capability is added to the platform.
- Schema overlay requires a service client call to the Blueprint Service to fetch action schemas. This is an optional enhancement — the feature works without it.
- A client-side graph rendering approach will be used for the Register Map (e.g., SVG/Canvas). The specific library choice is an implementation detail.
- The "Governance" tab rename is a straightforward text change with no functional differences to the tab content.
- Panel height persistence uses the browser's localStorage API, which is available in Blazor WASM via JS interop.
