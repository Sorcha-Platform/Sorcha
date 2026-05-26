# Implementation Plan: MCP Server Foundation

**Branch**: `139-mcp-foundation` | **Date**: 2026-05-26 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/139-mcp-foundation/spec.md`
**Milestone design**: `docs/superpowers/specs/2026-05-26-mcp-server-remediation-design.md`

## Summary

Make the existing 36-tool Sorcha MCP server genuinely work: forward the caller's token to backends (so the platform enforces F136-tiered privileges for real), reconcile every drifted tool onto the typed `Sorcha.ServiceClients`, map F136 tiers to tool slices (admitting consumer tokens, which are currently shut out), serve both stdio and Streamable HTTP, and add a live integration safety net plus a manifest-integrity gate so it cannot silently rot again. No new capability tools (those are Feature 140).

Technical approach: replace the singleton `McpSessionService` with an ambient `ICallerContext` (startup token on stdio; per-request `HttpContext` Authorization header on HTTP via `IHttpContextAccessor`); register a `CallerTokenForwardingHandler : DelegatingHandler` on the `Sorcha.ServiceClients` HttpClients pointed at the API Gateway; restructure `Program.cs` to a `WebApplication` selecting transport at startup; keep the local authorization check as an advisory-narrowing `ListTools` filter while the gateway is authoritative.

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: `ModelContextProtocol` 1.1.0 (+ `ModelContextProtocol.AspNetCore` for Streamable HTTP — version aligned to the core pin), `Sorcha.ServiceClients` / `Sorcha.ServiceClients.Http`, `Sorcha.ServiceDefaults` (`AddJwtAuthentication`, `SorchaIssuer`, `SorchaAudiences`, OTel), `System.IdentityModel.Tokens.Jwt`, `Microsoft.AspNetCore` (HTTP host + `IHttpContextAccessor`)
**Storage**: None new. Stateless server; existing `RateLimitService` keyed by caller; existing `ToolAuditService` extended (no persistence change)
**Testing**: xUnit v3 unit tests (logic/tier-filtering/error-mapping) + new `Category=McpIntegration` Docker-gated in-process tool-invocation tests against a running gateway with real per-tier minted tokens
**Target Platform**: Linux container (docker-compose `mcp-server`); stdio subprocess for local operators
**Project Type**: Single .NET app (`src/Apps/Sorcha.McpServer`) + shared `Sorcha.ServiceClients` library + test project
**Performance Goals**: Not latency-critical; tool round-trip dominated by backend calls. Stateless HTTP for horizontal scale
**Constraints**: Pass-through auth only (no OAuth AS yet) — caller manages token lifecycle; advisory gate may only ever narrow; no secrets in logs; tokens validated against the installation's own issuer/audiences
**Scale/Scope**: 36 existing tools reconciled; one new transport; ~1 new DI handler + 1 caller-context abstraction; no new platform endpoints except a gateway `/mcp` route

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Microservices-First | PASS — MCP server is an app depending downward on `ServiceClients`/`ServiceDefaults`; no upward or cross-service coupling introduced. New `ServiceClients` methods are additive, shared infra. |
| II. Security First | PASS (improves) — closes an anonymous-backend-call defect; enforces F136 tiers at the authoritative boundary (gateway); fail-closed token validation; no secrets logged; service-tier rejected. |
| III. API Documentation | PASS — no new REST surface beyond a YARP `/mcp` passthrough; tool descriptions retain the FR-017/Spec-117 quality bar; the MCP manifest is the discovery surface and stays in sync via the integrity gate. |
| IV. Testing | PASS — unit coverage retained; **new** live integration smoke is the headline addition (the missing safety net). |
| V. Code Quality | PASS — .NET 10 / C# 14, nullable enabled, async I/O, DI throughout. |
| VI. Blueprint Standards | N/A |
| VII. Domain-Driven Design | PASS — uses Participant/Blueprint/Register/Disclosure terms; "tier" is the F136 ubiquitous term. |
| VIII. Observability | PASS (improves) — new `Sorcha.Mcp` OTel meter, structured logging, existing health surface retained. |

**No violations.** Complexity Tracking left empty.

## Project Structure

### Documentation (this feature)

```text
specs/139-mcp-foundation/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions (MCP HTTP transport, caller-context, token forwarding, tier mapping, smoke harness, manifest gate)
├── data-model.md        # Phase 1 — ICallerContext, Tier, ToolEntitlement, ToolResultStatus
├── quickstart.md        # Phase 1 — run stdio + HTTP, mint per-tier tokens, run the smoke harness
├── contracts/           # Phase 1 — transport endpoints + tier→tool matrix + tool→client-method reconciliation table
└── tasks.md             # Phase 2 (/speckit.tasks — not created here)
```

### Source Code (repository root)

```text
src/Apps/Sorcha.McpServer/
├── Program.cs                       # CHANGED: Host → WebApplication; --transport stdio|http; AddJwtAuthentication on HTTP; IHttpContextAccessor; register forwarding handler + ICallerContext
├── Infrastructure/
│   ├── ICallerContext.cs            # NEW: ambient caller identity (tier, roles, org, raw token)
│   ├── StdioCallerContext.cs        # NEW: startup-token-backed
│   ├── HttpCallerContext.cs         # NEW: IHttpContextAccessor-backed (per-request)
│   ├── CallerTokenForwardingHandler.cs  # NEW: DelegatingHandler stamping Authorization from ICallerContext
│   ├── McpErrorHandler.cs           # CHANGED: map gateway 401/403/404/429 → tool-result statuses
│   └── ServiceAvailabilityTracker.cs# retained
├── Services/
│   ├── McpAuthorizationService.cs   # CHANGED: tier-primary, role-secondary entitlement; advisory ListTools filter; service-tier rejection
│   ├── McpSessionService.cs         # REMOVED/REPLACED by ICallerContext
│   ├── ToolAuditService.cs          # CHANGED: record tier + tool + outcome + backend status
│   └── McpMetrics.cs                # NEW: Sorcha.Mcp meter
└── Tools/{Admin,Designer,Participant,Citizen}/  # CHANGED: every tool → typed ServiceClients method; tier tags; wallet_sign removed; new Citizen slice surfaces existing consumer endpoints

src/Common/Sorcha.ServiceClients.Http/
└── (ADDITIVE methods where a tool needs an operation no client method exposes, e.g. action execute, platform org create)

src/Services/Sorcha.ApiGateway/
└── appsettings.json                 # CHANGED: add /mcp YARP route to mcp-server; catalogue stays manifest source

tests/Sorcha.McpServer.Tests/
├── (existing unit tests updated for ICallerContext + tier mapping)
└── Integration/                     # NEW: Category=McpIntegration in-process tool invocation vs running gateway, per-tier tokens

docker-compose.yml                   # CHANGED: HTTP-mode mcp-server service (port on sorcha-network) alongside stdio profile
server.json                          # CHANGED: version pin; subject to integrity gate
```

**Structure Decision**: Single-app feature centred on `src/Apps/Sorcha.McpServer`, with additive shared-infra changes in `Sorcha.ServiceClients.Http`, a gateway route, a docker-compose service, and a new integration test area. No new service, no new persistence.

## Complexity Tracking

> No constitution violations — section intentionally empty.
