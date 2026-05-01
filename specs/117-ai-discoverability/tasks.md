---
description: "Task list for spec 117: AI Discoverability & Machine-Readable Marketing"
---

# Tasks: AI Discoverability & Machine-Readable Marketing

**Input**: Design documents from `specs/117-ai-discoverability/`
**Prerequisites**: spec.md

**Tests**: Included where the artefact is verifiable in code (OpenAPI lint, MCP tool description audit, standards cross-reference). Documentation tasks ship without unit tests but are gated by the CI workflow at FR-044.

---

## Phase 1: Setup

- [ ] T001 Confirm `117-ai-discoverability` branch is rebased onto master with no merge conflicts
- [ ] T002 Confirm `Microsoft.AspNetCore.OpenApi` is referenced from `src/Services/Sorcha.ApiGateway/Sorcha.ApiGateway.csproj` (.NET 10 built-in — package reference may be implicit through framework metapackage)
- [ ] T003 [P] Add `package.json` dev dependencies for `@stoplight/spectral-cli` and `swagger-cli` at the repo root, or confirm they are available in the discoverability CI workflow image without local install
- [ ] T004 [P] Add `.spectral.yaml` at the repo root carrying the project's OpenAPI lint ruleset: extends `spectral:oas`, plus custom rules for `operationId-pascalcase`, `description-required-on-properties`, `examples-required-on-credential-issuance`, `info-x-mcp-server-required`, `info-x-standards-required`, `no-marketing-adjectives` (deny-list `revolutionary`, `best-in-class`, `industry-leading`, `cutting-edge`, `world-class`, `seamless`)
- [ ] T005 Add `scripts/check-discoverability.sh` — orchestrator script the CI workflow runs locally and in CI. It runs spectral lint, swagger-cli validate, mcp-manifest schema validation, llms.txt structure check, STANDARDS.md parse, and standards cross-reference. Each step exits non-zero on failure with a single-line message naming the offending file and reason

## Phase 2: Foundational — audit current state

- [ ] T006 Baseline: build the gateway with `dotnet build src/Services/Sorcha.ApiGateway/`, run it, capture the OpenAPI document currently emitted by `app.MapOpenApi()` at `http://localhost/openapi/v1.json`. Save to `specs/117-ai-discoverability/baseline-openapi.json` for diff review (do not commit — gitignore)
- [ ] T007 [P] Read every endpoint registration across `src/Services/Sorcha.ApiGateway/Program.cs` and the gateway-routed services (Blueprint, Wallet, Register, Tenant, Peer, Validator, HAIP). Produce `specs/117-ai-discoverability/endpoint-audit.md` listing endpoints missing `WithName`, `WithSummary`, `WithDescription`, or `WithTags`
- [ ] T008 [P] Walk every tool under `src/Apps/Sorcha.McpServer/Tools/{Admin,Designer,Participant}/`. For each, capture the current `[Description("...")]` attribute (or equivalent). Produce `specs/117-ai-discoverability/mcp-tool-audit.md` flagging any tool whose description is < 2 sentences or lacks a disambiguating situation
- [ ] T009 [P] Inventory the planning-folder narratives required by US6 — locate `sorcha-architecture-narrative.md`, `sorcha-openid4vc-mdl-integration.md`, `sorcha-applicability.md`, `sorcha-architecture-evaluation.md`. Confirm presence and authorial readiness; record locations in `specs/117-ai-discoverability/source-narratives.md`. If any are missing, surface as a blocker before phase 8 begins
- [ ] T010 Read `scripts/sorcha-setup.sh` end-to-end. Produce `specs/117-ai-discoverability/setup-script-audit.md` listing every prerequisite check, every silent-failure path, and every place that lacks an exit code or error message
- [ ] T011 Read `docker-compose.yml` first 30 lines. Confirm whether a topology comment block is present; record findings in `specs/117-ai-discoverability/compose-audit.md`

## Phase 3: User Story 1 — OpenAPI 3.1 surface (Priority: P1)

**Goal**: `GET /.well-known/openapi.json` returns a complete, valid, lint-clean OpenAPI 3.1 document with every endpoint and schema property fully annotated.

### Tests for US1

