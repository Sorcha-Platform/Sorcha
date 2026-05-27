# Phase 0 Research — MCP Server Foundation

All decisions below resolve the design's open questions. The spec carried zero `[NEEDS CLARIFICATION]` markers; this records the technical resolutions that back the plan.

## R-001 — Streamable HTTP transport on the pinned SDK

**Decision**: Add the `ModelContextProtocol.AspNetCore` package (version aligned to the core `ModelContextProtocol` 1.1.0 pin) and host via `WebApplication` → `builder.Services.AddMcpServer().WithHttpTransport(...)` + `app.MapMcp()`. Run in **stateless** mode.

**Rationale**: The official C# SDK provides first-class Streamable HTTP through `ModelContextProtocol.AspNetCore`; stateless mode is the SDK's recommended posture for servers that don't need server-to-client requests (sampling/elicitation), and it scales horizontally. Confirmed against current SDK docs.

**Alternatives considered**: SSE-only (legacy, being superseded by Streamable HTTP); custom HTTP shim (reinvents the SDK). Rejected.

**Action**: Confirm the exact `ModelContextProtocol.AspNetCore` version that pairs with `1.1.0` at implementation time and add it to `Directory.Packages.props` (the AspNetCore package is versioned in lockstep with the core package).

## R-002 — Per-request caller identity

**Decision**: `ICallerContext` with two implementations. HTTP: inject `IHttpContextAccessor` and read the validated `ClaimsPrincipal` + raw bearer from the current request. stdio: a startup-token-backed implementation. Selected by transport at composition time.

**Rationale**: The SDK explicitly supports injecting `IHttpContextAccessor` (or `ClaimsPrincipal`) into `[McpServerToolType]` classes for HTTP; stateless mode also exposes `ConfigureSessionOptions(httpContext, …)` per request. stdio has no `HttpContext`, so a separate startup-token source is needed. One abstraction keeps tools transport-agnostic.

**Alternatives considered**: `AsyncLocal` token smuggling (works but fragile across await boundaries and duplicates what `IHttpContextAccessor` already gives); keeping the singleton session (the root defect). Rejected.

## R-003 — Outbound token forwarding

**Decision**: A `CallerTokenForwardingHandler : DelegatingHandler` registered on every `Sorcha.ServiceClients` HttpClient. It reads the raw bearer from `ICallerContext` and stamps `Authorization: Bearer`. Service-client base addresses point at the **API Gateway**.

**Rationale**: Centralises auth in one place (vs 36 call sites), reuses the typed clients (core infra intended for reuse), and makes the MCP server a normal gateway client — the gateway enforces F136 tiers authoritatively and applies rate limiting/routing. Eliminates the endpoint-drift bug class because routes live in the versioned clients.

**Alternatives considered**: Per-tool header stamping (brittle, 36 sites); direct-to-service typed clients (replicates gateway routing/auth knowledge, exposes internal endpoints). Rejected per the milestone design (Approach 1 chosen).

## R-004 — F136 tier → tool entitlement

**Decision**: Tier-primary, role-secondary entitlement. `ICallerContext.Tier` parsed from the token `aud` via `SorchaAudiences`; roles from the role claim (platform tier only). Each tool tagged with permitted tier(s) + optional platform role. `McpAuthorizationService` filters `ListTools` to the caller's union (advisory, narrowing-only). On HTTP, the stateless `ConfigureSessionOptions` hook is the natural place to scope the advertised tool collection per request; the same entitlement table drives the per-invocation short-circuit. Service-tier tokens rejected at the door.

**Rationale**: Consumer tokens carry no roles, so the current role-only RBAC shuts citizens out entirely; keying on tier fixes that. Participation tools are cross-tier (consumer applicant + platform analyst both submit actions). The gateway remains authoritative — the local filter only improves UX.

**Alternatives considered**: Keep role-only RBAC (leaves consumers with zero tools); backend-only with no local filter (agents see tools they can't use, wasted calls + poor UX). Rejected.

## R-005 — Endpoint reconciliation

**Decision**: Audit all 36 tools; map each to a typed `Sorcha.ServiceClients` method; add a method to the client where missing (never hand-roll in the tool). Confirmed corrections: `sorcha_action_submit` → `POST /api/instances/{instanceId}/actions/{actionId}/execute` (`IBlueprintServiceClient`); `sorcha_tenant_create` → `POST /api/platform/organizations` (`ITenantServiceClient`). The full per-tool table is in `contracts/`.

**Rationale**: The clients hold correct, contract-compiled routes; routing through them prevents recurrence. Additive client methods benefit the whole platform.

## R-006 — Live integration safety net

**Decision**: New xUnit `Category=McpIntegration` tests invoking tools **in-process** against a running gateway (local Docker stack), authenticated with a **real minted token per tier** (consumer / platform-admin / platform-designer). Each test asserts a real success shape or the expected auth outcome (e.g. consumer → 403 on an admin tool). Docker-gated in CI; INFRA-skipped when no stack.

**Rationale**: Mocked unit tests hid both defects (missing header → 401; drift → 404). Only a live call exercises the auth header and the real route. Mirrors the repo's existing ground-truth discipline (walkthroughs, UI E2E).

**Alternatives considered**: WebApplicationFactory mock backend (wouldn't catch route drift against the real services); pure unit mocks (status quo, blind). Rejected.

## R-007 — Manifest / catalogue integrity

**Decision**: A CI gate reflects the `[McpServerTool]` set from the MCP assembly and asserts the gateway `appsettings.json` catalogue **and** `server.json` match it; build fails on drift. `server.json` version pinned per release.

**Rationale**: The catalogue is currently a hand-maintained list that can drift from the server — the exact silent-rot failure mode. Reflection-as-source-of-truth removes the manual step.

**Alternatives considered**: Gateway fetches the live catalogue at runtime (adds a runtime coupling + an MCP-server dependency on boot); manual review (status quo, drifts). Rejected.

## R-008 — HTTP endpoint protection & gateway exposure

**Decision**: The Streamable HTTP endpoint requires a valid bearer via ServiceDefaults `AddJwtAuthentication` (F136 issuer/audiences) before dispatch; the validated principal flows into `HttpCallerContext`. Exposed externally via a gateway `/mcp` YARP route (single origin alongside `/.well-known/mcp.json`). docker-compose gains an HTTP-mode `mcp-server`; the stdio profile stays.

**Rationale**: The server is itself a protected resource — unauthenticated agents are rejected at the door, not at the first backend call. One origin matches what an external agent already discovers.

## R-009 — OAuth seam (deferred, not built)

**Decision**: Pass-through is the only auth mode in this feature. The caller manages token lifecycle (stdio session dies on expiry; HTTP agent sends a fresh bearer per request). The `ICallerContext` boundary is the exact seam where a future OAuth 2.1 resource-server flow plugs in without touching the tool layer.

**Rationale**: Tenant Service is not an OAuth authorization server today (no first-party `/authorize`, AS metadata, DCR, resource indicators) — standing that up is its own milestone. Confirmed during brainstorm readiness check.
