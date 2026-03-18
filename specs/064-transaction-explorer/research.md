# Research: Transaction Explorer UX Overhaul

**Branch**: `064-transaction-explorer` | **Date**: 2026-03-18

## R1: DAG Rendering Approach for Register Map

**Decision**: SVG rendered directly in Blazor (C# layout algorithm + inline SVG markup)

**Rationale**: Follows Sorcha's pattern of minimal JS interop. The project avoids external JS libraries (D3.js, vis.js) and prefers Blazor-native rendering. SVG can be generated directly in Razor markup with `@foreach` loops over computed node/edge positions. Pan/zoom handled via CSS `transform` with JS pointer event helpers (following the existing `splitter.js` pattern). Click handlers are native Blazor `@onclick`.

**Alternatives considered**:
- **D3.js via JS interop**: Most capable, but adds ~250KB dependency, requires heavy JS↔.NET marshalling, and diverges from Blazor-first patterns.
- **Blazor.Diagrams NuGet**: Community library, but adds external dependency and may not match MudBlazor styling. Last release cadence uncertain for .NET 10.
- **Canvas rendering**: Better performance for 1000+ nodes, but loses native click handling and accessibility. Overkill for typical register sizes (10-500 transactions).

**Layout Algorithm**: Layered/Sugiyama-inspired approach:
1. BFS from genesis nodes to assign depth ranks (layer = distance from genesis)
2. Within each layer, order nodes to minimize edge crossings (median heuristic)
3. Compute X/Y from rank (column for LTR, row for TTB) and order within rank
4. Nodes sized 120x40px, spacing 60px horizontal / 80px vertical
5. Edges rendered as SVG `<path>` with cubic Bézier curves

## R2: Lightweight Transaction Graph API Endpoint

**Decision**: Add `GET /api/registers/{registerId}/transactions/graph` returning projected fields only

**Rationale**: Current transaction list endpoint returns full `TransactionModel` including Base64 payload data, signatures, and challenges. For a 200-transaction register, this could be 2-5MB of data when we only need ~50 bytes per transaction for the graph. The existing admin diagnostic endpoint already does a similar projection internally.

**Projection fields**: `TxId`, `PrevTxId`, `SenderWallet`, `TimeStamp`, `DocketNumber`, `MetaData.BlueprintId`, `MetaData.InstanceId`, `MetaData.TransactionType`

**Alternatives considered**:
- **Reuse existing paginated endpoint and discard payloads client-side**: Wastes bandwidth, requires multiple round-trips for pagination, unusable for registers with large payloads.
- **GraphQL-style field selection on existing endpoint**: Over-engineering for one use case. Would require a GraphQL layer across the Register Service.
- **WebSocket streaming**: Unnecessary complexity. Graph data is a one-time load, not real-time.

## R3: Payload Decoding Strategy

**Decision**: Client-side decoding in a `PayloadDecoderService` within Sorcha.UI.Core

**Rationale**: Payload data is Base64-encoded strings. Decoding to UTF-8 and JSON parsing are lightweight operations suitable for WASM. No server round-trip needed. The service handles: (1) trimming whitespace/LF/CR from raw Base64, (2) Base64 → byte[] → UTF-8 string, (3) JSON detection via `JsonDocument.TryParse`, (4) pretty-printing via `JsonSerializer.Serialize` with `WriteIndented = true`.

**Content type detection priority**:
1. `PayloadModel.ContentType` if set (e.g., `application/json`)
2. Heuristic: try `JsonDocument.TryParse()` on decoded UTF-8 text
3. Fallback: display as plain UTF-8 text
4. If UTF-8 decoding fails: display "Binary data — cannot display as text"

**Alternatives considered**:
- **Server-side decoding endpoint**: Adds latency and server load for no benefit. WASM handles this fine.
- **Decode lazily on tab switch**: Selected approach — decoded content computed when user switches to Decoded tab, not on transaction selection.

## R4: Bottom Panel Resize Implementation

**Decision**: Follow existing `splitter.js` pattern — JS handles pointer events at 60fps, callbacks to .NET on drag-end

**Rationale**: The project already has `splitter.js` implementing this exact pattern for the chat panel. The new `resizable-panel.js` follows the same architecture: `pointerdown` starts tracking, `pointermove` updates CSS directly (no .NET round-trip per frame), `pointerup` calls `DotNetObjectReference.invokeMethodAsync` with final height percentage. Height stored via `localStorage` using the same `IJSRuntime` direct access pattern used by `ConfigurationService.cs` and `BrowserTokenCache.cs`.

**Persistence key**: `sorcha:panel-height:register-detail` (follows `sorcha:` prefix convention)
**Default height**: 40% of available space
**Min/max**: 150px minimum panel, 200px minimum list

## R5: Encryption-Aware Display Architecture

**Decision**: Payload-level access check now, component interface designed for future field-level extension

**Rationale**: Current encryption is payload-level (WalletAccess list + IV determines whole-payload accessibility). The `EncryptionIndicator` component accepts an interface that can be swapped from `PayloadLevelAccessChecker` (current) to `FieldLevelAccessChecker` (future) without changing the component markup.

**Current user wallet resolution**: Read from JWT claims (`wallet_address` claim in the authentication state). The `CustomAuthenticationStateProvider` already extracts this. If the user has no linked wallet, all encrypted payloads show as "No wallet linked — cannot determine access".

## R6: Existing Component Reuse Assessment

| Component | Reuse Plan |
|-----------|------------|
| `JsonTreeView.razor` | Extend with optional `EncryptedFields` parameter for lock icons on nodes. Add `OnNodeClick` callback for future copy-field-value action. No breaking changes to existing `BlueprintJsonView` usage. |
| `TruncatedId.razor` | Reuse as-is for TX IDs, wallet addresses, hashes in the bottom panel. |
| `TransactionRow.razor` | Reuse as-is in the transaction list. Add selection highlight styling. |
| `TransactionList.razor` | Modify to emit keyboard events and expose `HighlightedIndex` for keyboard navigation. |
| `DocketChain.razor` | Modify to emit `OnDocketSelected` event for bottom panel integration instead of inline `DocketDetail`. |
| `DocketDetail.razor` | Modify to render inside the bottom panel rather than inline below the timeline. |
| `clipboardInterop` (JS) | Reuse for all copy operations. |
| `file-utils.js` (JS) | Reuse `downloadFile` for raw payload download. |
| `splitter.js` pattern | Clone and adapt for vertical (horizontal divider) resize. |
