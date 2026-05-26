# MCP Server Remediation — Design

**Date:** 2026-05-26
**Status:** Approved (brainstorm) → ready for spec-kit
**Author:** Stuart Fraser + Claude
**Branch:** `feature/mcp-server-remediation`

---

## 1. Context & problem

The Sorcha MCP server (`src/Apps/Sorcha.McpServer`) is structurally mature — 36 tools across admin/designer/participant slices, role gating, rate limiting, audit logging, graceful degradation, high-quality tool descriptions, an F117 discoverability manifest, and a registry-ready `server.json`. It has not been exercised against a live, secured backend in a long time, and a pass over it found **two foundational defects** plus a set of capability gaps.

### Defect 1 — the caller's privileges never reach the backend

Every tool performs a **local, in-process** authorization check (`McpAuthorizationService.CanInvokeTool`) and then calls the backend with a **bare `HttpClient`** that carries **no `Authorization` header**. `Program.cs` attaches no global auth handler. The supplied JWT is consumed only by `McpSessionService.InitializeFromToken()` for the local role gate, then discarded.

Consequences:
- Backend calls are anonymous → **401 against any auth-enforcing deployment** (real n1 / Production). The server only "works" against a dev stack with auth bypassed.
- The "correct privileges" model is **client-side advisory theatre** — the backend never sees the caller's identity, tier, or org.
- `AddServiceClients(...)` is registered but **unused** — tools bypass the typed clients (which *do* carry auth) for raw `HttpClient`.
- **F136 consumer tier gets zero tools** — consumer tokens omit `roles`, and the RBAC map keys on roles, so a citizen token maps to an empty tool set.
- The JWT is captured **once at process start** with no refresh — a long session dies on expiry.

### Defect 2 — endpoint drift

Tools were built against early/assumed endpoint shapes and never reconciled. Examples:
- `sorcha_action_submit` POSTs `/api/actions/{id}/submit`; `ActionEndpoints.cs` exposes only `GET /api/actions/pending` + `/pending/count`. The real path is `POST /api/instances/{instanceId}/actions/{actionId}/execute`. **The tool 404s.**
- `sorcha_tenant_create` POSTs `{name,adminEmail}` to `/api/organizations`; real org-with-admin provisioning is `POST /api/platform/organizations` (SystemAdmin, different body).

Mocked unit tests hide both defects — the missing header and the wrong URL both pass CI green because nothing calls a live backend.

### Capability gaps

