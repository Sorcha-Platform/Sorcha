---
description: "Task list for spec 117: AI Discoverability & Machine-Readable Marketing"
---

# Tasks: AI Discoverability & Machine-Readable Marketing

**Input**: Design documents from `specs/117-ai-discoverability/`
**Prerequisites**: spec.md, plan.md, research.md, data-model.md, contracts/

**Tests**: Included where the artefact is verifiable in code (OpenAPI lint, MCP tool description audit, standards cross-reference). Documentation tasks ship without unit tests but are gated by the CI workflow.

**Authoring source for content tasks**: `docs/strategic-context.md` is the canonical voice and framing source for every machine-readable artefact (OpenAPI `info.description`, `llms.txt`, MCP tool descriptions, `STANDARDS.md` intro, the four published `docs/` documents). Tasks that author externally-facing content cite it explicitly. Authors MUST read it before writing.

---

## Phase 1: Setup

- [ ] T001 Confirm `117-ai-discoverability` branch is rebased onto `master` with no merge conflicts (`git fetch origin && git rebase origin/master`)
- [ ] T002 Confirm `Microsoft.AspNetCore.OpenApi` is referenced from `src/Services/Sorcha.ApiGateway/Sorcha.ApiGateway.csproj` (.NET 10 framework metapackage; package reference may be implicit)
- [ ] T003 [P] Add Node dev dependencies for `@stoplight/spectral-cli` and `swagger-cli` to `package.json` at the repo root, or document availability in the discoverability CI workflow image
- [ ] T004 [P] Create `.spectral.yaml` at the repo root extending `spectral:oas` with custom rules: `operationId-pascalcase`, `description-required-on-properties`, `examples-required-on-credential-issuance`, `info-x-mcp-server-required`, `info-x-standards-required`, `no-marketing-adjectives` (deny-list `revolutionary`, `best-in-class`, `industry-leading`, `cutting-edge`, `world-class`, `seamless`)
- [ ] T005 [P] Create `scripts/check-discoverability.sh` orchestrator that runs spectral lint, swagger-cli validate, mcp-manifest schema validation, llms.txt structure check, STANDARDS.md parse, and standards cross-reference; exits non-zero with a single-line message naming the offending file on any failure

## Phase 2: Foundational

- [ ] T006 Establish baseline: build `dotnet build src/Services/Sorcha.ApiGateway/`, boot the gateway, capture the OpenAPI document at `http://localhost/openapi/v1.json` to `specs/117-ai-discoverability/baseline-openapi.json` (gitignored, used for diff review only)
- [ ] T007 [P] Audit endpoint metadata across `src/Services/Sorcha.ApiGateway/Program.cs` and the gateway-routed services. Produce `specs/117-ai-discoverability/endpoint-audit.md` listing every endpoint missing `WithName`, `WithSummary`, `WithDescription`, or `WithTags`
- [ ] T008 [P] Audit MCP tool descriptions: walk every file under `src/Apps/Sorcha.McpServer/Tools/{Admin,Designer,Participant}/`. Produce `specs/117-ai-discoverability/mcp-tool-audit.md` flagging tools whose `[Description]` is < 2 sentences or lacks a disambiguating phrase
- [ ] T009 [P] Inventory the planning-folder narratives required by US6 — `sorcha-architecture-narrative.md`, `sorcha-openid4vc-mdl-integration.md`, `sorcha-applicability.md`, `sorcha-architecture-evaluation.md`. Confirm presence and authorial readiness; record locations in `specs/117-ai-discoverability/source-narratives.md`. Surface any missing narrative as a Phase 8 blocker
- [ ] T010 [P] Audit `scripts/sorcha-setup.sh` end-to-end. Produce `specs/117-ai-discoverability/setup-script-audit.md` listing every prerequisite check, every silent-failure path, and every place lacking an exit code or error message
- [ ] T011 [P] Audit `docker-compose.yml` topology comments. Confirm whether a topology comment block is present in the first 30 lines; record findings in `specs/117-ai-discoverability/compose-audit.md`

---

