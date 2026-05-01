# Implementation Plan: AI Discoverability & Machine-Readable Marketing

**Branch**: `117-ai-discoverability` | **Date**: 2026-05-02 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/117-ai-discoverability/spec.md`

## Summary

Make Sorcha discoverable, parseable, and integrable by AI agents without human mediation. The substance already exists (36-tool MCP server, OpenID4VC issuance/verification, HAIP conformance work, IETF Token Status List, post-quantum internals); this spec ships the missing surface that lets an agent find and reason about it.

Six user stories, all P1 or P2:

- **US1** — `/.well-known/openapi.{json,yaml}` served by the API Gateway, fully-annotated OpenAPI 3.1, validates clean against `swagger-cli` and `spectral`.
- **US2** — `/.well-known/mcp.json` MCP discovery manifest, plus a 36-tool description audit so every tool meets a 2-sentence-with-disambiguation standard.
- **US3** — `llms.txt` at repo root and `docs/llms-full.txt`.
- **US4** — `STANDARDS.md` at repo root with structured compliance claims, PR-checklist enforcement, CI parse check.
- **US5** — `scripts/sorcha-setup.sh` exit-code discipline, `docker-compose.yml` topology comment, `docs/quickstart.md` with verify-installation step.
- **US6** — Four published technical documents under `docs/` adapted from planning narratives, with YAML frontmatter and standards cross-references.

A single CI workflow (`ai-discoverability-check.yml`) gates merge on every cross-reference: spectral lint, swagger-cli validate, MCP manifest schema, `STANDARDS.md` parse, `llms.txt` structure, marketing-adjective deny-list.

The feature is primarily documentation and well-known endpoint plumbing — no new services, no schema migrations, no security-sensitive code paths. Risk surface is limited to (a) accuracy of machine-readable claims, (b) endpoint generation completeness, and (c) drift between artefacts. Each is addressed by a CI check.

This plan is independent of every other in-flight spec. It builds on the substance of 095/096/097/098/099/113 by surfacing them but does not require any to merge first — partial-status rows in `STANDARDS.md` cover work in progress.

## Technical Context

**Language/Version**: C# 14, .NET 10. Plus YAML, JSON, Markdown, and shell (bash + PowerShell) for the documentation and CI surface.
**Primary Dependencies**: `Microsoft.AspNetCore.OpenApi` (the .NET 10 built-in, already wired in `Sorcha.ApiGateway/Program.cs:71` via `builder.AddSorchaOpenApi(...)` and `app.MapOpenApi()`). `YamlDotNet` for OpenAPI YAML serialisation (transitive). `@stoplight/spectral-cli` and `swagger-cli` for the lint and validate steps in CI (Node-based, run from GitHub Actions runners). `bats-core` (optional) for shell-script unit tests.
**Storage**: None. All artefacts are either served from runtime metadata (`/.well-known/openapi.json` is generated, `/.well-known/mcp.json` reads `appsettings.json` + tool count) or committed files in the repo.
**Testing**: xUnit + FluentAssertions for the C# integration tests under `tests/Sorcha.Gateway.Integration.Tests/` and `tests/Sorcha.McpServer.Tests/`. Bash + GitHub Actions for the CI orchestration script (`scripts/check-discoverability.sh`). A nightly cron-triggered workflow runs the quickstart on a clean `ubuntu-latest` runner.
**Target Platform**: Linux server (Docker), net10.0. CI runs on `ubuntu-latest`.
**Project Type**: Existing multi-service monorepo. No new services. The API Gateway gains two `/.well-known/*` endpoints; the MCP server tools have their `[Description("...")]` attributes rewritten in place.
**Performance Goals**: `/.well-known/openapi.json` and `/.well-known/mcp.json` P95 < 200 ms under cached conditions (NFR-005). `Cache-Control: public, max-age=300` on both (NFR-006).
**Constraints**: Machine-readable artefacts MUST be factual — marketing adjectives are CI-deny-listed (FR-045). Every standards reference MUST resolve to a `STANDARDS.md` row (FR-025, FR-042). `info.version` and MCP manifest `version` MUST be sourced from a single canonical version variable so they cannot drift (FR-046). Anonymous well-known endpoints MUST NOT expose `[ApiExplorerSettings(IgnoreApi = true)]` or `.ExcludeFromDescription()` endpoints (NFR-008).
**Scale/Scope**: Moderate. ~70 tasks across 9 phases, plus 36 explicit MCP-tool description rewrites at T039. Two new endpoints, one OpenAPI document transformer, one MCP manifest endpoint, ~6 new documentation files, one CI workflow. The endpoint-annotation pass spans every gateway-facing endpoint across 7 services (Blueprint, Wallet, Register, Tenant, Peer, Validator, Haip) — bounded by what's in `src/Services/*/Endpoints/`.

## Constitution Check

| Principle | Assessment |
|---|---|
| **I. Microservices-First Architecture** | PASS. No new services. Two new endpoints land in `Sorcha.ApiGateway` (`/.well-known/openapi.{json,yaml}` and `/.well-known/mcp.json`). The MCP-tool description audit edits `[Description]` attributes in place — no behaviour change to any tool. No upward dependencies introduced. |
| **II. Security First** | PASS. Well-known endpoints are anonymous by design (US1, US2 acceptance scenarios) — they describe the public API surface, never expose secrets. NFR-008 explicitly forbids exposing `[ApiExplorerSettings(IgnoreApi = true)]` or `.ExcludeFromDescription()` endpoints. CORS already permits `GET` from any origin per the gateway's existing `AddSorchaCors()`; T030 verifies this for the new paths. |
| **III. API Documentation** | PASS — this *is* the API documentation feature. The OpenAPI document is generated by `Microsoft.AspNetCore.OpenApi` (the .NET 10 built-in, mandated by Constitution III). Scalar UI at `/openapi` is preserved unchanged; the new `/.well-known/openapi.json` is a co-served alias. XML doc comments on public APIs are leaned on heavily (T026 enforces `<GenerateDocumentationFile>true</GenerateDocumentationFile>` is set so they flow into the OpenAPI document). |
| **IV. Testing Requirements** | PASS. New tests in `tests/Sorcha.Gateway.Integration.Tests/OpenApiWellKnownTests.cs` and `McpManifestWellKnownTests.cs` cover well-known endpoint behaviour. New unit test in `tests/Sorcha.McpServer.Tests/ToolDescriptionAuditTests.cs` enforces the 2-sentence-plus-disambiguation rule via reflection. Spectral rule unit tests under `.spectral.tests/`. Coverage on changed code stays > 85 %. The CI workflow itself is the integration test for the cross-reference contract. |
| **V. Code Quality** | PASS. C# changes follow async/await, nullable reference types, no Release-build warnings. The endpoint-annotation pass adds metadata only — no logic changes. Documentation is hand-written Markdown with linted frontmatter. |
| **VI. Blueprint Creation Standards** | N/A — no Blueprint authoring in this spec. |
| **VII. Domain-Driven Design** | PASS. New entities are wire-format records (`OpenApiInfoExtensions`, `McpManifest`, `McpTransport`, `McpAuthentication`, `McpToolCategory`) — small, anaemic by design because they describe over-the-wire shape. They sit in a new namespace `Sorcha.ApiGateway.Discoverability` to keep the bounded context clear. |
| **VIII. Observability by Default** | PASS. The two new well-known endpoints inherit the gateway's existing Serilog request logging and OpenTelemetry instrumentation. The CI workflow's failure modes are observable in PR checks; no separate dashboard required. The OpenAPI document is itself the platform's observability artefact for the public API surface. |

**Constitution gate: PASS.** No violations to justify.

## Project Structure

### Documentation (this feature)

```text
specs/117-ai-discoverability/
├── spec.md              # (complete)
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── README.md
│   ├── openapi-extensions.md
│   ├── mcp-manifest.schema.json
│   ├── mcp-manifest-example.json
│   ├── llms-txt-template.md
│   └── standards-md-template.md
└── tasks.md             # (complete from /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Services/
│   └── Sorcha.ApiGateway/
│       ├── Program.cs                                  # CHANGE — add /.well-known/openapi.{json,yaml} + /.well-known/mcp.json routes
│       ├── appsettings.json                            # CHANGE — add McpManifest configuration section
│       ├── Discoverability/                            # NEW namespace
│       │   ├── OpenApiInfoTransformer.cs               # NEW — injects info.x-mcp-server, info.x-standards, info.version into served OpenAPI doc
│       │   ├── McpManifestEndpoint.cs                  # NEW — handler for GET /.well-known/mcp.json
│       │   ├── McpToolCatalogueEndpoint.cs             # NEW — handler for GET /api/mcp/tools
│       │   ├── WellKnownOpenApiEndpoints.cs            # NEW — handler for GET /.well-known/openapi.{json,yaml}
│       │   └── Models/
│       │       ├── McpManifest.cs                      # NEW
│       │       ├── McpTransport.cs                     # NEW
│       │       ├── McpAuthentication.cs                # NEW
│       │       └── McpToolCategory.cs                  # NEW
│       └── Endpoints/                                   # CHANGE — annotate every endpoint with operationId/summary/description/tags
└── Common/
    └── Sorcha.ServiceDefaults/
        └── OpenApi/
            └── SorchaOpenApiExtensions.cs              # CHANGE — extend AddSorchaOpenApi to register OpenApiInfoTransformer

