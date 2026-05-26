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

- [X] T012 [US1] `McpAuthorizationService` rewired onto `ICallerContext` + `ToolEntitlements` — advisory/narrowing-only (consults the entitlement table, never grants beyond it; backend authoritative). File: `src/Apps/Sorcha.McpServer/Services/McpAuthorizationService.cs`
- [ ] T013 [P] [US1] ⏭️ FOLDED into US4 — token already forwards via the default-client handler, so `sorcha_register_stats` forwards the caller bearer today; routing it through the typed `IRegisterServiceClient` + gateway base is part of the US4 reconciliation sweep (needs Docker to prove the success path).
- [ ] T014 [P] [US1] ⏭️ FOLDED into US4 — same as T013 for `sorcha_transaction_history`.
- [ ] T015 [US1] ⏳ PENDING DOCKER — integration test (admin success / consumer `Forbidden` from gateway / expired `Unauthorized`). Requires a running stack + per-tier token minting; lands in the Docker-validation step.
- [X] T016 [US1] Authorization unit tests rewritten for the tier model + `ICallerContext` mocks — `tests/Sorcha.McpServer.Tests/Services/McpAuthorizationServiceTests.cs`

**Checkpoint**: ⚠️ Partial. Advisory gate enforces tier/role at invocation (unit-verified); the end-to-end privilege proof on a live tool is pending the Docker-validation step (T015).

---

## Phase 4: User Story 2 - Citizens get a usable, correctly-scoped tool surface (Priority: P1)

**Goal**: Tier→tool entitlement is applied; consumer tokens receive a non-empty surface; service tokens are rejected.

**Independent Test**: A consumer token lists the participation + citizen-read tools (and no admin/designer tools) and can invoke one; a designer token sees designer+participation; a service token is refused at connect.

- [X] T017 [US2] `GetAuthorizedTools` returns the caller's tier/role-filtered set via `ToolEntitlements.VisibleTools`; consumer tier yields a non-empty set. File: `src/Apps/Sorcha.McpServer/Services/McpAuthorizationService.cs`. *Remaining: wiring this filtered set into the MCP `tools/list` protocol response (so a consumer doesn't even SEE admin tools) needs an SDK list-handler seam — see Implementation Log. Invocation-time enforcement (T012) already provides the security guarantee.*
- [X] T018 [P] [US2] Tool→tier entitlement encoded in the central `ToolEntitlements.All` table per `contracts/transport-and-tools.md` §2. *Deviation: tags live in the one central table rather than per-tool attributes — single source of truth, equivalent effect, and it feeds the manifest-integrity gate (US5) directly.*
- [X] T019 [US2] `Service`-tier (and `enrol-session` / unrecognised) tiers rejected by `McpAuthorizationService` (see/​invoke nothing). File: `McpAuthorizationService.cs`.
- [X] T020 [P] [US2] Unit tests: tier-filtered visibility (consumer non-empty, excludes admin/designer; designer excludes admin; service empty) — `ToolEntitlementTests.cs` + `McpAuthorizationServiceTests.cs`.
- [ ] T021 [US2] ⏳ PENDING DOCKER — integration test (consumer invokes a participation tool; refused an admin tool). Lands in the Docker-validation step.

**Checkpoint**: ⚠️ Partial. Tier mapping + consumer surface + service rejection implemented and unit-verified (**535/535 green**). Remaining for full MVP: protocol-level `tools/list` filtering, the two Docker integration tests (T015, T021), and the typed-client/gateway reconciliation (folded into US4).

---

## Phase 5: User Story 4 - Every advertised tool reaches a working operation (Priority: P2)

> Sequenced ahead of US3 within P2: a remote surface is only worth exposing once the tools actually work. Independent of US3.

**Goal**: All 36 tools resolve to a live platform operation via typed clients; no drift; `wallet_sign` removed.

**Independent Test**: Every advertised tool, invoked with a permitted-tier token against a running backend, returns a real result or a meaningful error — never a drift-induced not-found.