- [ ] T012 [P] [US1] Add Spectral rule unit tests under `.spectral.tests/` exercising the custom rules (`operationId-pascalcase`, `description-required-on-properties`, etc.) against synthetic OpenAPI fragments
- [ ] T013 [P] [US1] Add an integration test in `tests/Sorcha.Gateway.Integration.Tests/OpenApiWellKnownTests.cs`:
  - `GET_WellKnownOpenapiJson_Returns200` — asserts route exists and returns `application/json`
  - `GET_WellKnownOpenapiYaml_Returns200` — asserts the YAML alias works and returns `application/yaml`
  - `OpenApiDocument_ContainsXMcpServer` — asserts `info.x-mcp-server` is set to the expected URL
  - `OpenApiDocument_ContainsXStandards` — asserts `info.x-standards` is a non-empty array of strings
  - `OpenApiDocument_VersionMatchesAssemblyVersion` — asserts `info.version` matches the assembly informational version
- [ ] T014 [P] [US1] Add a CI step in `.github/workflows/ai-discoverability-check.yml` that runs `spectral lint <served-openapi-url>` against the gateway booted in the workflow and fails on any violation

### Implementation

- [ ] T015 [US1] Add the `/.well-known/openapi.json` route in `src/Services/Sorcha.ApiGateway/Program.cs` immediately after the existing `app.MapOpenApi()` line. The handler should fetch the same OpenAPI document the existing endpoint serves and return it under the well-known path with `Cache-Control: public, max-age=300`
- [ ] T016 [US1] Add the `/.well-known/openapi.yaml` route. Convert the JSON document to YAML using `YamlDotNet` (already a transitive dependency) or `System.Text.Json` → `Yaml` adapter. Set `Content-Type: application/yaml`
- [ ] T017 [US1] Extend `AddSorchaOpenApi` (in the shared service-defaults extension under `src/Common/Sorcha.ServiceDefaults/`) to add an `OpenApiDocumentTransformer` that injects `info.x-mcp-server`, `info.x-standards`, and `info.version` from a single canonical version source (assembly informational version)
- [ ] T018 [US1] Walk every endpoint in `src/Services/Sorcha.ApiGateway/Program.cs` and add missing `.WithName(...)`, `.WithSummary(...)`, `.WithDescription(...)`, `.WithTags(...)` per the audit at T007. Use PascalCase `<Resource><Verb>` `operationId` convention
- [ ] T019 [US1] [P] Walk every endpoint in `src/Services/Sorcha.Blueprint.Service/Endpoints/` and add the same metadata
- [ ] T020 [US1] [P] Walk every endpoint in `src/Services/Sorcha.Wallet.Service/Endpoints/` and add the same metadata. Pay special attention to `CredentialEndpoints.IssueCredential` — FR-006 requires it to carry an example
- [ ] T021 [US1] [P] Walk every endpoint in `src/Services/Sorcha.Register.Service/Endpoints/` and add the same metadata
- [ ] T022 [US1] [P] Walk every endpoint in `src/Services/Sorcha.Tenant.Service/Endpoints/` and add the same metadata
- [ ] T023 [US1] [P] Walk every endpoint in `src/Services/Sorcha.Peer.Service/Endpoints/` and add the same metadata
- [ ] T024 [US1] [P] Walk every endpoint in `src/Services/Sorcha.Validator.Service/Endpoints/` and add the same metadata
- [ ] T025 [US1] [P] Walk every endpoint in `src/Services/Sorcha.Haip.Service/Endpoints/` and add the same metadata
- [ ] T026 [US1] Walk every request/response DTO and schema model under `src/Common/Sorcha.ServiceClients.Http/`, `src/Services/*/Models/`, and the wallet/register service request shapes. Add `[Description("...")]` to every property that lacks one. Properties documented via XML doc comments instead of `[Description]` are equivalent — confirm `<GenerateDocumentationFile>true</GenerateDocumentationFile>` is set in the relevant csproj files so XML comments flow into the OpenAPI document
- [ ] T027 [US1] Add an example request and response for `CredentialEndpoints.IssueCredential` via `[OpenApiExample]` attribute or `OpenApiOperationTransformer`. Example request body: trade-finance invoice credential (drawn from `walkthroughs/TradeFinance/`). Example response: a signed SD-JWT VC envelope shape
- [ ] T028 [US1] Add an example request and response for the wallet signing endpoint (`/api/v1/wallets/{address}/sign-transaction` or equivalent). Example: an action payload signature
- [ ] T029 [US1] Mark any endpoint whose specification is incomplete or whose behaviour is not yet stable with `.WithMetadata(new OpenApiExtensionAttribute("x-status", "partial"))` or equivalent transformer. Coordinate with T007 audit
- [ ] T030 [US1] Verify the gateway's CORS policy permits `GET /.well-known/openapi.json` and `GET /.well-known/openapi.yaml` from any origin. The well-known surface must be anonymously accessible per FR-001/FR-002

