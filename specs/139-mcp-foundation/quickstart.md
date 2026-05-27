# Quickstart — MCP Server Foundation

How to run and verify the MCP server in both transports, with tier-scoped tokens. Assumes the local Docker stack is up (`docker-compose up -d`) and the gateway is reachable.

## Mint tier-scoped tokens

```bash
# Platform-admin (operator/admin + designer tools)
scripts/get-jwt-token.sh   # admin@sorcha.local / Dev_Pass_2025!  → platform-tier token

# Consumer (citizen) — obtain via a /wallet returnTo login or the enrol-redeem path
# Platform-designer — a platform user holding the designer role
```

Tokens carry the F136 tier in `aud` (`{installation}:consumer|platform|service`) and, for platform, the role claim. The MCP server derives the caller's tool surface from these.

## Run — stdio (local operator)

```bash
dotnet run --project src/Apps/Sorcha.McpServer -- --transport stdio --jwt-token <platform-admin-token>
# or: SORCHA_JWT_TOKEN=<token> dotnet run --project src/Apps/Sorcha.McpServer
```

Tools call the API Gateway carrying your token; the gateway enforces what you may do.

## Run — Streamable HTTP (remote agent)

```bash
dotnet run --project src/Apps/Sorcha.McpServer -- --transport http
# served at the MCP HTTP endpoint; exposed via the gateway as /mcp
```

The HTTP endpoint requires a valid bearer per request (rejected before any tool runs). Point an MCP client at the gateway `/mcp` URL with `Authorization: Bearer <token>`.

## Verify behaviour

| Check | Expectation |
|---|---|
| `ListTools` with a **consumer** token | Participation + citizen-read tools appear; no admin/designer tools. (Was empty before this feature.) |
| `ListTools` with a **platform-admin** token | Operator/admin + participation tools. |
| `ListTools` with a **platform-designer** token | Designer + participation tools. |
| Invoke an admin tool with a **consumer** token | `Forbidden` — refused by the **gateway**, not just locally. |
| Invoke `sorcha_action_submit` as a participant | Workflow advances; a transaction id is returned (real `/execute`, not a 404). |
| Connect with a **service**-tier token | Rejected at the door. |
| HTTP request with no/invalid bearer | Rejected before dispatch. |

## Run the integration safety net

```bash
# Docker stack must be running
dotnet test tests/Sorcha.McpServer.Tests --filter "Category=McpIntegration"
```

Invokes every advertised tool in-process against the running gateway with real per-tier tokens, asserting real success shapes or expected refusals. INFRA-skips when no stack is present (consistent with the repo's other Docker-gated suites).

## Manifest integrity

```bash
dotnet test tests/Sorcha.McpServer.Tests --filter "FullyQualifiedName~ManifestIntegrity"
```

Fails if the gateway `appsettings.json` catalogue or `server.json` drifts from the reflected `[McpServerTool]` set.