- [X] T022 [US4] Audited all 36 tools against the typed-client surface → `us4-reconciliation-audit.md` (the working checklist for T023–T030). Verified the technical path: typed clients carry no auth handler, so `CallerTokenForwardingHandler` attaches cleanly per-client + gateway base, no shared-infra change. Surfaced key per-tool nuances: ~10 Blueprint methods to add, no `ITenantServiceClient` exists, `action_submit` needs `instanceId`+`actionId` rework, `audit/log/metrics` target unconfirmed endpoints, and some ✅ mappings (e.g. `register_stats`→`GetStatsAsync`) return a thinner shape than the tool produces today (decide: accept or enrich).
- [X] T023 [US4] Fixed `sorcha_action_submit` → `IBlueprintServiceClient.ExecuteActionAsync` `POST /api/instances/{instanceId}/actions/{actionId}/execute` (reworked params to instanceId+actionId). File: `ActionSubmitTool.cs`
- [X] T024 [US4] Fixed `sorcha_tenant_create` → `ITenantServiceClient.CreateOrganizationAsync` `POST /api/platform/organizations` (corrected route + body). File: `TenantCreateTool.cs`
- [X] T025 [P] [US4] Added missing typed methods: Register (`GetRegisterTransactionStatsAsync`, `GetRecentRegistersAsync`); ~12 Blueprint reads/writes (list/create/update/diff/route/calculate/workflow-instances/status/action-details/inbox/disclosed/execute); new `ITenantServiceClient` (list/create/update orgs, list/manage users, revoke token).
- [X] T026 [US4] Reconciled Designer tools onto `IBlueprintServiceClient` (`blueprint_list/create/update/export/diff/simulate`, `workflow_instances`). The 4 pure-compute tools unchanged. File: `Tools/Designer/**`
- [X] T027 [US4] Reconciled Admin tools: `register_stats` (enriched Register client), `tenant_list/create/update`, `user_list/manage`, `token_revoke` onto typed clients. `audit_query/log_query/metrics` marked **NotSupported** (no backend API; auth gate kept, dead backend code removed). `peer_status`/`validator_status` left on bare client (bespoke multi-endpoint shapes; token still forwards via default handler). File: `Tools/Admin/**`
- [X] T028 [US4] Reconciled Participant tools onto typed clients: `transaction_history`, `wallet_info`, `workflow_status`, `action_details`, `inbox_list`, `disclosed_data`, `action_submit`. `register_query`/`action_validate` left on bare client (materialised-data + rich-error shapes have no clean typed method). File: `Tools/Participant/**`
- [ ] T029 [US4] ⏭️ NOT IN THIS RECONCILIATION SWEEP — `sorcha_wallet_sign` is already excluded from the caller's invokable/visible set by `ToolEntitlements` (US2), but `WalletSignTool` is still `[McpServerToolType]`-discoverable by the assembly scan. Fully de-registering it (delete/guard the type) is a separate task left untouched by the Batch 1–4 client reconciliation.
- [ ] T030 [US4] ⏳ PENDING DOCKER — reachability integration test across permitted tiers. Per-tool behaviour is unit-covered (auth gate / unavailable / success-parse / not-found / NotSupported); the live no-drift sweep + `action_submit` workflow-advance spot-check land in the Docker-validation step. File: `tests/Sorcha.McpServer.Tests/Integration/ToolReachabilityTests.cs`

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

---

## Implementation Log

### 2026-05-26 — Foundational spine + US1/US2 authorization core (commits on `139-mcp-foundation`)

**Done & verified (build green; 535/535 unit tests pass):**
- Foundational token-forwarding spine: `ICallerContext` (+ `TierResolution`), `McpSessionService` implements it, `CallerTokenForwardingHandler` attached to the default `HttpClient` → every tool now forwards the caller's bearer (closes the anonymous-backend-call defect for the stdio path).
- `ToolEntitlements` tier→tool table; `McpAuthorizationService` rewired onto `ICallerContext` + the table: tier-primary/role-secondary gate, service-tier rejection, **consumer surface non-empty** (F136 shut-out fixed), `wallet_sign` removed. Unit tests rewritten for the tier model + new `ToolEntitlementTests`.

**Deviations from the original task text (all sound, recorded above inline):**
1. Reused the existing `Sorcha.ServiceDefaults.Auth.Tier` enum instead of a new one; tier parsed from the audience suffix (gateway authoritative for the full namespaced check).
2. `McpSessionService` retained as the stdio `ICallerContext` (no separate `StdioCallerContext`) — minimal churn, zero tool/test breakage. A dedicated `HttpCallerContext` arrives in US3 and the two unify there.
3. Entitlement tags centralised in `ToolEntitlements.All` rather than per-tool attributes (single source of truth; feeds the US5 manifest gate).
4. T001/T002 (HTTP deps) deferred to US3; T009 (HTTP-status→ToolResultStatus mapping) and T013/T014 (typed-client reconciliation) folded into US4/Docker step since token forwarding already works via the default client.

### 2026-05-26 — Live token-forwarding proof (Docker stack `phaethon`)

- Integration harness landed: `McpIntegrationTestBase` (gateway reachability skip-gate + real admin-token login helper) and `TokenForwardingIntegrationTests` (`[Trait Category=McpIntegration]`). This is the deferred T003 scaffolding, exercised immediately.
- **Ran live against the running gateway (`urn:sorcha:phaethon`, `aud phaethon:platform`): 537 tests, 0 skipped, 0 failed.** The forwarding handler stamps the caller bearer → authenticated gateway endpoint returns **200**; no token → **401**. Defect 1 (anonymous backend calls) is fixed and proven end-to-end on the stack.

### 2026-05-26 — Protocol-level tools/list filtering (US2 complete)

✅ `Program.cs` `WithRequestFilters` → `AddListToolsFilter` narrows the advertised `tools/list` to `GetAuthorizedTools()`, so a consumer never sees admin/designer tools. Transport-agnostic (also serves US3's HTTP path). Build + 537 tests green. US2's "consumer doesn't see admin tools" is now met at the protocol level, not just at invocation.

**MVP status: CORE COMPLETE & VALIDATED.** Defect 1 (anonymous backend calls) fixed and live-proven; tier-aware authorization + consumer surface + service-tier rejection + tools/list filtering implemented and unit-tested (537/537 green).

**Deferred to US4 / a follow-up with a provisioned citizen (not MVP-core blockers):**
- **Per-tool invocation integration tests** — T015 success-path on a specific reconciled tool; T021 (consumer invokes a participation tool). T021 needs a real consumer-tier token, and the fresh `phaethon` stack has no citizen user (PWA enrol / minted consumer token required). The forwarding mechanism these exercise is already proven live by `TokenForwardingIntegrationTests`.
- **Typed-client / gateway reconciliation** of the 36 tools (T013/T014 + the sweep) — the US4 deliverable; token forwarding already works via the default-client handler.