## Phase 4: User Story 2 — MCP discovery and tool catalogue (Priority: P1)

**Goal**: `GET /.well-known/mcp.json` returns a complete manifest. All 36 MCP tools have ≥ 2-sentence descriptions naming a disambiguating situation. `docs/mcp-server.md` exists.

### Tests for US2

- [ ] T031 [P] [US2] Add an integration test in `tests/Sorcha.Gateway.Integration.Tests/McpManifestWellKnownTests.cs`:
  - `GET_WellKnownMcpJson_Returns200`
  - `Manifest_ContainsRequiredFields` — asserts `name`, `version`, `description`, `transports`, `authentication`, `tool_categories`, `tool_catalogue_url`, `documentation_url` all present
  - `Manifest_VersionMatchesAssemblyVersion`
  - `Manifest_TransportsIncludeStdioAndHttpSse`
- [ ] T032 [P] [US2] Add a unit test in `tests/Sorcha.McpServer.Tests/ToolDescriptionAuditTests.cs`:
  - `EveryTool_DescriptionIsAtLeastTwoSentences` — uses reflection to enumerate all tool classes under `Sorcha.McpServer.Tools.*`, reads their `[Description]` attribute, asserts ≥ 2 sentences
  - `EveryTool_DescriptionMentionsDisambiguatingSituation` — heuristic: description contains one of the substrings `"call this when"`, `"use when"`, `"prefer this when"`, `"not when"`, `"versus"`, or an equivalent disambiguator. The set of allowed disambiguators is configurable in the test via a small constant array; the audit at T008 may surface additional acceptable forms
- [ ] T033 [P] [US2] Add a JSON-schema validation step to `scripts/check-discoverability.sh` that validates the served `/.well-known/mcp.json` against `specs/117-ai-discoverability/contracts/mcp-manifest.schema.json` (committed in this spec)

### Implementation