## Phase 3: User Story 1 — OpenAPI 3.1 surface (Priority: P1)

**Goal**: `GET /.well-known/openapi.json` returns a complete, valid, lint-clean OpenAPI 3.1 document with every endpoint and schema property fully annotated.

**Independent Test**: From a fresh container, `curl -s http://localhost/.well-known/openapi.json | swagger-cli validate -` exits 0; `spectral lint http://localhost/.well-known/openapi.json` exits 0; piping the document into `openapi-typescript` produces a usable typed client.

### Tests for US1

- [ ] T012 [P] [US1] Add Spectral rule unit tests under `.spectral.tests/operationId-pascalcase.spec.yaml`, `description-required-on-properties.spec.yaml`, `examples-required-on-credential-issuance.spec.yaml`, `info-x-mcp-server-required.spec.yaml`, `info-x-standards-required.spec.yaml`, `no-marketing-adjectives.spec.yaml`
- [ ] T013 [P] [US1] Add integration test methods to `tests/Sorcha.Gateway.Integration.Tests/OpenApiWellKnownTests.cs`: `GET_WellKnownOpenapiJson_Returns200`, `GET_WellKnownOpenapiYaml_Returns200_WithApplicationYamlContentType` (asserts `Content-Type: application/yaml` exactly per FR-002), `OpenApiDocument_ContainsXMcpServer`, `OpenApiDocument_ContainsXStandards`, `OpenApiDocument_VersionMatchesAssemblyVersion`, `OpenApiDocument_InfoTitleNonEmpty` (FR-007), `OpenApiDocument_InfoContactUrlIsGitHubOrg` (FR-007), `OpenApiDocument_ExcludesAdminAndIgnoredEndpoints` (NFR-008 — asserts no path is marked `[ApiExplorerSettings(IgnoreApi = true)]` or `.ExcludeFromDescription()` reaches the served document)
- [ ] T014 [P] [US1] Add CI step in `.github/workflows/ai-discoverability-check.yml` running `spectral lint <served-openapi-url>` against the gateway booted in the workflow, failing on any violation

### Implementation for US1

