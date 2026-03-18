# Implementation Plan: Transaction Explorer UX Overhaul

**Branch**: `064-transaction-explorer` | **Date**: 2026-03-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/064-transaction-explorer/spec.md`

## Summary

Overhaul the register detail page transaction viewer: replace the right-side detail drawer with a full-width bottom-docked resizable panel, add Raw/Decoded/Tree payload tabs with JSON pretty-printing and Base64 cleanup, rename Policy tab to Governance, add a Register Map tab with DAG visualization of transaction lineage (PrevTxId chains), implement docket drill-through with breadcrumb navigation, and build encryption-aware payload display with extensibility for future field-level encryption. A new lightweight transaction graph API endpoint is needed to efficiently build the DAG without fetching full payload data.

## Technical Context

**Language/Version**: C# 13 / .NET 10, Blazor WASM
**Primary Dependencies**: MudBlazor (UI components), System.Text.Json (payload decoding), IJSRuntime (localStorage, resize, file download)
**Storage**: N/A (reads from Register Service REST API via HttpClient)
**Testing**: xUnit + FluentAssertions (unit tests for view models, services), Playwright (E2E)
**Target Platform**: Blazor WebAssembly (browser), served via Sorcha.UI.Web
**Project Type**: Web application (Blazor WASM client components + backend API endpoint)
**Performance Goals**: Register Map renders 200-node DAG in <2s; bottom panel resize at 60fps
**Constraints**: No external JS graph libraries (SVG rendered in Blazor); localStorage for persistence; follow existing JS interop patterns (splitter.js, clipboard.js, file-utils.js)
**Scale/Scope**: Registers typically 10-500 transactions; progressive loading for larger registers

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | UI reads from Register Service API only; new graph endpoint owned by Register Service |
| II. Security First | PASS | No new secrets; encryption display reads existing WalletAccess metadata |
| III. API Documentation | PASS | New graph endpoint will have XML docs + Scalar OpenAPI |
| IV. Testing Requirements | PASS | Unit tests for PayloadDecoder, GraphBuilder; Playwright E2E for panel interactions |
| V. Code Quality | PASS | Async/await, DI, nullable enabled, no warnings |
| VI. Blueprint Standards | N/A | No blueprint creation in this feature |
| VII. Domain-Driven Design | PASS | Uses canonical terms: Transaction, Docket, Register, Participant |
| VIII. Observability | PASS | Structured logging for graph endpoint; existing telemetry covers UI |

No violations. Complexity tracking not needed.

## Project Structure

### Documentation (this feature)

```text
specs/064-transaction-explorer/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0: Research findings
├── data-model.md        # Phase 1: Data model changes
├── quickstart.md        # Phase 1: Implementation quickstart
├── contracts/           # Phase 1: API contracts
│   └── graph-endpoint.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (via /speckit.tasks)
```

### Source Code (repository root)

```text
# Backend: New lightweight graph endpoint
src/Services/Sorcha.Register.Service/
└── Program.cs                              # Add GET /api/registers/{id}/transactions/graph

# Frontend: Blazor WASM components (all changes)
src/Apps/Sorcha.UI/Sorcha.UI.Core/
├── Components/
│   ├── Registers/
│   │   ├── TransactionDetail.razor         # REWRITE → bottom panel layout
│   │   ├── TransactionDetailPanel.razor    # NEW: bottom dock container with resize
│   │   ├── PayloadViewer.razor             # NEW: Raw/Decoded/Tree tabs
│   │   ├── PayloadPills.razor              # NEW: multi-payload pill tabs
│   │   ├── EncryptionIndicator.razor       # NEW: payload access status display
│   │   └── TransactionList.razor           # MODIFY: keyboard nav, "Show in Map" button
│   ├── Explorer/
│   │   ├── DocketDetail.razor              # MODIFY: use bottom panel, breadcrumbs
│   │   ├── DocketChain.razor               # MODIFY: bottom panel integration
│   │   └── RegisterMap.razor               # NEW: DAG visualization
│   └── Shared/
│       ├── JsonTreeView.razor              # EXTEND: encryption indicators, search
│       ├── ResizableSplitter.razor         # NEW: generic horizontal splitter
│       └── BreadcrumbNav.razor             # NEW: breadcrumb navigation
├── Models/Registers/
│   ├── PayloadViewModel.cs                 # EXTEND: DecodedContent, IsAccessible
│   ├── TransactionGraphNode.cs             # NEW: DAG node model
│   └── NavigationContext.cs                # NEW: breadcrumb state
├── Services/
│   ├── TransactionService.cs               # MODIFY: trim payload data, add graph fetch
│   ├── ITransactionService.cs              # MODIFY: add GetTransactionGraphAsync
│   └── PayloadDecoderService.cs            # NEW: Base64 decode, JSON detect, pretty-print

# Frontend: Page layout changes
src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/
└── Pages/Registers/
    └── Detail.razor                        # REWRITE: bottom panel, 4 tabs, keyboard nav

# Frontend: JS interop
src/Apps/Sorcha.UI/Sorcha.UI.Web/
└── wwwroot/app/js/
    └── resizable-panel.js                  # NEW: drag resize (follows splitter.js pattern)

# Tests
tests/Sorcha.UI.Core.Tests/
├── Services/
│   ├── PayloadDecoderServiceTests.cs       # NEW: decode, trim, JSON detection
│   └── TransactionServiceTests.cs          # EXTEND: graph fetch, trim tests
├── Models/
│   └── TransactionGraphNodeTests.cs        # NEW: DAG building, chain highlight
└── Components/                             # Playwright E2E added later
```

**Structure Decision**: This feature spans two layers — a small backend addition (one lightweight endpoint in Register Service) and a substantial frontend rewrite (Blazor WASM components in Sorcha.UI.Core). The UI changes follow the existing component structure under `Components/Registers/` and `Components/Explorer/`. No new projects are created.

## Complexity Tracking

No constitution violations to justify.