- [ ] T034 [US2] Create `specs/117-ai-discoverability/contracts/mcp-manifest.schema.json` — a JSON Schema describing the manifest shape per FR-013. Required fields: `name`, `version`, `description`, `transports`, `authentication`, `tool_categories`, `tool_catalogue_url`, `documentation_url`. Reference it from the manifest's `$schema` field
- [ ] T035 [US2] Create `src/Services/Sorcha.ApiGateway/Endpoints/McpManifestEndpoint.cs` with a static `GetMcpManifest` handler. It builds the manifest from a configuration source (`McpManifest` section in `appsettings.json`) plus the live tool count derived from the running MCP server (read once at startup; not per-request)
- [ ] T036 [US2] Add `McpManifest` configuration section to `src/Services/Sorcha.ApiGateway/appsettings.json` with default values for transports, authentication issuer, audience, and category descriptions. Production overrides happen via environment variables
- [ ] T037 [US2] Wire the route `MapGet("/.well-known/mcp.json", McpManifestEndpoint.GetMcpManifest)` in `Program.cs` with `Cache-Control: public, max-age=300`. Anonymous access; no auth requirement
- [ ] T038 [US2] Create `src/Services/Sorcha.ApiGateway/Endpoints/McpToolCatalogueEndpoint.cs` serving `GET /api/mcp/tools` with the full tool listing (name, category, description, parameter schema link). The manifest's `tool_catalogue_url` points here
- [ ] T039 [US2] Bring all 36 MCP tools' descriptions up to the FR-017 standard. Per the T008 audit, edit each `[Description("...")]` attribute. Order by category:
  - **Admin (13 tools)**: T039a `AuditQueryTool.cs`, T039b `HealthCheckTool.cs`, T039c `LogQueryTool.cs`, T039d `MetricsTool.cs`, T039e `PeerStatusTool.cs`, T039f `RegisterStatsTool.cs`, T039g `TenantListTool.cs`, T039h `UserListTool.cs`, T039i `ValidatorStatusTool.cs`, T039j `TenantCreateTool.cs`, T039k `TenantUpdateTool.cs`, T039l `TokenRevokeTool.cs`, T039m `UserManageTool.cs`
  - **Designer (13 tools)**: T039n `BlueprintCreateTool.cs`, T039o `BlueprintGetTool.cs`, T039p `BlueprintListTool.cs`, T039q `JsonLogicTestTool.cs`, T039r `SchemaGenerateTool.cs`, T039s `SchemaValidateTool.cs`, T039t `BlueprintDiffTool.cs`, T039u `BlueprintExportTool.cs`, T039v `BlueprintSimulateTool.cs`, T039w `BlueprintUpdateTool.cs`, T039x `BlueprintValidateTool.cs`, T039y `DisclosureAnalysisTool.cs`, T039z `WorkflowInstancesTool.cs`
  - **Participant (10 tools)**: T039aa `ActionValidateTool.cs`, T039ab `InboxListTool.cs`, T039ac `TransactionHistoryTool.cs`, T039ad `ActionDetailsTool.cs`, T039ae `ActionSubmitTool.cs`, T039af `DisclosedDataTool.cs`, T039ag `RegisterQueryTool.cs`, T039ah `WalletInfoTool.cs`, T039ai `WalletSignTool.cs`, T039aj `WorkflowStatusTool.cs`
  Each tool's revised description must be ≥ 2 sentences and include a disambiguating clue. Examples can be drawn from the worked TradeFinance walkthrough at T040