- [ ] T015 [US1] Add `/.well-known/openapi.json` route handler to `src/Services/Sorcha.ApiGateway/Discoverability/WellKnownOpenApiEndpoints.cs` returning the same document `app.MapOpenApi()` serves with `Cache-Control: public, max-age=300` and `Content-Type: application/json`
- [ ] T016 [US1] Add `/.well-known/openapi.yaml` route handler to `src/Services/Sorcha.ApiGateway/Discoverability/WellKnownOpenApiEndpoints.cs` converting the JSON document to YAML via `YamlDotNet` with `Content-Type: application/yaml`
- [ ] T017 [US1] Create `src/Services/Sorcha.ApiGateway/Discoverability/OpenApiInfoTransformer.cs` implementing `IOpenApiDocumentTransformer` that injects `info.title`, `info.version`, `info.description`, `info.contact.url` (GitHub organisation URL — `https://github.com/Sorcha-Platform`), `info.x-mcp-server`, and `info.x-standards` from `IConfiguration` and assembly informational version (covers FR-007 + FR-008 + FR-009). **`info.description` content authored against `docs/strategic-context.md`** — frame Sorcha as cryptographic proof infrastructure for multi-party workflows, name AI-fraud and AI-decision-maker context, no marketing adjectives, ≤ 1000 characters
- [ ] T018 [US1] Register `OpenApiInfoTransformer` in `src/Common/Sorcha.ServiceDefaults/OpenApi/SorchaOpenApiExtensions.cs` `AddSorchaOpenApi` extension so every service gets the transform
- [ ] T019 [US1] Wire the well-known routes in `src/Services/Sorcha.ApiGateway/Program.cs` immediately after the existing `app.MapOpenApi()` call (currently line ~529)
- [ ] T020 [US1] [P] Annotate every endpoint in `src/Services/Sorcha.ApiGateway/Program.cs` with `WithName` (PascalCase `<Resource><Verb>`), `WithSummary`, `WithDescription`, and `WithTags` per the audit at T007
- [ ] T021 [US1] [P] Annotate every endpoint in `src/Services/Sorcha.Blueprint.Service/Endpoints/` with `WithName`, `WithSummary`, `WithDescription`, `WithTags`
- [ ] T022 [US1] [P] Annotate every endpoint in `src/Services/Sorcha.Wallet.Service/Endpoints/` (including `CredentialEndpoints.cs:498` `IssueCredential` — FR-006 requires example) with `WithName`, `WithSummary`, `WithDescription`, `WithTags`
- [ ] T023 [US1] [P] Annotate every endpoint in `src/Services/Sorcha.Register.Service/Endpoints/` with `WithName`, `WithSummary`, `WithDescription`, `WithTags`
- [ ] T024 [US1] [P] Annotate every endpoint in `src/Services/Sorcha.Tenant.Service/Endpoints/` with `WithName`, `WithSummary`, `WithDescription`, `WithTags`
- [ ] T025 [US1] [P] Annotate every endpoint in `src/Services/Sorcha.Peer.Service/Endpoints/` with `WithName`, `WithSummary`, `WithDescription`, `WithTags`
- [ ] T026 [US1] [P] Annotate every endpoint in `src/Services/Sorcha.Validator.Service/Endpoints/` with `WithName`, `WithSummary`, `WithDescription`, `WithTags`
- [ ] T027 [US1] [P] Annotate every endpoint in `src/Services/Sorcha.Haip.Service/Endpoints/` with `WithName`, `WithSummary`, `WithDescription`, `WithTags`
- [ ] T028 [US1] Add `[Description("...")]` (or XML doc summary) to every property on every request/response DTO under `src/Common/Sorcha.ServiceClients.Http/` and `src/Services/*/Models/`. Confirm `<GenerateDocumentationFile>true</GenerateDocumentationFile>` is set in each csproj so XML doc comments flow into OpenAPI
- [ ] T029 [US1] Add request and response examples for `IssueCredential` in `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs` via `[OpenApiExample]` or an `OpenApiOperationTransformer`. Source from `walkthroughs/TradeFinance/` payloads
- [ ] T030 [US1] Add request and response examples for the wallet signing endpoint in `src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs` (or equivalent `/sign-transaction` location)
- [ ] T031 [US1] Mark incomplete or unstable endpoints with `x-status: "partial"` via `OpenApiOperationTransformer`. Coordinate with the audit at T007
- [ ] T032 [US1] Verify `src/Services/Sorcha.ApiGateway/Program.cs` `AddSorchaCors()` permits `GET /.well-known/openapi.{json,yaml}` from any origin (anonymous access per FR-001/FR-002)

---

## Phase 4: User Story 2 — MCP discovery + 36-tool description audit (Priority: P1)

**Goal**: `GET /.well-known/mcp.json` returns a complete manifest. All 36 MCP tools have ≥ 2-sentence descriptions naming a disambiguating situation. `docs/mcp-server.md` exists.

**Independent Test**: An MCP-aware agent given only `/.well-known/mcp.json` connects, authenticates, and drives `walkthroughs/TradeFinance/` end-to-end via MCP tool calls.

### Tests for US2

- [ ] T033 [P] [US2] Add integration tests to `tests/Sorcha.Gateway.Integration.Tests/McpManifestWellKnownTests.cs`: `GET_WellKnownMcpJson_Returns200`, `Manifest_ContainsRequiredFields`, `Manifest_VersionMatchesAssemblyVersion`, `Manifest_TransportsIncludeStdioAndHttpSse`
- [ ] T034 [P] [US2] Add unit tests to `tests/Sorcha.McpServer.Tests/ToolDescriptionAuditTests.cs`: `EveryTool_DescriptionIsAtLeastTwoSentences` (reflection over `Sorcha.McpServer.Tools.*`), `EveryTool_DescriptionMentionsDisambiguatingSituation` (heuristic substring match)
- [ ] T035 [P] [US2] Add JSON-schema validation step to `scripts/check-discoverability.sh` that validates `/.well-known/mcp.json` against `specs/117-ai-discoverability/contracts/mcp-manifest.schema.json`

