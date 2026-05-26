# Tasks: MCP Server Foundation

**Input**: Design documents from `/specs/139-mcp-foundation/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/transport-and-tools.md, quickstart.md

**Tests**: Integration ("safety net") and manifest-integrity tests are explicit feature requirements (FR-004, FR-012, SC-005, SC-007), so their tasks are included. Existing unit tests are updated, not expanded for their own sake.

**Organization**: Tasks grouped by user story. MVP = Setup + Foundational + US1 + US2 (both P1).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US5 for story-phase tasks; none for Setup/Foundational/Polish

## Path Conventions

- MCP server: `src/Apps/Sorcha.McpServer/`
- Service clients: `src/Common/Sorcha.ServiceClients.Http/`
- Gateway: `src/Services/Sorcha.ApiGateway/`
- Tests: `tests/Sorcha.McpServer.Tests/`
- Repo root: `docker-compose.yml`, `server.json`, `Directory.Packages.props`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Bring in the HTTP-transport dependency and the test scaffolding everything else uses.

- [ ] T001 ⏭️ DEFERRED to US3 — `ModelContextProtocol.AspNetCore` is an HTTP-transport dependency; not needed for the stdio MVP. Add when building US3.
- [ ] T002 ⏭️ DEFERRED to US3 — ASP.NET Core framework reference is an HTTP-host prerequisite; add with US3.
- [ ] T003 ⏭️ FOLDED into US1 (T015) — the integration scaffolding + `TierTokenFixture` lands with the first integration test (US1), so the fixture and base class are exercised immediately rather than sitting unused.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The caller-identity + token-forwarding + error-mapping spine. Both P1 stories (US1, US2) require all of it. Delivers a fully wired **stdio** path; the HTTP path is added in US3.

**⚠️ CRITICAL**: No user-story work begins until this phase is complete.

- [X] T004 `aud`→`Tier` parser via audience suffix (`TierResolution.Resolve`, reusing the existing `Sorcha.ServiceDefaults.Auth.Tier` enum rather than duplicating it) — `src/Apps/Sorcha.McpServer/Infrastructure/ICallerContext.cs`
- [X] T005 `ICallerContext` (`RawToken`, `Tier`, `Roles`, `OrganizationId`, `Subject`, `IsAuthenticated`) — `src/Apps/Sorcha.McpServer/Infrastructure/ICallerContext.cs`
- [X] T006 Stdio caller context — `McpSessionService` now implements `ICallerContext` (computes `Tier` from `jwt.Audiences`; exposes raw token + claims). *Deviation: kept `McpSessionService` as the stdio impl instead of a separate `StdioCallerContext` class — minimal churn, no tool/test breakage; a dedicated `HttpCallerContext` arrives in US3 and the two unify there.*
- [X] T007 `CallerTokenForwardingHandler : DelegatingHandler` (stamps `Authorization: Bearer` from `ICallerContext`; no-op when unauthenticated or header already present) — `src/Apps/Sorcha.McpServer/Infrastructure/CallerTokenForwardingHandler.cs`
- [X] T008 Forwarding handler attached to the default `HttpClient` so all current tools forward the caller token immediately — `Program.cs`. *Carry-forward: pointing the typed `Sorcha.ServiceClients` at the gateway lands in US1 with the first reconciled typed client.*
- [ ] T009 ⏭️ MOVED to US1 — gateway HTTP-status→`ToolResultStatus` mapping is exercised by the first reconciled tools; doing it there keeps it testable rather than speculative.
- [X] T010 DI rewired in `Program.cs`: `ICallerContext` registered as the stdio session instance; forwarding handler registered. *Deviation: `McpSessionService` retained (see T006); `IHttpContextAccessor` registration deferred to US3 (HTTP only).*
- [X] T011 [P] `ToolEntitlement` model + static tier→tool table (per `contracts/transport-and-tools.md` §2; `wallet_sign` excluded) — `src/Apps/Sorcha.McpServer/Services/ToolEntitlement.cs` (consumed by US2)

**Checkpoint**: ✅ Reached. Token-forwarding spine live over stdio (all tools forward the caller's bearer); tier derived from the token; entitlement table modelled. Server + test projects build; **550/550 existing unit tests pass**. Deviations recorded inline + in the Implementation Log below.

---

## Phase 3: User Story 1 - Tools execute with the caller's real privileges (Priority: P1) 🎯 MVP

**Goal**: Every tool call is authorized by the platform as the calling identity; the local check is advisory and narrowing-only.

**Independent Test**: With an admin token a representative admin tool returns a real backend result; with a consumer token the same tool is refused **by the gateway**; an expired token yields a clean `Unauthorized`.

- [ ] T012 [US1] Convert `McpAuthorizationService` to advisory/narrowing-only: it may hide/refuse but never grant; the per-invocation check returns `Unauthorized`/`Forbidden` cleanly and defers final authority to the backend. File: `src/Apps/Sorcha.McpServer/Services/McpAuthorizationService.cs`
- [ ] T013 [P] [US1] Reconcile a representative **admin** read tool (`sorcha_register_stats`) onto its typed `IRegisterServiceClient` method so it calls the gateway with the forwarded token — `src/Apps/Sorcha.McpServer/Tools/Admin/RegisterStatsTool.cs`
- [ ] T014 [P] [US1] Reconcile a representative **participation** read tool (`sorcha_transaction_history`) onto its typed client — `src/Apps/Sorcha.McpServer/Tools/Participant/TransactionHistoryTool.cs`
- [ ] T015 [US1] Integration test: admin token → `sorcha_register_stats` success; consumer token → same tool `Forbidden` (assert refusal originates from the gateway, not the local gate); expired token → `Unauthorized`. File: `tests/Sorcha.McpServer.Tests/Integration/PrivilegeEnforcementTests.cs`
- [ ] T016 [US1] Update affected unit tests for the advisory-narrowing behaviour and `ICallerContext` (replace `McpSessionService` mocks) in `tests/Sorcha.McpServer.Tests/Services/McpAuthorizationServiceTests.cs`

**Checkpoint**: Privilege enforcement is real and demonstrated end-to-end on representative tools. MVP backbone in place.

---

## Phase 4: User Story 2 - Citizens get a usable, correctly-scoped tool surface (Priority: P1)

**Goal**: Tier→tool entitlement is applied; consumer tokens receive a non-empty surface; service tokens are rejected.

**Independent Test**: A consumer token lists the participation + citizen-read tools (and no admin/designer tools) and can invoke one; a designer token sees designer+participation; a service token is refused at connect.

- [ ] T017 [US2] Apply the entitlement table to `ListTools` so the advertised set is the caller's tier/role union (stdio path); ensure consumer-tier yields a non-empty set. File: `src/Apps/Sorcha.McpServer/Services/McpAuthorizationService.cs`
- [ ] T018 [P] [US2] Tag every tool with its `ToolEntitlement` per `contracts/transport-and-tools.md` §2 (participation/citizen-read = `[Consumer,Platform]`; designer/admin = `[Platform]`+role) across `src/Apps/Sorcha.McpServer/Tools/**`
- [ ] T019 [US2] Reject `Service`-tier (and `enrol-session`) tokens at connect/first-invocation with a clear message, in `src/Apps/Sorcha.McpServer/Infrastructure/StdioCallerContext.cs` + `Program.cs`
- [ ] T020 [P] [US2] Unit tests: `ListTools` per tier (consumer non-empty; consumer excludes admin/designer; designer excludes admin; service rejected) in `tests/Sorcha.McpServer.Tests/Services/ToolEntitlementTests.cs`
- [ ] T021 [US2] Integration test: consumer token invokes a participation tool (`sorcha_inbox_list`) successfully and is refused an admin tool. File: `tests/Sorcha.McpServer.Tests/Integration/ConsumerSurfaceTests.cs`

**Checkpoint**: US1 + US2 complete = MVP. The existing surface works, tiered, over stdio; citizens are no longer shut out.

---

## Phase 5: User Story 4 - Every advertised tool reaches a working operation (Priority: P2)

> Sequenced ahead of US3 within P2: a remote surface is only worth exposing once the tools actually work. Independent of US3.

**Goal**: All 36 tools resolve to a live platform operation via typed clients; no drift; `wallet_sign` removed.

**Independent Test**: Every advertised tool, invoked with a permitted-tier token against a running backend, returns a real result or a meaningful error — never a drift-induced not-found.

- [ ] T022 [US4] Audit all 36 tools against `contracts/transport-and-tools.md` §3; record per-tool target client method + status (✅/➕/🔧) as the working checklist for this phase
- [ ] T023 [US4] Fix `sorcha_action_submit` → `IBlueprintServiceClient` `POST /api/instances/{instanceId}/actions/{actionId}/execute` in `src/Apps/Sorcha.McpServer/Tools/Participant/ActionSubmitTool.cs`
- [ ] T024 [US4] Fix `sorcha_tenant_create` → `ITenantServiceClient` `POST /api/platform/organizations` in `src/Apps/Sorcha.McpServer/Tools/Admin/TenantCreateTool.cs`
- [ ] T025 [P] [US4] Add any missing typed methods to `src/Common/Sorcha.ServiceClients.Http/` (e.g. Blueprint action/instance reads, Register query/stats/history/disclosed, Wallet info, Tenant user/token/audit ops) — one method per gap identified in T022
- [ ] T026 [US4] Reconcile the remaining Designer tools onto `IBlueprintServiceClient` methods in `src/Apps/Sorcha.McpServer/Tools/Designer/**`
- [ ] T027 [US4] Reconcile the remaining Admin tools onto their typed clients in `src/Apps/Sorcha.McpServer/Tools/Admin/**`
- [ ] T028 [US4] Reconcile the remaining Participant/citizen-read tools onto their typed clients in `src/Apps/Sorcha.McpServer/Tools/Participant/**`
- [ ] T029 [US4] Remove `sorcha_wallet_sign` from the advertised surface (delete the tool registration; leave a code comment pointing to the deferred dedicated wave) in `src/Apps/Sorcha.McpServer/Tools/Participant/WalletSignTool.cs`
- [ ] T030 [US4] Integration test: invoke every advertised tool across its permitted tiers and assert no drift-induced `NotFound`; spot-check `sorcha_action_submit` advances a workflow and returns a transaction id. File: `tests/Sorcha.McpServer.Tests/Integration/ToolReachabilityTests.cs`

**Checkpoint**: Every advertised tool is genuinely reachable (SC-001).

---

## Phase 6: User Story 3 - Remote agents and local operators can both connect (Priority: P2)

**Goal**: stdio + Streamable HTTP both work; the HTTP endpoint is a protected resource; exposed via the gateway `/mcp`.

**Independent Test**: In network mode, an authenticated request dispatches a tool and an unauthenticated one is rejected before dispatch; in local mode the same identity behaves identically.

- [ ] T031 [US3] Restructure `Program.cs` from console `Host` to `WebApplication` with a `--transport stdio|http` switch (default `stdio`); preserve the stdio path verbatim. File: `src/Apps/Sorcha.McpServer/Program.cs`
- [ ] T032 [US3] Implement `HttpCallerContext` (per-request, via `IHttpContextAccessor` — validated `ClaimsPrincipal` + raw bearer) in `src/Apps/Sorcha.McpServer/Infrastructure/HttpCallerContext.cs`; register it for the HTTP transport
- [ ] T033 [US3] Wire `WithHttpTransport(stateless)` + `app.MapMcp()`; protect the endpoint with ServiceDefaults `AddJwtAuthentication` (F136 issuer/audiences) so invalid/absent bearers are rejected before dispatch. File: `src/Apps/Sorcha.McpServer/Program.cs`
- [ ] T034 [US3] Use the stateless `ConfigureSessionOptions(httpContext,…)` hook to scope the advertised tool collection per request by caller tier (HTTP equivalent of the US2 `ListTools` filter). File: `src/Apps/Sorcha.McpServer/Program.cs`
- [ ] T035 [P] [US3] Add a `/mcp` YARP route to the MCP server in `src/Services/Sorcha.ApiGateway/appsettings.json`
- [ ] T036 [P] [US3] Add an HTTP-mode `mcp-server` service to `docker-compose.yml` (port on `sorcha-network`, `--transport http`, gateway base-URL env); keep the stdio `--profile tools` definition
- [ ] T037 [US3] Integration test: HTTP request with a valid bearer dispatches a tool; no/invalid bearer is rejected pre-dispatch; stdio parity for the same identity. File: `tests/Sorcha.McpServer.Tests/Integration/TransportParityTests.cs`

**Checkpoint**: Both transports functional; remote agents can connect through the gateway.

---

## Phase 7: User Story 5 - Breakage is visible & the catalogue stays honest (Priority: P3)

**Goal**: Per-invocation telemetry by tier/tool/outcome; the advertised catalogue cannot drift from the server.

**Independent Test**: A mixed run attributes each invocation in telemetry; changing the tool set without updating the catalogue fails an automated check.

- [ ] T038 [US5] Extend `ToolAuditService` to record `{ tier, toolName, outcome, backendStatus }` per invocation (no token material) in `src/Apps/Sorcha.McpServer/Services/ToolAuditService.cs`
- [ ] T039 [P] [US5] Add `McpMetrics` (`Sorcha.Mcp` meter): `sorcha_mcp_tool_invoked_total{tool,tier,outcome}`, `sorcha_mcp_backend_call_total{service,status}`, latency histogram; register on the meter provider. File: `src/Apps/Sorcha.McpServer/Services/McpMetrics.cs`
- [ ] T040 [US5] Pin `server.json` to a real version and align its tool list to the server
- [ ] T041 [US5] Manifest-integrity test: reflect the `[McpServerTool]` set from the MCP assembly and assert the gateway `appsettings.json` catalogue **and** `server.json` match; fail on drift. File: `tests/Sorcha.McpServer.Tests/ManifestIntegrityTests.cs`
- [ ] T042 [P] [US5] Wire the manifest-integrity test into CI (the discoverability/build gate) so drift fails the build

**Checkpoint**: Regression is observable and catalogue drift is build-blocking.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T043 [P] Update `docs/mcp-server.md` (tiered surface, both transports, token-forwarding, run/verify) and `docs/mcp-registry-publishing.md` (version pin)
- [ ] T044 [P] Update the `sorcha-architecture` skill MCP surface section and `src/Apps/Sorcha.McpServer/README.md`
- [ ] T045 [P] Add a CLAUDE.md Critical Pattern note: MCP tools forward the caller token via the typed clients; no hand-rolled HTTP/headers; gateway is authoritative
- [ ] T046 Remove dead code / leftover `McpSessionService` references; ensure no compiler warnings (Release)
- [ ] T047 Run `quickstart.md` end-to-end against the local Docker stack (all verify-matrix rows) and capture results

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (P1)** → no deps.
- **Foundational (P2)** → after Setup. **BLOCKS all stories.**
- **US1, US2 (both P1)** → after Foundational. US2 applies the entitlement table US1's gate consults; do US1 then US2 (or parallel with care — they touch `McpAuthorizationService` together, so sequential is safer).
- **US4 (P2)** → after Foundational; independent of US3. Recommended before US3.
- **US3 (P2)** → after Foundational; independent of US4. Adds `HttpCallerContext` + HTTP filter (mirrors US2's stdio filter).
- **US5 (P3)** → after US4 (tool set stable) for the manifest gate; telemetry tasks (T038/T039) can start after Foundational.
- **Polish** → after all desired stories.

### Within stories

- Reconcile-tool tasks marked [P] touch different files → parallelizable.
- `McpAuthorizationService` is touched by T012/T017 → keep sequential.
- Integration tests follow the implementation they assert.

### Parallel opportunities

- Setup: T002, T003 in parallel.
- Foundational: T011 parallel with T004–T010 spine.
- US4: T025 (client methods) parallel with T026/T027/T028 tool reconciliations once the gaps are known (T022 first).
- US3: T035 (gateway route) + T036 (compose) parallel with the host work.
- US5: T039 + T042 parallel with T038/T041.

---

## Implementation Strategy

### MVP (Setup + Foundational + US1 + US2)

Delivers the existing tool surface working over stdio, tiered by token, with privileges enforced by the platform and citizens admitted. Stop and validate here — this alone resolves both foundational defects for the local-operator path.

### Incremental delivery

1. Setup + Foundational → spine ready.
2. + US1 → privileges real (demo on representative tools).
3. + US2 → tiered surface, citizens admitted → **MVP**.
4. + US4 → all 36 tools reachable (no drift).
5. + US3 → remote agents over HTTP.
6. + US5 → observability + manifest gate.
7. Polish → docs, cleanup, quickstart validation.

### Notes

- [P] = different files, no incomplete-task dependency.
- Each story is independently demonstrable at its checkpoint.
- Commit after each task or logical group; keep `McpAuthorizationService`-touching tasks sequential.