src/Services/Sorcha.{Blueprint,Wallet,Register,Tenant,Peer,Validator,Haip}.Service/Endpoints/  # CHANGE — annotate endpoints
src/Apps/Sorcha.McpServer/Tools/{Admin,Designer,Participant}/                                   # CHANGE — rewrite [Description] attributes for all 36 tools

tests/
├── Sorcha.Gateway.Integration.Tests/
│   ├── OpenApiWellKnownTests.cs                        # NEW
│   └── McpManifestWellKnownTests.cs                    # NEW
└── Sorcha.McpServer.Tests/
    └── ToolDescriptionAuditTests.cs                    # NEW

# Repo-root artefacts
llms.txt                                                # NEW
STANDARDS.md                                            # NEW
.spectral.yaml                                          # NEW
.github/workflows/ai-discoverability-check.yml          # NEW
.github/pull_request_template.md                        # CHANGE (or NEW)

# Documentation
docs/
├── architecture.md                                     # NEW (adapted from planning narrative)
├── openid4vc-haip-integration.md                       # NEW (adapted from planning narrative)
├── applicability.md                                    # NEW (adapted from planning narrative)
├── security-model.md                                   # NEW (synthesised)
├── quickstart.md                                       # NEW
├── mcp-server.md                                       # NEW
├── llms-full.txt                                       # NEW
└── README.md                                           # CHANGE — list new docs

# Scripts
scripts/
├── sorcha-setup.sh                                     # CHANGE — exit-code discipline + remediation hints
└── check-discoverability.sh                            # NEW — CI orchestrator

# Compose
docker-compose.yml                                      # CHANGE — add topology comment block
README.md                                               # CHANGE — link to llms.txt, STANDARDS.md, docs/quickstart.md
```

**Structure Decision**: Existing monorepo. Code changes concentrate in `src/Services/Sorcha.ApiGateway/Discoverability/` (new namespace) and `src/Apps/Sorcha.McpServer/Tools/` (description rewrites). All other artefacts are repo-root or `docs/`. No new projects, no new service boundaries.

## Complexity Tracking

*No Constitution violations — this section intentionally empty.*