### Implementation for US2

- [ ] T036 [US2] Confirm `specs/117-ai-discoverability/contracts/mcp-manifest.schema.json` exists with required fields per FR-013 (already created during Phase 1 design — verify only)
- [ ] T037 [US2] Create `src/Services/Sorcha.ApiGateway/Discoverability/McpManifestEndpoint.cs` with a `GetMcpManifest` handler reading from `IOptions<McpManifestOptions>` plus the live tool count derived at startup
- [ ] T038 [US2] Add `McpManifest` configuration section to `src/Services/Sorcha.ApiGateway/appsettings.json` with default values for transports, JWT issuer, audience, and per-category descriptions
- [ ] T039 [US2] Wire `MapGet("/.well-known/mcp.json", McpManifestEndpoint.GetMcpManifest)` in `src/Services/Sorcha.ApiGateway/Program.cs` with `Cache-Control: public, max-age=300`, anonymous access
- [ ] T040 [US2] Create `src/Services/Sorcha.ApiGateway/Discoverability/McpToolCatalogueEndpoint.cs` serving `GET /api/mcp/tools` with the full tool listing (name, category, description, parameter schema link)
- [ ] T041 [US2] Create `src/Services/Sorcha.ApiGateway/Discoverability/Models/McpManifest.cs` (record with `$schema`, `name`, `version`, `description`, `transports`, `authentication`, `tool_categories`, `tool_catalogue_url`, `documentation_url` properties)
- [ ] T042 [US2] Create `src/Services/Sorcha.ApiGateway/Discoverability/Models/McpTransport.cs`, `McpAuthentication.cs`, `McpToolCategory.cs` records per `data-model.md`

**Tone authored against `docs/strategic-context.md`** for T043–T078: each tool description ≥ 2 sentences, names *what the tool does* and *when an AI agent should call it*, references the data the tool produces or consumes (signed transactions, schema-validated payloads, register entries) so agents can reason about fit. Examples drawn from the worked TradeFinance transcript at T079.

#### Admin tools (13)

- [ ] T043 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Admin/AuditQueryTool.cs`
- [ ] T044 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Admin/HealthCheckTool.cs`
- [ ] T045 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Admin/LogQueryTool.cs`
- [ ] T046 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Admin/MetricsTool.cs`
- [ ] T047 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Admin/PeerStatusTool.cs`
- [ ] T048 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Admin/RegisterStatsTool.cs`
- [ ] T049 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Admin/TenantListTool.cs`
- [ ] T050 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Admin/UserListTool.cs`
- [ ] T051 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Admin/ValidatorStatusTool.cs`
- [ ] T052 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Admin/TenantCreateTool.cs`
- [ ] T053 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Admin/TenantUpdateTool.cs`
- [ ] T054 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Admin/TokenRevokeTool.cs`
- [ ] T055 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Admin/UserManageTool.cs`

#### Designer tools (13)

- [ ] T056 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Designer/BlueprintCreateTool.cs`
- [ ] T057 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Designer/BlueprintGetTool.cs`
- [ ] T058 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Designer/BlueprintListTool.cs`
- [ ] T059 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Designer/JsonLogicTestTool.cs`
- [ ] T060 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Designer/SchemaGenerateTool.cs`
- [ ] T061 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Designer/SchemaValidateTool.cs`
- [ ] T062 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Designer/BlueprintDiffTool.cs`
- [ ] T063 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Designer/BlueprintExportTool.cs`
- [ ] T064 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Designer/BlueprintSimulateTool.cs`
- [ ] T065 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Designer/BlueprintUpdateTool.cs`
- [ ] T066 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Designer/BlueprintValidateTool.cs`
- [ ] T067 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Designer/DisclosureAnalysisTool.cs`
- [ ] T068 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Designer/WorkflowInstancesTool.cs`

#### Participant tools (10)