- [ ] T040 [US2] Create `docs/mcp-server.md`. Sections: Overview · Connecting (stdio + http+sse with command snippets) · Authentication (JWT acquisition flow with the existing `scripts/get-jwt-token.sh` referenced) · Role slices (admin / designer / participant — when to use which) · Worked example (an agent transcript driving `walkthroughs/TradeFinance/` from start to finish — capture an actual transcript by running an MCP-aware agent against the walkthrough)
- [ ] T041 [US2] Add the `mcp-server` GitHub topic to the repository (manual step — note in the PR description that the maintainer must add the topic via the GitHub UI; the spec's CI cannot enforce this)

## Phase 5: User Story 3 — `llms.txt` and project summary (Priority: P1)

**Goal**: `llms.txt` exists at the repo root, is factual, follows the [llmstxt.org](https://llmstxt.org) structure, and cross-references `STANDARDS.md`.

### Tests for US3

- [ ] T042 [P] [US3] Add `tests/discoverability-lint/llms-txt-structure.test.js` (or a Bash test in `scripts/check-discoverability.sh`):
  - File exists at repo root
  - Size ≤ 8 KB
  - Contains exactly one H1
  - Contains a blockquote summary paragraph
  - Contains sections `## Capabilities`, `## Standards`, `## Links`
- [ ] T043 [P] [US3] Add a marketing-adjective deny-list scan to `scripts/check-discoverability.sh`. Deny-list: `revolutionary`, `best-in-class`, `industry-leading`, `cutting-edge`, `world-class`, `seamless`. Fails on case-insensitive match in any of `llms.txt`, `docs/llms-full.txt`, `STANDARDS.md`, served `/.well-known/openapi.json`, served `/.well-known/mcp.json`

### Implementation

- [ ] T044 [US3] Create `llms.txt` at the repo root. Structure:
  ```
  # Sorcha
  > <one-paragraph factual summary>
  ## Capabilities
  - <one-line per capability>
  ## Standards
  - <name>: <stable URL>
  ## Links
  - OpenAPI: <full URL of /.well-known/openapi.json on the canonical host>
  - MCP manifest: <full URL of /.well-known/mcp.json>
  - Quickstart: <repo URL>/blob/master/docs/quickstart.md
  - Architecture: <repo URL>/blob/master/docs/architecture.md
  - STANDARDS.md: <repo URL>/blob/master/STANDARDS.md
  ```
  Capabilities list draws from `STANDARDS.md` and `docs/architecture.md`. Standards list mirrors `STANDARDS.md` rows with status `full` or `partial` (cross-reference enforced by CI at T056)
- [ ] T045 [US3] Create `docs/llms-full.txt`. ≤ 32 KB. Sections: Architecture summary · Quickstart pointer · MCP integration pointer · Security model summary · How to integrate (links into the four published docs from US6). Source content from the four `docs/` documents at US6; `llms-full.txt` is a digest, not a duplicate
- [ ] T046 [US3] Add a CI cross-reference check in `scripts/check-discoverability.sh`: every line in `llms.txt` `## Standards` and `docs/llms-full.txt` standards section must match a row in `STANDARDS.md` whose status is `full` or `partial`. Fails on any mismatch with a single-line message naming the missing standard

## Phase 6: User Story 4 — `STANDARDS.md` (Priority: P2)

**Goal**: `STANDARDS.md` exists at the repo root, accurately lists every standard Sorcha implements, and is enforced fresh by PR checklist + CI.

### Tests for US4

- [ ] T047 [P] [US4] Add a structural parse check to `scripts/check-discoverability.sh`: `STANDARDS.md` exists, contains a Markdown table with the seven required columns (name, version, body, spec-url, components, status, notes), every row's status is one of `full|partial|planned`, every component path resolves to a real path in the repo
- [ ] T048 [P] [US4] Add a PR-template field check (manual review, not CI) — when a PR touches a path listed in any `components` cell of `STANDARDS.md`, the PR checklist `STANDARDS.md reviewed` box must be checked

### Implementation

- [ ] T049 [US4] Create `STANDARDS.md` at the repo root. Initial table content (each row: name | version | body | spec URL | component path(s) | status | notes):
  - **BIP32** | 2017 | Bitcoin Improvement Proposals | [BIP-0032](https://github.com/bitcoin/bips/blob/master/bip-0032.mediawiki) | `src/Core/Sorcha.Wallet.Core/Services/Implementation/KeyManagementService.cs` | full | NBitcoin-backed; BIP32 path-style derivation across all wallets
  - **BIP39** | 2013 | Bitcoin Improvement Proposals | [BIP-0039](https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki) | `src/Core/Sorcha.Wallet.Portable/Domain/ValueObjects/Mnemonic.cs` | full | English wordlist only
  - **BIP44** | 2014 | Bitcoin Improvement Proposals | [BIP-0044](https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki) | `src/Core/Sorcha.Wallet.Portable/Constants/SorchaDerivationPaths.cs` | full | Sorcha-specific purpose namespace per slot
  - **ML-DSA (FIPS 204)** | 2024 | NIST | [FIPS 204](https://csrc.nist.gov/pubs/fips/204/final) | `src/Common/Sorcha.Cryptography/Pqc/` | full | Internal signing path; HAIP wire boundary remains classical per HAIP 1.0
  - **OpenID4VCI** | Draft 14 | OpenID Foundation | [OpenID4VCI](https://openid.net/specs/openid-4-verifiable-credential-issuance-1_0.html) | `src/Services/Sorcha.Haip.Service/` | partial | Issuer endpoint per spec 097; ongoing hardening
  - **OpenID4VP** | Draft 21 | OpenID Foundation | [OpenID4VP](https://openid.net/specs/openid-4-verifiable-presentations-1_0.html) | `src/Services/Sorcha.Haip.Service/` | partial | Verifier endpoint per spec 098
  - **HAIP 1.0** | 2025-12 | OpenID Foundation | [HAIP 1.0](https://openid.net/specs/openid4vc-high-assurance-interoperability-profile-1_0.html) | `src/Services/Sorcha.Haip.Service/` | partial | Wire boundary classical-only per spec; PQC is internal
  - **W3C VC Data Model 2.0** | 2025 | W3C | [VC Data Model 2.0](https://www.w3.org/TR/vc-data-model-2.0/) | `src/Common/Sorcha.Cryptography/SdJwt/`, `src/Services/Sorcha.Wallet.Service/` | full | SD-JWT VC profile; classical embedding for wire compatibility
  - **IETF Token Status List 2024 (RFC 9972)** | 2024 | IETF | [RFC 9972](https://datatracker.ietf.org/doc/html/rfc9972) | `src/Services/Sorcha.Wallet.Service/Services/Implementation/CitizenStatusListPublisher.cs`, `src/Services/Sorcha.Blueprint.Service/Services/StatusListManager.cs` | full | Per spec 095; W3C and IETF envelopes back the same bitstring
  - **W3C Bitstring Status List** | 2024 | W3C | [Bitstring Status List](https://www.w3.org/TR/vc-bitstring-status-list/) | `src/Services/Sorcha.Blueprint.Service/Services/StatusListManager.cs` | full | Internal-path issuance default
  - **ISO 18013-5 (mdoc)** | 2021 | ISO/IEC | [ISO 18013-5](https://www.iso.org/standard/69084.html) | n/a | planned | Roadmap; not yet implemented
  - **DID (W3C)** | 1.0 | W3C | [DID 1.0](https://www.w3.org/TR/did-core/) | `src/Common/Sorcha.Cryptography/`, `src/Services/Sorcha.Wallet.Service/` | partial | `did:sorcha:org:` and `did:sorcha:holder:` types; no DID resolution registry adoption
  - **OAuth 2.0** | 2012 | IETF (RFC 6749) | [RFC 6749](https://datatracker.ietf.org/doc/html/rfc6749) | `src/Services/Sorcha.Tenant.Service/` | full | Used as the JWT issuer
- [ ] T050 [US4] Add a `## Maintenance` section at the bottom of `STANDARDS.md` describing the PR checklist requirement, the CI parse check, and the cross-reference contract with `llms.txt` and the `docs/` frontmatter
- [ ] T051 [US4] Edit `.github/pull_request_template.md` (create if absent) to add a `Standards & discoverability` section with checkboxes:
  - [ ] `STANDARDS.md` reviewed and updated for any standards-related change
  - [ ] `last_updated` bumped on changed `docs/` files with frontmatter
  - [ ] `llms.txt` reviewed if a new standard or capability was added

## Phase 7: User Story 5 — quickstart hardening (Priority: P2)

**Goal**: `scripts/sorcha-setup.sh` exits non-zero on any prerequisite failure with a remediation hint. `docker-compose.yml` carries a topology comment block. `docs/quickstart.md` exists with a verify-installation step.

### Tests for US5

- [ ] T052 [P] [US5] Add a CI job `quickstart-on-clean-vm` that runs nightly on `ubuntu-latest`:
  - clones the repo
  - runs `./scripts/sorcha-setup.sh`
  - waits for gateway readiness
  - runs the verify-installation curl from `docs/quickstart.md` and asserts HTTP 200 with the expected JSON shape
  - fails the build (and notifies via PR comment / Slack hook if configured) on any step failure
- [ ] T053 [P] [US5] Add unit tests for the prerequisite check helpers in `scripts/sorcha-setup.sh` (using `bats` or equivalent shell test framework). Each prerequisite check has a positive and negative test path

### Implementation

- [ ] T054 [US5] Refactor `scripts/sorcha-setup.sh` per the T010 audit:
  - At top of file, set `set -euo pipefail`
  - Extract every prerequisite check into a `check_<name>` function returning 0 on success and printing `[sorcha-setup] missing prerequisite: <name> (≥ <version>); install via <link>` on failure with non-zero exit
  - Required checks: Docker installed, Docker daemon running, Docker Compose v2, ports 80/443/8080 free or remap option offered, PowerShell 7.5+ (warning-only, walkthroughs require it), Git, OpenSSL (cert generation)
  - At the end of the script, print `[sorcha-setup] success — gateway reachable at http://localhost. Verify with: curl -s http://localhost/api/health`
- [ ] T055 [US5] Add a topology comment block to `docker-compose.yml` within the first 30 lines:
  ```
  # Sorcha topology
  # ----------------
  # gateway        :80    YARP API gateway — single entry point
  # blueprint      :5000  Workflow management, SignalR
  # register       :5290  Distributed ledger, OData
  # wallet         :int   Crypto operations, HD wallets
  # tenant         :5110  Multi-tenant auth, JWT issuer
  # validator      :int   Consensus, chain integrity
  # peer           :5002  P2P network, gRPC
  # haip           :int   OpenID4VCI/OpenID4VP issuer/verifier
  # postgres       :5432  Wallet, register, blueprint persistence
  # mongodb        :27017 Register document storage
  # redis          :6379  Cache, sessions, distributed coordination
  # aspire-dash    :18888 Orchestration dashboard
  ```
  Update the comment block whenever services are added or ports change
- [ ] T056 [US5] Create `docs/quickstart.md`. Sections: Prerequisites (every entry with version constraint and install link) · Setup (single `./scripts/sorcha-setup.sh` invocation) · Common failures (table: symptom → fix) · Verify your installation (`curl -s http://localhost/api/health` and the expected JSON shape). Cross-reference `docs/architecture.md` for next-steps reading
- [ ] T057 [US5] Edit `README.md` quickstart section to link to `docs/quickstart.md` for detailed instructions. Keep the README quickstart section to a high-level summary
- [ ] T058 [US5] If `org-profile-README.md` exists at repo root or in a `.github` profile repo, edit its quickstart section to link to `docs/quickstart.md`. (Per assumption — file may not exist; tracked as conditional task)

## Phase 8: User Story 6 — technical documentation publication (Priority: P2)

**Goal**: Four documents under `docs/` carrying YAML frontmatter and accurately reflecting the implemented platform.

### Tests for US6

- [ ] T059 [P] [US6] Add a structural check to `scripts/check-discoverability.sh`:
  - Each of the four documents exists at its required path
  - Each has YAML frontmatter with `title`, `description`, `standards[]`, `last_updated` fields
  - Every `standards[]` entry corresponds to a row in `STANDARDS.md` with status `full` or `partial`
  - `last_updated` is a valid ISO date

### Implementation

- [ ] T060 [US6] Create `docs/architecture.md`. Source content: the "Five Layers of Open" architecture narrative from the planning folder (located via T009). Adapt for public consumption — strip internal references, normalise terminology, ensure every spec reference points at a public spec URL. Frontmatter:
  ```yaml
  ---
  title: Sorcha Architecture
  description: The five-layer open architecture of the Sorcha platform — wallets, registers, validators, peers, and the gateway above them.
  standards:
    - BIP32
    - BIP44
    - W3C VC Data Model 2.0
    - HAIP 1.0
    - OpenID4VCI
    - OpenID4VP
  last_updated: 2026-05-02
  ---
  ```
- [ ] T061 [US6] Create `docs/openid4vc-haip-integration.md`. Source: planning-folder OpenID4VC + HAIP narrative. Adapt similarly. Frontmatter `standards`: `OpenID4VCI`, `OpenID4VP`, `HAIP 1.0`, `W3C VC Data Model 2.0`, `IETF Token Status List 2024 (RFC 9972)`, `ML-DSA (FIPS 204)`
- [ ] T062 [US6] Create `docs/applicability.md`. Source: planning-folder applicability narrative. Adapt similarly. Cover at minimum DPP, trade finance, IPC-1782, municipal governance with one worked example each. Frontmatter `standards`: `W3C VC Data Model 2.0`, `OpenID4VCI`, `OpenID4VP`, `HAIP 1.0`
- [ ] T063 [US6] Create `docs/security-model.md`. Synthesise from the architecture-evaluation and applicability narratives. Required sections:
  - Selective disclosure — what SD-JWT VC gives, what it does not
  - Aggregate inference threat — disclosed-attribute correlation across presentations
  - Post-quantum posture — internal PQC, classical wire boundary at HAIP, what this protects and what it does not
  - mTLS gap — be explicit that service-to-service mTLS is not in production today; name it as a known gap
  - Trust anchor model — system register genesis (spec 099) and what it does and does not anchor
  Frontmatter `standards`: `ML-DSA (FIPS 204)`, `HAIP 1.0`, `W3C VC Data Model 2.0`, `OAuth 2.0`

## Phase 9: Polish & CI workflow

- [ ] T064 Create `.github/workflows/ai-discoverability-check.yml`. Trigger: pull_request on master. Steps:
  1. Checkout
  2. Boot the gateway in the workflow runner via `docker compose up -d`
  3. Wait for `http://localhost/api/health` to return 200
  4. Run `scripts/check-discoverability.sh` (which orchestrates spectral lint, swagger-cli validate, mcp manifest schema validation, llms.txt structure, STANDARDS.md parse, cross-reference checks, marketing-adjective deny-list)
  5. On failure, post a comment on the PR with the failing check and a link to the workflow run
- [ ] T065 Mark the `ai-discoverability-check` job as required in the `master` branch protection ruleset (manual GitHub UI step; record in PR description)
- [ ] T066 [P] Update `docs/README.md` to list the four new published documents (`architecture.md`, `openid4vc-haip-integration.md`, `applicability.md`, `security-model.md`) plus `quickstart.md`, `mcp-server.md`, `llms-full.txt`
- [ ] T067 [P] Update `README.md` root to link to `llms.txt`, `STANDARDS.md`, and `docs/quickstart.md` from a new "For AI agents and integrators" section
- [ ] T068 [P] Update the `sorcha-architecture` skill at `.claude/skills/sorcha-architecture/SKILL.md` with a short pointer section for AI Discoverability Surface listing the well-known endpoints, `llms.txt`, `STANDARDS.md`, and the four published documents
- [ ] T069 Run the full discoverability check locally end-to-end: `./scripts/check-discoverability.sh` against a freshly-built gateway. Confirm zero failures
- [ ] T070 Run the quickstart against a clean Docker desktop locally. Confirm gateway reachable, `/api/health` returns 200, and the verify-installation curl prints the expected JSON

---

## Dependencies

- Phase 1 (Setup) → Phase 2 (Audit) → Phases 3-8 in any order, but P1 phases (3, 4, 5) should land before P2 phases (6, 7, 8)
- US1 (OpenAPI) feeds US3 (`llms.txt` Links section) and US6 (frontmatter `standards[]` cross-reference)
- US2 (MCP) feeds US3 (`llms.txt` Links section)
- US4 (`STANDARDS.md`) is a pre-requisite for the cross-reference checks in US3 (T046) and US6 (T059)
- Phase 9 (Polish & CI) depends on all earlier phases — the workflow's checks reference all earlier artefacts
- T039 (the 36 MCP tool description rewrites) can be parallelised across categories but each individual tool is a serial edit

## Parallel opportunities

- T003 / T004 parallel — Spectral and swagger-cli setup are independent
- T007 / T008 / T009 / T010 / T011 parallel — independent audit tasks
- T012 / T013 / T014 parallel — independent test infrastructure setup
- T019-T025 parallel — each service's endpoint annotation pass is independent
- T031 / T032 / T033 parallel — independent test infrastructure for US2
- T039 (the 36 tool rewrites) parallel by category and by individual tool (with same-file edits serialised)
- T042 / T043 parallel
- T047 / T048 parallel
- T052 / T053 parallel
- T060 / T061 / T062 / T063 parallel — four documents with no shared content
- T066 / T067 / T068 parallel — independent doc updates

---

## Task summary

| Phase | Tasks | Count |
|---|---|---|
| Phase 1 Setup | T001-T005 | 5 |
| Phase 2 Foundational audit | T006-T011 | 6 |
| Phase 3 US1 OpenAPI surface | T012-T030 | 19 |
| Phase 4 US2 MCP discovery (incl. 36 sub-tool edits) | T031-T041 | 11 (+ 36 sub-edits at T039) |
| Phase 5 US3 llms.txt | T042-T046 | 5 |
| Phase 6 US4 STANDARDS.md | T047-T051 | 5 |
| Phase 7 US5 quickstart hardening | T052-T058 | 7 |
| Phase 8 US6 docs publication | T059-T063 | 5 |
| Phase 9 Polish & CI | T064-T070 | 7 |
| **Total** | | **70 (+ 36 sub-edits)** |

**Suggested MVP**: Phase 1 + 2 + 3 + 4 + 5 = 46 tasks (plus the 36 tool sub-edits at T039). Ships the OpenAPI surface, MCP discovery + tool quality, and `llms.txt`. `STANDARDS.md` (US4), quickstart hardening (US5), and docs publication (US6) can ship in subsequent increments.

**Recommended sequencing**: Phase 1 + Phase 2 first (sets the lint / audit baseline). Then US4 (STANDARDS.md, since US3 and US6 reference it). Then US1 + US2 + US3 in parallel branches. Then US5 + US6 in parallel branches. Phase 9 last.
