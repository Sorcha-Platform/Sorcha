# Phase 1 Data Model — MCP Server Foundation

This feature introduces no persisted entities. The "model" is the in-memory identity/authorization shape that flows through a tool invocation. All types live in `src/Apps/Sorcha.McpServer`.

## ICallerContext

The ambient identity behind the current invocation. One per process (stdio) or one per request (HTTP).

| Member | Type | Notes |
|---|---|---|
| `RawToken` | `string` | The caller's bearer, forwarded verbatim to backends. Never logged. |
| `Tier` | `Tier` | Parsed from the token `aud` via `SorchaAudiences`. |
| `Roles` | `IReadOnlySet<string>` | From the role claim; empty for consumer tier. |
| `OrganizationId` | `string?` | `org_id` claim (home/public org for consumer). |
| `Subject` | `string?` | `sub` claim. |
| `IsAuthenticated` | `bool` | True once a valid token is resolved. |

**Implementations**:
- `StdioCallerContext` — captures the startup `--jwt-token`/`SORCHA_JWT_TOKEN`, validates once, exposes it for the process lifetime.
- `HttpCallerContext` — wraps `IHttpContextAccessor`; reads the validated `ClaimsPrincipal` + raw `Authorization` bearer per request.

**Validation**: token validated against the installation issuer + tier audiences (`SorchaIssuer` / `SorchaAudiences`). Invalid / wrong-installation / missing-tier-audience → not authenticated → rejected before dispatch.

## Tier

The F136 trust boundary carried by the token audience.

| Value | Audience | MCP meaning |
|---|---|---|
| `Consumer` | `{installation}:consumer` | Citizen / wallet holder. Gets citizen self-service + participation tools. |
| `Platform` | `{installation}:platform` | Admin / operator / designer. Role sub-divides. |
| `Service` | `{installation}:service` | Internal mesh. **Rejected** as an MCP caller. |

(`enrol-session` audience is not a valid MCP caller and is treated like `Service`: rejected.)

## ToolEntitlement

Static metadata per tool driving the advisory `ListTools` filter and the per-invocation short-circuit.

| Field | Type | Notes |
|---|---|---|
| `ToolName` | `string` | e.g. `sorcha_action_submit`. |
| `Tiers` | `Tier[]` | Tier(s) permitted to see/invoke. Participation tools carry `[Consumer, Platform]`. |
| `RequiredRole` | `string?` | Platform-only role gate (e.g. admin, designer). Null = any of the listed tiers. |

**Slices** (from the tier mapping):
- Citizen self-service → `[Consumer]`.
- Workflow participation → `[Consumer, Platform]`, no required role.
- Designer → `[Platform]` + designer role.
- Operator/Admin → `[Platform]` + admin role.

**Invariant**: the entitlement check may only ever *narrow* (hide/refuse). It can never grant access the gateway would refuse. The gateway is authoritative.

## ToolResultStatus

The caller-facing outcome, mapped from backend responses by `McpErrorHandler`.

| Status | Source |
|---|---|
| `Success` | 2xx from the gateway. |
| `Unauthorized` | 401 (invalid/expired token) or local not-authenticated. |
| `Forbidden` | 403 (tier/role/ownership refused by the platform). |
| `NotFound` | 404 (resource absent — must NOT be caused by route drift after reconciliation). |
| `RateLimited` | 429 (carry Retry-After when present). |
| `Unavailable` | service down / connection failure (`ServiceAvailabilityTracker`). |
| `Error` | any other non-success. |

## Relationships

```
ICallerContext ──(raw token)──▶ CallerTokenForwardingHandler ──▶ Sorcha.ServiceClients ──▶ API Gateway ──▶ services
      │                                                                                         │
      └──(tier, roles)──▶ McpAuthorizationService ◀──(ToolEntitlement table)                   └──(401/403/404/429)──▶ McpErrorHandler ──▶ ToolResultStatus
                                  │
                                  └──▶ advisory ListTools filter (HTTP: via stateless ConfigureSessionOptions)
```

`ToolAuditService` records `{ tier, toolName, outcome, backendStatus }` per invocation; `McpMetrics` (`Sorcha.Mcp` meter) emits `sorcha_mcp_tool_invoked_total{tool,tier,outcome}` + `sorcha_mcp_backend_call_total{service,status}` + a latency histogram. Neither records token material.