- [ ] T069 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Participant/ActionValidateTool.cs`
- [ ] T070 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Participant/InboxListTool.cs`
- [ ] T071 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Participant/TransactionHistoryTool.cs`
- [ ] T072 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Participant/ActionDetailsTool.cs`
- [ ] T073 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Participant/ActionSubmitTool.cs`
- [ ] T074 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Participant/DisclosedDataTool.cs`
- [ ] T075 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Participant/RegisterQueryTool.cs`
- [ ] T076 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Participant/WalletInfoTool.cs`
- [ ] T077 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Participant/WalletSignTool.cs`
- [ ] T078 [P] [US2] Rewrite `[Description]` on `src/Apps/Sorcha.McpServer/Tools/Participant/WorkflowStatusTool.cs`

#### Documentation + topic

- [ ] T079 [US2] Create `docs/mcp-server.md`. **Tone authored against `docs/strategic-context.md`** — open with one paragraph framing what an AI agent gets from the Sorcha MCP server. Sections: Overview · Connecting (stdio + http+sse with command snippets) · Authentication (JWT acquisition flow referencing `scripts/get-jwt-token.sh`) · Role slices (admin / designer / participant) · Worked example (capture an actual transcript driving `walkthroughs/TradeFinance/` from start to finish via MCP)
- [ ] T080 [US2] Add the `mcp-server` GitHub topic to the Sorcha-Platform/Sorcha repository (manual step — record completion in PR description; CI cannot enforce)

---

## Phase 5: User Story 3 — `llms.txt` and project summary (Priority: P1)

**Goal**: `llms.txt` exists at the repo root, is factual, follows [llmstxt.org](https://llmstxt.org) structure, and cross-references `STANDARDS.md`.

**Independent Test**: An LLM agent given only `https://<host>/llms.txt` produces an accurate one-sentence summary of Sorcha that mentions verifiable credentials, multi-party workflows, and the implemented standards.

### Tests for US3

- [ ] T081 [P] [US3] Add `llms.txt` structure check to `scripts/check-discoverability.sh`: file exists at repo root, size ≤ 8192 bytes, exactly one H1, exactly one blockquote, sections `## Capabilities`, `## Standards`, `## Links` present
- [ ] T082 [P] [US3] Add marketing-adjective deny-list scan to `scripts/check-discoverability.sh` covering `llms.txt`, `docs/llms-full.txt`, `STANDARDS.md`, served `/.well-known/openapi.json`, served `/.well-known/mcp.json`

### Implementation for US3

- [ ] T083 [US3] Create `llms.txt` at the repo root following `specs/117-ai-discoverability/contracts/llms-txt-template.md`. **Authored against `docs/strategic-context.md`** — blockquote summary uses the strategic frame ("Cryptographic proof infrastructure for multi-party workflows…"). Capabilities list draws from `STANDARDS.md` and the strategic-context Architecture and Cryptographic Posture sections. No marketing adjectives
- [ ] T084 [US3] Create `docs/llms-full.txt`, ≤ 32 KB. **Authored against `docs/strategic-context.md`** — opens with the strategic frame (problem, AI fraud + AI decision-maker context, regulatory pull). Sections: Architecture summary · Quickstart pointer · MCP integration pointer · Security model summary with honest gaps named (HAIP classical-only boundary, SLH-DSA not implemented, BBS+ not implemented) · How to integrate
- [ ] T085 [US3] Add cross-reference check to `scripts/check-discoverability.sh`: every line in `llms.txt` `## Standards` and `docs/llms-full.txt` standards section matches a row in `STANDARDS.md` with status `full` or `partial`, fail with single-line message naming any mismatch

---

## Phase 6: User Story 4 — `STANDARDS.md` (Priority: P2)

**Goal**: `STANDARDS.md` exists at the repo root, accurately lists every standard Sorcha implements, and is enforced fresh by PR checklist + CI.

**Independent Test**: A reviewer with no codebase familiarity reads `STANDARDS.md` end-to-end, picks any three rows at random, and verifies each by following the spec URL and the component path.

### Tests for US4

