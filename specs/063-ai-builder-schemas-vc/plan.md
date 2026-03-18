# Implementation Plan: AI Blueprint Builder Enhancement

**Branch**: `063-ai-builder-schemas-vc` | **Date**: 2026-03-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/063-ai-builder-schemas-vc/spec.md`

## Summary

Enhance the AI Blueprint Chat Designer with four workstreams: (1) a standardised schema library of 25+ reusable data schemas seeded on startup and accessible to the AI via new tools and system prompt awareness, (2) credential requirement and issuance tools exposing the existing `CredentialRequirement` and `CredentialIssuanceConfig` models through the AI builder, (3) a complete system prompt overhaul for professional, inquisitive conversation flow with minimal-disclosure defaults, and (4) chat UI layout fixes for fixed-bottom input and auto-scroll. The existing `SchemaEntry` model, `ISchemaStore`, and `TemplateSeedService` patterns provide the foundation — no new domain models or infrastructure needed.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Anthropic SDK (AI streaming), SignalR (chat hub), MudBlazor (UI), JsonSchema.Net (schema validation), Sorcha.Blueprint.Fluent (builder API), Sorcha.Blueprint.Schemas (schema store)
**Storage**: MongoDB (schemas via `ISchemaStore`), Redis (chat sessions), In-memory (templates via `IDocumentStore`)
**Testing**: xUnit 3.2.2 + FluentAssertions 8.8.0 + Moq 4.20.72
**Target Platform**: Linux containers (services) + Blazor WASM (UI)
**Project Type**: Web application (microservices + Blazor WASM frontend)
**Performance Goals**: Schema seeding < 5s on startup. AI tool execution < 100ms per tool call. Chat UI renders at 60fps during streaming.
**Constraints**: System prompt must stay under ~4000 tokens with schema/template summaries. Max 13 tools total for Anthropic API (from 8 currently).
**Scale/Scope**: 25+ schema files, 5 new AI tools, 1 system prompt rewrite, 2 UI component fixes, ~40 new tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | All changes within Blueprint Service + UI. No new service. No cross-service coupling. |
| II. Security First | PASS | No secrets in schema files. Schema data is public reference data. Credential models already validated. |
| III. API Documentation | PASS | No new API endpoints (tools are internal to the chat orchestration). System prompt is internal. |
| IV. Testing Requirements | PASS | Target >85% coverage for new code. Schema seeding, tool executor, system prompt tests planned. |
| V. Code Quality | PASS | Async/await for seeding + tool execution. DI for all services. Nullable enabled. |
| VI. Blueprint Creation Standards | PASS | Schemas are JSON files seeded at startup. AI uses Fluent API via tool executor. |
| VII. Domain-Driven Design | PASS | Uses ubiquitous language: Blueprint, Action, Participant, Disclosure. |
| VIII. Observability by Default | PASS | Structured logging in seed service and tool executor. No new health endpoints needed. |

No violations. No complexity tracking required.

**Post-Phase 1 Re-check**: All 8 principles still PASS. Design adds no new projects, no new services, no cross-service coupling. Schema files are plain JSON data. Tools operate on existing domain models via existing Fluent API. System prompt is internal to Blueprint Service. UI changes are CSS/JS fixes within existing components.

## Project Structure

### Documentation (this feature)

```text
specs/063-ai-builder-schemas-vc/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (tool definitions)
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
# Schema files (new)
blueprints/schemas/
├── people-identity/
│   ├── uk-address.json
│   ├── international-address.json
│   ├── contact-details.json
│   ├── personal-identity.json
│   └── company-identity.json
├── financial/
│   ├── payment-details.json
│   ├── invoice-line-item.json
│   └── bank-account.json
├── documents-evidence/
│   ├── document-upload.json
│   ├── signature-block.json
│   └── audit-entry.json
├── compliance-governance/
│   ├── risk-assessment.json
│   ├── approval-decision.json
│   └── due-diligence-check.json
├── supply-chain/
│   ├── product-item.json
│   ├── shipment-details.json
│   └── inspection-record.json
├── healthcare/
│   ├── patient-reference.json
│   └── clinical-observation.json
└── credentials/
    ├── training-certificate.json
    ├── professional-license.json
    ├── right-to-work.json
    ├── identity-verification.json
    ├── product-passport.json
    ├── inspection-certificate.json
    └── approval-attestation.json

# Backend changes (existing projects, modified files)
src/Services/Sorcha.Blueprint.Service/
├── Services/
│   ├── SchemaSeedService.cs              # NEW: IHostedService, seeds schemas from blueprints/schemas/
│   ├── ChatOrchestrationService.cs       # MODIFIED: new system prompt, dynamic schema/template summary injection
│   ├── BlueprintToolExecutor.cs          # MODIFIED: add 5 new tools (search_schemas, use_standard_schema, require_credential, issue_credential, search_templates)
│   └── AnthropicProviderService.cs       # UNCHANGED (tools passed generically)
├── Hubs/
│   └── ChatHub.cs                        # UNCHANGED
└── Program.cs                            # MODIFIED: register SchemaSeedService

# Frontend changes (existing projects, modified files)
src/Apps/Sorcha.UI/
├── Sorcha.UI.Web.Client/
│   ├── Pages/
│   │   └── BlueprintChat.razor           # MODIFIED: height calc fix
│   ├── Components/Chat/
│   │   ├── ChatPanel.razor               # MODIFIED: fixed input, auto-scroll JS interop
│   │   └── BlueprintPreview.razor        # MODIFIED: credential badges on actions
│   └── wwwroot/js/
│       └── chat-scroll.js                # NEW: auto-scroll helper
└── Sorcha.UI.Core/
    └── Models/Chat/
        └── ChatSession.cs                # UNCHANGED

# Tests (existing test projects, new files)
tests/
├── Sorcha.Blueprint.Service.Tests/
│   ├── Services/
│   │   ├── SchemaSeedServiceTests.cs     # NEW
│   │   ├── BlueprintToolExecutorTests.cs # MODIFIED: tests for 5 new tools
│   │   └── ChatOrchestrationServiceTests.cs # MODIFIED: system prompt tests
│   └── Hubs/
│       └── ChatHubTests.cs               # UNCHANGED
└── Sorcha.UI.E2E.Tests/
    └── Docker/
        └── BlueprintChatTests.cs         # NEW: E2E tests for chat UI layout
```

**Structure Decision**: No new projects. All changes are within existing `Sorcha.Blueprint.Service` and `Sorcha.UI` projects. Schema files follow the established `blueprints/` directory convention. This is the simplest possible structure.