Mapping the 36 tools against the platform surface: no transaction-submission via the correct path, no register-control / federation tools (the federation work in PRs #828/#829 had the operator driving sync by hand — MCP-101/102), no credential/presentation lifecycle, no citizen/consumer self-service (wallet, devices, persona, invitations), and thin platform-admin (no org status/settings, no validator control). Transport is stdio-only. The gateway `/api/mcp/tools` catalogue is a hand-maintained list that can drift from the server.

---

## 2. Goals & non-goals

### Goals
- Make tool invocation **execute with the caller's real privileges**, enforced by the backend, tiered by the F136 token (`consumer` / `platform`).
- Eliminate the **endpoint-drift class of bug** permanently by routing all tool calls through the typed `Sorcha.ServiceClients`.
- Support **both stdio (operators) and Streamable HTTP (external agents)**.
- **Close the capability gaps** across register-control, credential/presentation lifecycle, citizen self-service, and platform-admin — phased.
- Add a **live integration safety net** so this can't silently rot again.

### Non-goals (this milestone)
- **Full MCP OAuth 2.1 Authorization Server** — the Tenant Service is a capable token *issuer* and OAuth *client*, but not an authorization server (no first-party `/authorize`, no AS metadata RFC 8414, no protected-resource metadata RFC 9728, no DCR RFC 7591, no resource indicators RFC 8707, no consent UI). Standing that up is its own milestone. Seams are designed in so it slots in later.
- **`wallet_sign`** as a usable tool — important, but raw signing-as-a-service for an agent needs a dedicated threat model and authz design. Parked to its own wave.
- **`service`-tier MCP callers** — service tokens are for the internal mesh; rejected at the door.
- **Node-lifecycle tools** (bootstrap / validator-key import / reset, MCP-103) — likely operator-only; the *decision* is documented, the tools are out.

---

## 3. Chosen approach

**Approach 1 — Gateway-fronted, typed-client refactor** (selected over a minimal header-patch and a direct-to-service variant).

Tools stop hand-rolling HTTP. They call the **API Gateway** (single base URL) through the existing typed `Sorcha.ServiceClients`. A per-request caller-token context feeds a `DelegatingHandler` that stamps `Authorization: Bearer` on every outbound call. The gateway enforces F136 tier/privilege for real; the MCP server keeps only an *advisory* tier/role filter on `ListTools`.

Rationale: it is the only approach that makes "privileges tiered by token" *true* rather than advisory, kills drift structurally (routes live in the versioned clients), and makes the MCP server exercise the same front door an external agent uses — which keeps it honest. The service clients are core infrastructure intended for exactly this reuse.

---

## 4. Foundation architecture

### 4.1 Caller context & token forwarding

Replace the singleton `McpSessionService` (one token per process) with an ambient **`ICallerContext`** (one token per call context):
- **stdio:** token from `--jwt-token` / `SORCHA_JWT_TOKEN` at startup (one caller per process).
- **Streamable HTTP:** token per request from the inbound `Authorization` header via `IHttpContextAccessor`.

`ICallerContext` exposes the raw bearer + parsed claims (tier from `aud` via `SorchaAudiences`, roles from the role claim, org_id, sub).

A **`CallerTokenForwardingHandler : DelegatingHandler`** is registered on the `Sorcha.ServiceClients` HttpClients. It reads the raw bearer from `ICallerContext` and stamps `Authorization`. No tool touches a header. All service-client base addresses point at the **API Gateway**.

### 4.2 Two enforcement layers

1. **Advisory (local, UX):** validate the bearer locally (`JwtValidationHandler`, F136 issuer/audiences) to parse tier+roles → filter the advertised `ListTools` set and short-circuit obviously-unauthorised calls with a friendly message. **Invariant: the local gate can only ever narrow — never grant access the backend wouldn't.**
2. **Authoritative (backend, security):** the gateway re-validates and enforces. `401/403/404/429` map to clean tool-result statuses (`Unauthorized`/`Forbidden`/`NotFound`/`RateLimited`).

### 4.3 Tier → tool mapping (F136-aware)

Tier-primary, role-secondary, with explicit cross-tier tools. Each tool is tagged with the tier(s) that may invoke it and (platform-only) a required role. The advisory `ListTools` filter shows the union the caller qualifies for.

| Slice | Who (tier + role) | Tools |
|---|---|---|
| **Citizen / wallet self-service** *(new)* | `consumer` | my credentials, my devices, my persona, pending-applications, present-credential, my presentation history, holder-keys, my transaction history |
| **Workflow participation** *(cross-tier)* | `consumer` **or** `platform` | inbox/pending actions, action details, action validate, **action submit**, workflow status, disclosed data |
| **Designer** | `platform` + designer role | blueprint CRUD / validate / simulate / diff / export / schema / jsonlogic / workflow-instances / publish |
| **Operator / Admin** | `platform` + admin role | health, logs, metrics, audit, tenant & org mgmt, user mgmt, peer/validator status, register stats, register-control, validator control, platform settings, token revoke |

- Consumer tokens get a real surface (citizen + participation) — fixes the F136 shut-out.
- Participation is cross-tier on purpose (citizen applicant *and* org analyst both submit actions); the tier routes the call to the right audience and the gateway enforces what each can touch.
- `service`-tier tokens rejected at the door with a clear message.
- `wallet_sign` removed from the participation slice pending its dedicated wave; `wallet_info` (read-only) stays.

### 4.4 Endpoint reconciliation & service-client reuse

**Rule: no hand-rolled URLs in the MCP server.** Every tool maps to a typed `Sorcha.ServiceClients` method. Where a method exists, use it; where it doesn't, **add it to the service client** (core infra — the whole platform benefits), never to the tool. Identified reconciliations:
- `sorcha_action_submit` → `POST /api/instances/{instanceId}/actions/{actionId}/execute` via `IBlueprintServiceClient`.
- `sorcha_tenant_create` → `POST /api/platform/organizations` via `ITenantServiceClient`.
- Every other tool gets the same audit; nonexistent-route tools are corrected or removed.

### 4.5 Transport & hosting

`Program.cs` restructures from a console `Host` to a `WebApplication` selecting transport at startup (`--transport stdio|http`, default `stdio` for back-compat):
- **stdio** — unchanged operator ergonomics.
- **Streamable HTTP** — MCP SDK HTTP transport on ASP.NET Core. The endpoint is itself a **protected resource**: ServiceDefaults `AddJwtAuthentication` rejects anything without a valid F136 bearer before dispatch; the validated token flows per-request into `ICallerContext`.

**Gateway-fronted, single origin:** the MCP HTTP endpoint is exposed via the API Gateway (a `/mcp` YARP route) alongside the existing `/.well-known/mcp.json`. The server's outbound calls also target the gateway (internal URL); the gateway→mcp→gateway loop is fine (distinct paths). docker-compose gains an HTTP-mode `mcp-server` with a port on `sorcha-network`; the stdio profile stays for local use.

**Token lifecycle is the caller's job under pass-through** (deliberate): stdio operator sessions die on token expiry (documented); HTTP agents send a fresh bearer per request. This is the exact seam where the future OAuth AS slots in.

---

## 5. Phased milestone

One coherent milestone in waves; **Phase 0 is load-bearing** — nothing else ships until the surface is genuinely wired.

- **Phase 0 — Foundation:** `ICallerContext` + `CallerTokenForwardingHandler`; gateway-fronted typed-client refactor of existing tools; F136 tier→slice mapping (+ consumer slice, service-tier rejection); endpoint reconciliation (+ new `ServiceClients` methods); `WebApplication` restructure + Streamable HTTP + protected-resource auth; integration smoke harness; manifest/`server.json` reconciliation + CI gate. `wallet_sign` removed pending its wave. **Outcome: the existing surface works, tiered, over both transports.**
- **Wave 1 — Register-control / federation** (MCP-101/102): subscribe/unsubscribe, sync-state, local-relationship, transaction submit/verify (F079 receipts, inclusion-proof, verification-bundle), revoke.
- **Wave 2 — Credential & presentation lifecycle:** HAIP issue-offer, verifier request, presentation status/lifecycle (F111), credential revoke/suspend/reinstate/refresh.
- **Wave 3 — Citizen/consumer self-service:** credentials, devices (F114/F128), persona (F125), pending-applications (F124), org + register invitations, present-credential.
- **Wave 4 — Platform-admin depth:** org status (suspend/activate), platform settings, org user audit, validator start/stop/restart, platform user provisioning/password reset.
- **Dedicated wave — `wallet_sign`:** strict authn/authz, threat model, consent + audit. Own design.
- **Backlog milestone — full MCP OAuth 2.1 AS** (Tenant Service as authorization server). Seams designed in Phase 0.
- **MCP-103 node-lifecycle:** explicit *documented* decision (likely operator-only, out of MCP).

Each wave is independently shippable on top of Phase 0.

---

## 6. Cross-cutting concerns

### 6.1 Testing
- **Unit tests** stay for fast logic coverage (input validation, tier filtering, error mapping).
- **Live integration smoke harness** (new, `Category=McpIntegration`): invokes tools in-process against a running gateway (Docker, ground-truth approach like the walkthroughs), authenticated with a **real minted token per tier** (consumer / platform-admin / platform-designer). Asserts a real success shape *or* the expected auth outcome (e.g. consumer token → 403 on an admin tool). Catches both defect classes — a missing header surfaces as 401, a drifted route as 404. CI runs it Docker-gated.

### 6.2 Observability
- Extend `ToolAuditService` to record caller tier + tool + outcome + backend status.
- New `Sorcha.Mcp` OTel meter: `sorcha_mcp_tool_invoked_total{tool,tier,outcome}`, `sorcha_mcp_backend_call_total{service,status}`, + a latency histogram.
- `RateLimitService` retained, keyed by caller.

### 6.3 Manifest integrity
- A CI gate reflects the MCP assembly's `[McpServerTool]` set and asserts the gateway `appsettings.json` catalogue **and** `server.json` match — build fails on drift. `server.json` version pinned per release.

### 6.4 Security invariants
- Advisory gate can only narrow, never grant.
- Never log tokens.
- `service`-tier rejected.
- Local validation is defence-in-depth; the gateway is authoritative.

### 6.5 Documentation
Update `docs/mcp-server.md`, `docs/mcp-registry-publishing.md`, the `sorcha-architecture` skill (MCP surface), the MCP server README, and add a CLAUDE.md note for the token-forwarding rule.

---

## 7. Out of scope / backlog (explicit)

| Item | Disposition |
|---|---|
| Full MCP OAuth 2.1 AS (Tenant Service as authorization server) | Backlog milestone; seams designed in Phase 0 |
| `wallet_sign` usable tool | Dedicated security-reviewed wave |
| Node-lifecycle tools (bootstrap / key import / reset, MCP-103) | Documented decision: operator-only, out of MCP |
| `service`-tier MCP callers | Rejected at the door |

---

## 8. Risks & mitigations

- **Big Phase 0 surface (touches MCP server + `Sorcha.ServiceClients.Http` + tests):** mitigated by the integration smoke harness gating "done," and by the fact that the typed-client refactor is mechanical once `ICallerContext` + handler exist.
- **HTTP transport restructure (`Host` → `WebApplication`):** keep stdio as the default and verify operator parity before flipping any deployment to HTTP.
- **Gateway loop (gateway→mcp→gateway):** distinct paths, no recursion; validated in the smoke harness.
- **Per-request token context under the MCP SDK's HTTP transport:** confirm the SDK surfaces `HttpContext` to tool invocations; if not, capture the token in a scoped accessor at the transport boundary.