- [ ] T086 [P] [US4] Add `STANDARDS.md` structural parse check to `scripts/check-discoverability.sh`: file exists, contains a Markdown table with the seven required columns, every row's `Status` is `full|partial|planned`, every `Components` cell path resolves to a real path in the repo

### Implementation for US4

- [ ] T087 [US4] Create `STANDARDS.md` at the repo root following `specs/117-ai-discoverability/contracts/standards-md-template.md`. **Intro paragraph authored against `docs/strategic-context.md` Cryptographic Posture section** — name what is core (ML-DSA FIPS 204, ML-KEM FIPS 203, BIP32/39/44, JSON Pointer selective disclosure, Merkle dockets) and the honest gaps (HAIP classical-only at boundary, SLH-DSA not yet implemented, BBS+ not yet implemented). Then the initial table with rows for BIP32, BIP39, BIP44, ML-DSA, OpenID4VCI, OpenID4VP, HAIP 1.0, W3C VCDM 2.0, IETF Token Status List 2024, W3C Bitstring Status List, ISO 18013-5 (`planned`), DID 1.0, OAuth 2.0
- [ ] T088 [US4] Add `## Maintenance` section at the bottom of `STANDARDS.md` describing the PR checklist requirement, the CI parse check, and the cross-reference contract with `llms.txt` and `docs/` frontmatter
- [ ] T089 [US4] Edit `.github/pull_request_template.md` (create if absent) to add a `Standards & discoverability` section with checkboxes: `STANDARDS.md reviewed and updated for any standards-related change`, `last_updated bumped on changed docs/ files with frontmatter`, `llms.txt reviewed if a new standard or capability was added`

---

## Phase 7: User Story 5 — quickstart hardening (Priority: P2)

**Goal**: `scripts/sorcha-setup.sh` exits non-zero on any prerequisite failure with a remediation hint. `docker-compose.yml` carries a topology comment block. `docs/quickstart.md` exists with a verify-installation step.

**Independent Test**: A fresh `ubuntu-latest` runner with Docker Engine ≥ 24 and PowerShell 7.5 installed, given only the repo URL and `docs/quickstart.md`, completes setup and runs the verify-installation curl in under 15 minutes with no human input.

### Tests for US5

- [ ] T090 [P] [US5] Add nightly cron-triggered job `quickstart-on-clean-vm` to `.github/workflows/ai-discoverability-check.yml` that on `ubuntu-latest` clones the repo, runs `./scripts/sorcha-setup.sh`, waits for gateway readiness, runs the verify-installation curl from `docs/quickstart.md`, asserts HTTP 200
- [ ] T091 [P] [US5] Add bats-based unit tests for prerequisite check helpers in `tests/scripts/sorcha-setup.bats` (positive + negative path per check)

### Implementation for US5

- [ ] T092 [US5] Refactor `scripts/sorcha-setup.sh` per the audit at T010: add `set -euo pipefail` at top; extract every prerequisite check into a `check_<name>` function returning 0 on success and `[sorcha-setup] missing prerequisite: <name> (≥ <version>); install via <link>` with non-zero exit on failure; cover Docker installed, Docker daemon running, Docker Compose v2, ports 80/443/8080 free, PowerShell 7.5+ (warning), Git, OpenSSL; print `[sorcha-setup] success — gateway reachable at http://localhost. Verify with: curl -s http://localhost/api/health` on success
- [ ] T093 [US5] Add topology comment block to `docker-compose.yml` within the first 30 lines naming every service, port, and one-line purpose (`gateway :80 YARP API gateway`, `blueprint :5000 Workflow management, SignalR`, etc.)
- [ ] T094 [US5] Create `docs/quickstart.md` with sections: Prerequisites (every entry with version constraint and install link), Setup (`./scripts/sorcha-setup.sh` invocation), Common failures (table: symptom → fix), Verify your installation (`curl -s http://localhost/api/health` and the expected JSON shape)
- [ ] T095 [US5] Edit `README.md` quickstart section to link to `docs/quickstart.md` for detailed instructions
- [ ] T096 [US5] If `org-profile-README.md` exists at repo root or in a `.github` profile repo, edit its quickstart section to link to `docs/quickstart.md` (conditional task — file may not exist)

