# Quickstart: Transaction Explorer UX Overhaul

**Branch**: `064-transaction-explorer` | **Date**: 2026-03-18

## Implementation Order

The feature decomposes into 6 implementation phases, ordered by dependency:

### Phase 1: Data Layer Cleanup + Payload Decoder (no UI changes yet)

**Goal**: Clean Base64 data and add decoding capability

1. **TransactionService.cs** — Trim `Data` field in `MapPayloads()`:
   ```
   Data = p.Data?.Trim()   // strips LF, CR, whitespace
   ```

2. **PayloadDecoderService.cs** — New service:
   - `TrimBase64(string raw)` → strip whitespace/LF/CR
   - `DecodeBase64ToUtf8(string base64)` → byte[] → UTF-8 string
   - `TryFormatJson(string text)` → (bool isJson, string? prettyPrinted)
   - `DetectContentType(string? declaredType, string? decodedText)` → string

3. **PayloadViewModel.cs** — Add `DecodedContent`, `IsJson`, `PrettyJson`, `ContentType`, `IsAccessible`, `IsEncrypted` properties

4. **Unit tests** for PayloadDecoderService (trim, decode, JSON detection, binary handling)

### Phase 2: Bottom Dock Panel + Tab Restructure

**Goal**: Replace right drawer with bottom panel, rename tabs

1. **resizable-panel.js** — JS interop for drag resize (clone splitter.js pattern)
2. **ResizableSplitter.razor** — Blazor wrapper for horizontal divider
3. **TransactionDetailPanel.razor** — New bottom dock container:
   - Left: transaction metadata (from existing TransactionDetail.razor content)
   - Right: payload viewer area (placeholder)
   - Close button, resize handle
4. **Detail.razor** — Rewrite layout:
   - Remove `MudGrid` 7/5 column split
   - Add vertical flex: list on top, splitter, panel on bottom
   - Rename tabs: Transactions, Docket Chain, Governance, Register Map (placeholder)
   - Wire localStorage persistence for panel height
5. **Keyboard navigation** — Add Up/Down/Enter/Escape handling to TransactionList

### Phase 3: Payload Viewer (Raw/Decoded/Tree tabs)

**Goal**: Full payload viewing experience in the bottom panel

1. **PayloadPills.razor** — Horizontal pill tabs for multi-payload selection
2. **PayloadViewer.razor** — Tabbed viewer:
   - Raw tab: monospace Base64 with copy button
   - Decoded tab: pretty-printed JSON (or plain text fallback) with copy button
   - Tree tab: JsonTreeView with decoded JsonElement
3. **JsonTreeView.razor** — Extend with optional encrypted field indicators
4. **Quick actions toolbar** — Copy TX ID, Copy Payload, Download Raw (reuse file-utils.js)

### Phase 4: Docket Drill-Through + Breadcrumbs

**Goal**: Bottom panel works for docket navigation too

1. **BreadcrumbNav.razor** — `Register > Docket #N > TX abc123...` with click handlers
2. **NavigationContext.cs** — State tracking for breadcrumb levels
3. **DocketChain.razor** — Emit `OnDocketSelected` to parent instead of inline detail
4. **DocketDetail.razor** — Render inside bottom panel, transaction clicks drill down
5. **Detail.razor** — Wire docket number click → Docket Chain tab with pre-selection
6. **Cross-tab navigation** — "Show in Map" button (wires to Phase 5)

### Phase 5: Register Map (DAG Visualization)

**Goal**: Transaction lineage DAG with pan/zoom and chain highlighting

1. **Graph endpoint** — `GET /api/registers/{id}/transactions/graph` in Register Service
2. **ITransactionService.cs** — Add `GetTransactionGraphAsync(registerId, limit, before?)`
3. **TransactionGraphNode.cs** — Node + Edge models
4. **GraphLayoutService.cs** — BFS rank assignment, median ordering, coordinate computation
5. **RegisterMap.razor** — SVG rendering:
   - Nodes as rounded rectangles (colour by InstanceId/type)
   - Edges as Bézier curves
   - Click → highlight chain + open bottom panel
   - Pan (pointer drag on canvas), zoom (scroll wheel via CSS transform)
   - Layout toggle: LTR ↔ TTB (persisted in localStorage)
   - Progressive loading: "Load earlier" button
6. **"Show in Map" wiring** — From Transactions tab → Register Map tab with pre-selection

### Phase 6: Encryption-Aware Display

**Goal**: Show payload access status based on wallet

1. **EncryptionIndicator.razor** — Lock/unlock badge per payload pill
2. **PayloadViewer.razor** — Add access status bar ("Payload accessible" / "Payload encrypted")
3. **Current user wallet resolution** — Read from JWT claims via AuthenticationStateProvider
4. **PayloadViewModel.IsAccessible** — Compute from WalletAccess + current user wallet

### Phase 7 (Optional/P3): Schema Overlay

**Goal**: Annotate decoded JSON with blueprint schema labels

1. **IBlueprintSchemaService** — Fetch action schema by BlueprintId + ActionId
2. **Schema annotation in Decoded tab** — Display `fieldName: "Label"` alongside values
3. **Tooltip on hover** — Schema description
4. **Graceful fallback** — Show raw keys if schema unavailable

## Key Files to Read First

| File | Why |
|------|-----|
| `Detail.razor` | Current page layout — will be rewritten |
| `TransactionDetail.razor` | Current drawer — content moves to bottom panel |
| `DocketChain.razor` + `DocketDetail.razor` | Docket navigation — modified for bottom panel |
| `splitter.js` | Pattern to follow for resize interop |
| `TransactionService.cs` | Mapping layer where Base64 trimming goes |
| `ConfigurationService.cs` | Pattern for localStorage access |
| `Register Service Program.cs` (line ~600+) | Transaction endpoints — new graph endpoint goes here |

## Testing Strategy

| Layer | Tool | Coverage |
|-------|------|----------|
| PayloadDecoderService | xUnit + FluentAssertions | Trim, decode, JSON detect, binary, malformed Base64 |
| GraphLayoutService | xUnit + FluentAssertions | BFS ranking, edge crossing minimization, genesis detection |
| TransactionGraphNode | xUnit | Chain highlighting, fork handling |
| NavigationContext | xUnit | State transitions, breadcrumb generation |
| Bottom panel interactions | Playwright E2E | Open/close, resize, keyboard nav, tab switching |
| Register Map | Playwright E2E | Pan/zoom, node click, chain highlight, layout toggle |
| Docket drill-through | Playwright E2E | Breadcrumb navigation, cross-tab links |
