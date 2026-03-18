# Tasks: Transaction Explorer UX Overhaul

**Input**: Design documents from `/specs/064-transaction-explorer/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/graph-endpoint.md, quickstart.md

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Data layer cleanup and shared services that all UI stories depend on

- [x] T001 [P] Create PayloadDecoderService with TrimBase64, DecodeBase64ToUtf8, TryFormatJson, DetectContentType methods in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/PayloadDecoderService.cs
- [x] T002 [P] Create IPayloadDecoderService interface in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IPayloadDecoderService.cs
- [x] T003 [P] Add PayloadDecoderService unit tests (trim LF/CR/whitespace, decode valid Base64, JSON detection, malformed Base64 error, binary data fallback) in tests/Sorcha.UI.Core.Tests/Services/PayloadDecoderServiceTests.cs
- [x] T004 Trim payload Base64 Data field in MapPayloads() method — change `Data = p.Data` to `Data = p.Data?.Trim()` in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/TransactionService.cs
- [x] T005 Extend PayloadViewModel with DecodedContent, IsJson, PrettyJson, ContentType, IsEncrypted, IsAccessible properties in src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Registers/PayloadViewModel.cs
- [x] T006 Register IPayloadDecoderService in DI container in the appropriate Program.cs or service registration extension

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Bottom dock panel infrastructure — MUST be complete before any UI story can use the panel

**⚠️ CRITICAL**: User Stories 1-7 all depend on the bottom panel existing

- [x] T007 Create resizable-panel.js following splitter.js pattern — pointerdown/move/up events, DotNetObjectReference callbacks for drag start/end, min/max height clamping in src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/app/js/resizable-panel.js
- [x] T008 Create ResizableSplitter.razor — Blazor wrapper for horizontal divider, accepts MinTopHeight/MinBottomHeight/DefaultHeightPercent parameters, persists height to localStorage key `sorcha:panel-height:register-detail` via IJSRuntime, exposes OnHeightChanged callback in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Shared/ResizableSplitter.razor
- [x] T009 Create NavigationContext model with NavigationLevel enum (Register/Docket/Transaction), DocketId, DocketVersion, TransactionId, TransactionIdTruncated computed property in src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Registers/NavigationContext.cs
- [x] T010 [P] Create BreadcrumbNav.razor — renders clickable segments from NavigationContext (Register > Docket #N > TX abc123...), emits OnNavigate callback with target NavigationLevel in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Shared/BreadcrumbNav.razor

**Checkpoint**: Panel infrastructure ready — user story implementation can now begin

---

## Phase 3: User Story 1 - Bottom Dock Panel (Priority: P1) 🎯 MVP

**Goal**: Replace right-side drawer with full-width bottom-docked resizable panel showing transaction metadata

**Independent Test**: Click any transaction row → bottom panel appears with correct metadata on left, placeholder on right. Resize persists. Close with X or Escape.

### Implementation for User Story 1

- [x] T011 [US1] Create TransactionDetailPanel.razor — full-width bottom dock container with: left column (transaction metadata from existing TransactionDetail.razor content: TX ID, status/type chips, docket number, timestamp, sender, recipients, signature, action ID), right column (placeholder div for payload viewer), close button, BreadcrumbNav integration in header in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/TransactionDetailPanel.razor
- [x] T012 [US1] Rewrite Detail.razor layout — replace MudGrid 7/5 column split with vertical flex layout: transaction list fills top area, ResizableSplitter divider, TransactionDetailPanel docked at bottom (hidden when no selection). Rename tabs to: Transactions, Docket Chain, Governance (was Policy), Register Map (placeholder empty state). Wire _selectedTransaction to bottom panel. Keep all existing SignalR/real-time logic intact in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Detail.razor
- [x] T013 [US1] Update TransactionDetail.razor — refactor to extract metadata rendering into a reusable partial or keep as subcomponent consumed by TransactionDetailPanel. Remove the outer MudPaper wrapper (panel provides that now) in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/TransactionDetail.razor
- [x] T014 [US1] Add responsive stacking — on narrow viewports (below md breakpoint), metadata and payload columns stack vertically within TransactionDetailPanel in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/TransactionDetailPanel.razor
- [x] T015 [US1] Add Escape key handler — close bottom panel on Escape keypress when panel is open. Wire via @onkeydown on the containing element in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Detail.razor

**Checkpoint**: Bottom dock panel fully functional — click transaction → panel opens with metadata, resize works, height persists, Escape closes

---

## Phase 4: User Story 2 - Payload Viewer (Priority: P1) 🎯 MVP

**Goal**: Raw/Decoded/Tree tabs in the bottom panel's right column, multi-payload pill tabs, Base64 cleanup visible

**Independent Test**: Select transaction with JSON payload → Decoded tab shows pretty-printed JSON, Raw tab shows clean Base64, Tree tab shows collapsible tree. Multi-payload transactions show pill tabs.

### Implementation for User Story 2

- [x] T016 [P] [US2] Create PayloadPills.razor — horizontal MudChipSet with pill chips for each payload showing index and formatted size (e.g., "Payload 0 (2.1 KB)"), emits OnPayloadSelected(int index), highlights active pill in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/PayloadPills.razor
- [x] T017 [P] [US2] Create PayloadViewer.razor — accepts PayloadViewModel and IPayloadDecoderService. Three MudTabs: Raw (monospace Base64 in scrollable container with copy button), Decoded (pretty-printed JSON with syntax highlighting or plain text fallback, copy button, error message for malformed Base64), Tree (JsonTreeView with parsed JsonElement). Default active tab: Decoded. Include PayloadPills above tabs when PayloadCount > 1 in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/PayloadViewer.razor
- [x] T018 [US2] Integrate PayloadViewer into TransactionDetailPanel — replace right-column placeholder with PayloadViewer component, pass selected payload from pills in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/TransactionDetailPanel.razor
- [x] T019 [US2] Extend JsonTreeView.razor — add optional OnNodeClick EventCallback parameter for future copy-value-on-click. Add optional EncryptedPaths parameter (HashSet<string>) for future per-field lock icons. No breaking changes to existing BlueprintJsonView usage in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Shared/JsonTreeView.razor
- [x] T020 [US2] Add copy button per tab — each tab (Raw/Decoded/Tree) gets a MudIconButton that copies the displayed content to clipboard via clipboardInterop. Add confirmation snackbar in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/PayloadViewer.razor

**Checkpoint**: Full payload viewing — JSON payloads pretty-printed, tree browsable, raw Base64 clean, multi-payload navigation works

---

## Phase 5: User Story 3 - Register Map DAG (Priority: P2)

**Goal**: DAG visualization of transaction lineage with pan/zoom, colour-coding, chain highlighting, layout toggle

**Independent Test**: Open Register Map tab → DAG renders with genesis roots, click node → chain highlights to genesis, pan/zoom works, layout toggles between LTR and TTB.

### Implementation for User Story 3

- [x] T021 [P] [US3] Add GET /api/registers/{registerId}/transactions/graph endpoint in Register Service — MongoDB projection returning only TxId, PrevTxId, SenderWallet, TimeStamp, DocketNumber, BlueprintId, InstanceId, TransactionType. Support `limit` (default 200, max 1000) and `before` (cursor TxId) query params. Return TransactionGraphResponse with nodes array, totalCount, hasMore flag in src/Services/Sorcha.Register.Service/Program.cs
- [x] T022 [P] [US3] Create TransactionGraphNode.cs and TransactionGraphEdge.cs models — TxId, PrevTxId, SenderWallet, TimeStamp, DocketNumber, BlueprintId, InstanceId, TransactionType, X, Y, IsGenesis, IsHighlighted, Rank, OrderInRank. Edge: SourceTxId, TargetTxId, IsHighlighted in src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Registers/TransactionGraphNode.cs
- [x] T023 [P] [US3] Create GraphLayoutService.cs — BFS from genesis nodes to assign depth ranks, median heuristic for ordering within ranks, coordinate computation for both LTR (X=rank*180, Y=order*80) and TTB (X=order*180, Y=rank*80) layouts, chain highlighting (walk PrevTxId from selected node to genesis) in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/GraphLayoutService.cs
- [x] T024 [P] [US3] Add unit tests for GraphLayoutService — single chain, forked chain, multiple genesis roots, empty register, chain highlight walk, LTR vs TTB coordinate swap in tests/Sorcha.UI.Core.Tests/Services/GraphLayoutServiceTests.cs
- [x] T025 [US3] Add GetTransactionGraphAsync method to ITransactionService and TransactionService — calls graph endpoint, maps response to TransactionGraphNode list in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ITransactionService.cs and src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/TransactionService.cs
- [x] T026 [US3] Create RegisterMap.razor — SVG rendering of DAG: nodes as rounded <rect> with truncated TxId text, colour-coded by InstanceId (hash-to-hue) or TransactionType, edges as <path> cubic Bézier curves. Click node → highlight chain + emit OnTransactionSelected. CSS transform for pan (pointer drag) and zoom (wheel). Layout toggle button (LTR/TTB) persisting to localStorage. "Load earlier" button when hasMore=true. Empty state when no transactions in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Explorer/RegisterMap.razor
- [x] T027 [US3] Wire Register Map tab in Detail.razor — replace placeholder empty state with RegisterMap component. Wire OnTransactionSelected → fetch full transaction via TransactionService.GetTransactionAsync → open bottom panel. Pass RegisterId in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Detail.razor
- [x] T028 [US3] Add "Show in Map" button to TransactionDetailPanel — MudIconButton in quick actions area, emits OnShowInMap event with current TxId. Detail.razor handles by switching to Register Map tab and calling RegisterMap.HighlightTransaction(txId) in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/TransactionDetailPanel.razor

**Checkpoint**: Register Map renders DAG, click-to-highlight works, pan/zoom functional, "Show in Map" cross-navigates from Transactions tab

---

## Phase 6: User Story 4 - Docket Drill-Through (Priority: P2)

**Goal**: Bottom panel works for docket navigation with breadcrumb trail

**Independent Test**: Click docket in Docket Chain tab → bottom panel shows docket metadata + transaction list. Click transaction → panel shows full detail. Breadcrumb allows back navigation.

### Implementation for User Story 4

- [x] T029 [US4] Modify DocketChain.razor — remove inline DocketDetail rendering. Instead emit OnDocketSelected(DocketViewModel) event to parent. Keep timeline UI unchanged in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Explorer/DocketChain.razor
- [x] T030 [US4] Modify DocketDetail.razor — refactor to render inside bottom panel context. Remove outer MudPaper wrapper. Add transaction row click handler that emits OnTransactionSelected(TransactionViewModel) event instead of using internal _selectedTransaction state. Remove the internal MudGrid 7/5 split (bottom panel provides layout) in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Explorer/DocketDetail.razor
- [x] T031 [US4] Wire docket drill-through in Detail.razor — when Docket Chain tab is active and OnDocketSelected fires: open bottom panel, set NavigationContext to Docket level, show DocketDetail in panel. When DocketDetail emits OnTransactionSelected: set NavigationContext to Transaction level, show TransactionDetailPanel content. BreadcrumbNav OnNavigate handles back-navigation in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Detail.razor
- [x] T032 [US4] Add docket number cross-link in TransactionDetailPanel — when transaction has DocketNumber, render it as a clickable link. Click emits OnNavigateToDocket(docketNumber) which Detail.razor handles by switching to Docket Chain tab with that docket pre-selected in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/TransactionDetailPanel.razor

**Checkpoint**: Full docket drill-through — Docket Chain → docket → transaction → back via breadcrumb. Cross-tab docket link works.

---

## Phase 7: User Story 5 - Encryption-Aware Display (Priority: P3)

**Goal**: Show payload access status based on current user's wallet address

**Independent Test**: View encrypted transaction → payload shows locked/unlocked indicator based on WalletAccess. Summary bar shows access count.

### Implementation for User Story 5

- [x] T033 [P] [US5] Create EncryptionIndicator.razor — accepts IsEncrypted (bool), IsAccessible (bool). Renders MudIcon lock/unlock with colour (green unlocked, red locked) and tooltip text. Extensible: accepts optional FieldLevelMetadata parameter (null for now) in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/EncryptionIndicator.razor
- [x] T034 [US5] Add current user wallet resolution — create helper method or service that reads wallet_address claim from AuthenticationState via CascadingParameter. Return null if no wallet linked in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/CurrentUserWalletService.cs
- [x] T035 [US5] Compute IsAccessible in PayloadViewModel — when IsEncrypted (HasIV == true), check if current user's wallet is in WalletAccess list. Update MapPayloads in TransactionService to accept current wallet address and populate IsAccessible in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/TransactionService.cs
- [x] T036 [US5] Add EncryptionIndicator to PayloadPills — each pill chip shows lock/unlock icon via EncryptionIndicator when payload IsEncrypted. Add summary bar below pills: "N/M payloads accessible" or "Payload accessible"/"Payload encrypted" in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/PayloadViewer.razor
- [x] T037 [US5] Handle inaccessible payload in Decoded tab — when !IsAccessible, show redacted block with MudAlert explaining "This payload is encrypted. Your wallet address is not in the access list." instead of decoded content. Raw tab still shows Base64 in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/PayloadViewer.razor

**Checkpoint**: Encrypted payloads show clear access status, inaccessible payloads display redacted message, pills show lock icons

---

## Phase 8: User Story 6 - Schema Overlay (Priority: P3)

**Goal**: Annotate decoded JSON with blueprint schema field labels and descriptions

**Independent Test**: View transaction with BlueprintId → field labels from schema appear alongside JSON keys. Hover shows description tooltip. Missing schema falls back gracefully.

### Implementation for User Story 6

- [x] T038 [P] [US6] Create IBlueprintSchemaService and BlueprintSchemaService — fetch action schema by BlueprintId + ActionId via existing Blueprint Service client. Cache schemas in-memory (Dictionary keyed by blueprintId:actionId). Return schema as JsonElement or null on failure in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/BlueprintSchemaService.cs
- [x] T039 [US6] Add schema annotation to PayloadViewer Decoded tab — when transaction has BlueprintId, fetch schema via IBlueprintSchemaService. For each JSON field, look up matching schema property. Display: `fieldName → "Schema Label"` with MudTooltip showing description on hover. Graceful fallback: if schema unavailable, show raw field names only in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/PayloadViewer.razor
- [x] T040 [US6] Add schema annotation to JsonTreeView Tree tab — extend JsonTreeNode to accept optional SchemaProperties dictionary. When property name matches a schema key, display label in parentheses after property name. Add MudTooltip with description in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Shared/JsonTreeView.razor

**Checkpoint**: Schema labels and tooltips appear on decoded and tree views for blueprint transactions

---

## Phase 9: User Story 7 - Quick Actions & Keyboard Navigation (Priority: P3)

**Goal**: Keyboard shortcuts for transaction list navigation, quick action toolbar in bottom panel

**Independent Test**: Arrow keys navigate list, Enter opens panel, Escape closes. Copy/Download buttons work.

### Implementation for User Story 7

- [x] T041 [US7] Add keyboard navigation to TransactionList.razor — track _highlightedIndex state, @onkeydown handler for Up/Down (move highlight), Enter (select highlighted → open panel), Tab/Shift+Tab (skip to next/prev). Add visual highlight class to highlighted row (distinct from selected) in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/TransactionList.razor
- [x] T042 [US7] Add quick actions toolbar to TransactionDetailPanel — row of MudIconButtons in panel header: Copy TX ID (clipboard), Copy Decoded Payload (clipboard), Download Raw (file-utils.js downloadFile with filename `payload-{txId}-{index}.b64`), Show in Map (emits OnShowInMap event) in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/TransactionDetailPanel.razor

**Checkpoint**: Full keyboard navigation works, all quick action buttons functional

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Final quality pass across all user stories

- [ ] T043 Verify all existing Playwright E2E tests still pass after layout changes — run Sorcha.UI.E2E.Tests register-related tests (DEFERRED: requires Docker)
- [x] T044 [P] Remove dead code — clean up old TransactionDetail.razor styling (.payload-data-container, .signature-text) and unused MudGrid layout code from Detail.razor (VERIFIED: no dead code found, existing styles still in use)
- [x] T045 [P] Update service README and API documentation for new graph endpoint — add endpoint to docs/reference/API-DOCUMENTATION.md
- [x] T046 Update MASTER-TASKS.md with completion status for this feature

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on T001-T002 (PayloadDecoderService) for PayloadViewer usage later
- **US1 Bottom Panel (Phase 3)**: Depends on Phase 2 (ResizableSplitter, NavigationContext)
- **US2 Payload Viewer (Phase 4)**: Depends on Phase 3 (needs bottom panel right column) + Phase 1 (PayloadDecoderService)
- **US3 Register Map (Phase 5)**: Depends on Phase 3 (bottom panel for node-click detail). T021 (API endpoint) can start in parallel with Phases 3-4
- **US4 Docket Drill-Through (Phase 6)**: Depends on Phase 3 (bottom panel + breadcrumbs)
- **US5 Encryption (Phase 7)**: Depends on Phase 4 (PayloadViewer + PayloadPills)
- **US6 Schema Overlay (Phase 8)**: Depends on Phase 4 (PayloadViewer Decoded tab)
- **US7 Quick Actions (Phase 9)**: Depends on Phase 3 (bottom panel) + Phase 5 (Show in Map)
- **Polish (Phase 10)**: Depends on all desired stories being complete

### User Story Dependencies

- **US1 (P1)**: Foundation only — no other story dependencies
- **US2 (P1)**: Depends on US1 (needs bottom panel's right column)
- **US3 (P2)**: Depends on US1 (bottom panel for node detail). API endpoint (T021) is independent
- **US4 (P2)**: Depends on US1 (bottom panel + breadcrumbs)
- **US5 (P3)**: Depends on US2 (PayloadViewer, PayloadPills)
- **US6 (P3)**: Depends on US2 (PayloadViewer Decoded/Tree tabs)
- **US7 (P3)**: Depends on US1 + US3 (panel + Show in Map wiring)

### Parallel Opportunities

- T001, T002, T003 (PayloadDecoder service + tests) can all run in parallel
- T007, T008, T009, T010 (Foundational) — T008 depends on T007, but T009 and T010 are parallel
- T016, T017 (PayloadPills + PayloadViewer) are parallel
- T021, T022, T023, T024 (graph endpoint + models + layout service + tests) are all parallel
- T033, T034 (EncryptionIndicator + wallet service) are parallel
- T021 (API endpoint) can start during Phase 3-4 since it's backend work

---

## Parallel Example: User Story 3

```
# These 4 tasks can all run in parallel (different files, no dependencies):
T021: Graph endpoint in Register Service Program.cs
T022: TransactionGraphNode models in Sorcha.UI.Core/Models
T023: GraphLayoutService in Sorcha.UI.Core/Services
T024: GraphLayoutService unit tests