---

## Phase 8: User Story 6 — published technical documentation (Priority: P2)

**Goal**: Four documents under `docs/` carrying YAML frontmatter and accurately reflecting the implemented platform.

**Independent Test**: A technical reviewer with no prior Sorcha exposure reads the four documents in sequence and can answer (a) what Sorcha is, (b) how it integrates with HAIP wallets, (c) what domains it is applicable to, (d) what its known security trade-offs are, without other sources.

### Tests for US6

- [ ] T097 [P] [US6] Add structural check to `scripts/check-discoverability.sh`: each of the four documents exists at its required path, each has YAML frontmatter with `title`, `description`, `standards[]`, `last_updated` fields, every `standards[]` entry corresponds to a `STANDARDS.md` row with status `full` or `partial`, `last_updated` is a valid ISO date

### Implementation for US6

- [ ] T098 [US6] Create `docs/architecture.md` with frontmatter (`title: Sorcha Architecture`, `description`, `standards: [BIP32, BIP44, W3C VC Data Model 2.0, HAIP 1.0, OpenID4VCI, OpenID4VP]`, `last_updated`). **Tone authored against `docs/strategic-context.md`** — the "Architecture in One Paragraph" framing leads. Source: planning-folder `sorcha-architecture-narrative.md` (located at T009). Strip internal references; ensure every spec reference points at a public spec URL
- [ ] T099 [US6] Create `docs/openid4vc-haip-integration.md` with frontmatter (`standards: [OpenID4VCI, OpenID4VP, HAIP 1.0, W3C VC Data Model 2.0, IETF Token Status List 2024 (RFC 9972), ML-DSA (FIPS 204)]`). **Tone authored against `docs/strategic-context.md`** — Sorcha is the workflow layer above GOV.UK Wallet / EUDIW; it does NOT replace those wallets and does NOT control the citizen experience. Source: planning-folder `sorcha-openid4vc-mdl-integration.md`
- [ ] T100 [US6] Create `docs/applicability.md` with frontmatter (`standards: [W3C VC Data Model 2.0, OpenID4VCI, OpenID4VP, HAIP 1.0]`). **Tone authored against `docs/strategic-context.md` Target Markets section** — lead with regulatory pull (EU ESPR / DPP, HAIP / EUDI / GOV.UK Wallet, EU AI Act, SME trade finance) before the technology. Source: planning-folder `sorcha-applicability.md`. Cover at minimum DPP, trade finance, IPC-1782, municipal governance with one worked example each
- [ ] T101 [US6] Create `docs/security-model.md` with frontmatter (`standards: [ML-DSA (FIPS 204), HAIP 1.0, W3C VC Data Model 2.0, OAuth 2.0]`). **Tone authored against `docs/strategic-context.md` Cryptographic Posture section**. Sections: Selective disclosure (architectural not policy) · Aggregate inference threat · Post-quantum posture (internal PQC, classical wire boundary at HAIP) · Honest gaps (SLH-DSA not implemented; BBS+ not implemented) · mTLS gap (named explicitly) · Trust anchor model (system register genesis, spec 099). Source: planning-folder `sorcha-architecture-evaluation.md` sections 4 and 7

---

## Phase 9: Polish & cross-cutting