# Then sequentially:
T025: Wire GetTransactionGraphAsync in TransactionService (needs T022)
T026: RegisterMap.razor (needs T023, T025)
T027: Wire in Detail.razor (needs T026)
T028: Show in Map button (needs T026, T027)
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2)

1. Complete Phase 1: Setup (PayloadDecoder + Base64 trim)
2. Complete Phase 2: Foundational (ResizableSplitter, NavigationContext, BreadcrumbNav)
3. Complete Phase 3: US1 — Bottom dock panel with metadata
4. Complete Phase 4: US2 — Payload viewer with Raw/Decoded/Tree tabs
5. **STOP and VALIDATE**: Clean payload display in bottom panel, JSON pretty-printing works
6. Deploy/demo — immediate value delivered

### Incremental Delivery

1. Setup + Foundational → Infrastructure ready
2. US1 + US2 → Core transaction viewing (MVP!)
3. US3 → Register Map adds investigative capability
4. US4 → Docket drill-through adds audit capability
5. US5 → Encryption awareness adds security context
6. US6 → Schema overlay adds business meaning
7. US7 → Keyboard nav adds power-user efficiency
8. Each story adds value without breaking previous stories

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- US1 and US2 are both P1 but US2 depends on US1's panel — implement in order
- T021 (API endpoint) is the only backend task — can be developed independently and early
- Existing Playwright E2E tests must continue passing throughout (T043 validates)
- All JS interop follows existing patterns: splitter.js (resize), clipboardInterop (copy), file-utils.js (download)