- [ ] T102 Create `.github/workflows/ai-discoverability-check.yml` triggered on `pull_request` to master, running `scripts/check-discoverability.sh` after booting the gateway via `docker compose up -d` and waiting for `/api/health` to return 200; on failure, post a PR comment with the failing check and a link to the workflow run
- [ ] T103 Mark `ai-discoverability-check` job as required in the `master` branch protection ruleset (manual GitHub UI step — record in PR description)
- [ ] T104 [P] Update `docs/README.md` to list new published documents: `architecture.md`, `openid4vc-haip-integration.md`, `applicability.md`, `security-model.md`, `quickstart.md`, `mcp-server.md`, `llms-full.txt`
- [ ] T105 [P] Update `README.md` root with a new "For AI agents and integrators" section linking to `llms.txt`, `STANDARDS.md`, `docs/quickstart.md`, `/.well-known/openapi.json`, `/.well-known/mcp.json`
- [ ] T106 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` with a pointer section for AI Discoverability Surface listing the well-known endpoints, `llms.txt`, `STANDARDS.md`, and the four published documents
- [ ] T107 Run `./scripts/check-discoverability.sh` end-to-end against a freshly-built gateway locally; confirm zero failures
- [ ] T108 Run the quickstart against a clean Docker desktop locally; confirm gateway reachable, `/api/health` returns 200, the verify-installation curl prints the expected JSON

---

## Dependencies

- Phase 1 (Setup) → Phase 2 (Foundational) → Phases 3–8 in any order, but P1 phases (3, 4, 5) should land before P2 phases (6, 7, 8)
- US1 (OpenAPI) feeds US3 (`llms.txt` Links section) and US6 (frontmatter `standards[]` cross-reference)
- US2 (MCP) feeds US3 (`llms.txt` Links section)
- **US4 (`STANDARDS.md`) is a prerequisite for US3 cross-reference (T085) and US6 frontmatter check (T097)** — schedule US4 ahead of those checks landing
- Phase 9 (Polish & CI) depends on every earlier phase — the workflow's checks reference all earlier artefacts

## Parallel opportunities

- T003 / T004 / T005 — independent setup
- T007 / T008 / T009 / T010 / T011 — independent audits
- T012 / T013 / T014 — independent test infrastructure
- T020–T027 — endpoint annotation passes across services (each service is a different file tree)
- T033 / T034 / T035 — independent test infrastructure for US2
- T043–T078 — all 36 MCP tool description rewrites (independent files)
- T081 / T082 — independent CI checks
- T086 — single check
- T090 / T091 — independent test infrastructure
- T097 — single check
- T104 / T105 / T106 — independent doc updates

## Implementation strategy

**Recommended sequencing**:

1. Phase 1 + Phase 2 first (sets the lint and audit baseline).
2. **Phase 6 (`STANDARDS.md`) before Phase 5 / 8** — US3 (`llms.txt`) and US6 (frontmatter) cross-reference it. Landing it early unblocks both.
3. Phase 3 (US1 OpenAPI) and Phase 4 (US2 MCP) in parallel branches — independent code surfaces.
4. Phase 5 (US3 `llms.txt`) once US4 is landed.
5. Phase 7 (US5 quickstart) and Phase 8 (US6 docs) in parallel branches — independent.
6. Phase 9 (Polish & CI) last — the CI workflow gates all earlier artefacts.

**Suggested MVP scope**: Phase 1 + Phase 2 + Phase 3 (US1 OpenAPI surface). 33 tasks. Ships the minimal AI-discoverability story: an agent can find, parse, and reason over the API surface. Subsequent phases extend the surface but do not block US1's value.

---

## Task summary

| Phase | Range | Count |
|---|---|---|
| Phase 1 Setup | T001–T005 | 5 |
| Phase 2 Foundational audits | T006–T011 | 6 |
| Phase 3 US1 OpenAPI surface | T012–T032 | 21 |
| Phase 4 US2 MCP discovery + 36 tool rewrites + docs | T033–T080 | 48 |
| Phase 5 US3 `llms.txt` | T081–T085 | 5 |
| Phase 6 US4 `STANDARDS.md` | T086–T089 | 4 |
| Phase 7 US5 quickstart hardening | T090–T096 | 7 |
| Phase 8 US6 published docs | T097–T101 | 5 |
| Phase 9 Polish & CI | T102–T108 | 7 |
| **Total** | T001–T108 | **108** |

## Format validation

Every task in this file follows the strict checklist format `- [ ] T### [P?] [US?] description with file path`. Setup, foundational, and polish phases carry no `[US]` label. User-story phases (3–8) carry the corresponding `[US1]`–`[US6]` label. `[P]` is present only where the task targets a different file from neighbouring tasks and has no incomplete dependency.
