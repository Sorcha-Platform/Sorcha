# Sorcha MCP Server

A Model Context Protocol (MCP) server for the Sorcha decentralised register platform. This server enables AI assistants like Claude Desktop to interact with Sorcha's Blueprint, Register, Wallet, and other services through a standardized protocol.

## Overview

The MCP server provides role-based access to Sorcha platform operations through a set of tools organized by user role:

- **Administrator (`sorcha:admin`)**: platform health, logs, metrics, org/user admin, register federation, credential lifecycle, presentations
- **Designer (`sorcha:designer`)**: blueprint creation, validation, simulation, versioning
- **Participant (`sorcha:participant`)**: inbox, actions, transactions, wallet operations
- **Citizen (consumer tier)**: self-service wallet, devices, credentials, persona — gated on a consumer-tier token (F136), not a role claim

## Features

- **JWT Authentication**: Secure access using JWT bearer tokens from Tenant Service
- **Role-Based Authorization**: Tools are filtered based on user's assigned roles
- **Rate Limiting**: Protects backend services from excessive API calls
- **Audit Logging**: Tracks all tool invocations for security and compliance
- **Service Discovery**: Automatically connects to Sorcha backend services
- **Two Transports** (spec 139): stdio for local AI-assistant integration, and **stateless Streamable HTTP** served by `mcp-server-http` behind the gateway's `/mcp` route — probe `GET /.well-known/mcp.json` for the per-installation URL
- **Discoverability**: the gateway serves `/.well-known/mcp.json` (manifest: version, transports, real JWT issuer/audience for the installation) and `/api/mcp/tools` (flat catalogue). Both are guarded against drift by `ManifestIntegrityTests`

## Running with Docker

### Using docker-compose

The MCP server is included in the docker-compose configuration with the `tools` profile:

```bash
# Run MCP server with JWT token
docker-compose run mcp-server --jwt-token <your-jwt-token>

# Or use environment variable
SORCHA_JWT_TOKEN=<your-jwt-token> docker-compose run mcp-server
```

### Building the Docker image

```bash
# Build the image
docker-compose build mcp-server

# Run interactively
docker-compose run --rm mcp-server --jwt-token <token>
```

## Running Locally (Development)

```bash
# Navigate to project directory
cd src/Apps/Sorcha.McpServer

# Run with JWT token
dotnet run -- --jwt-token <your-jwt-token>

# Or set environment variable
export SORCHA_JWT_TOKEN=<your-jwt-token>
dotnet run
```

## Configuration

The MCP server uses standard .NET configuration with the following sources (in order of precedence):

1. Command-line arguments (`--jwt-token`)
2. Environment variables (prefix: `SORCHA_`)
3. `appsettings.{Environment}.json`
4. `appsettings.json`

### Key Configuration Sections

#### Service Clients

```json
{
  "ServiceClients": {
    "BlueprintService": {
      "Address": "http://blueprint-service:8080"
    },
    "RegisterService": {
      "Address": "http://register-service:8080"
    },
    "WalletService": {
      "Address": "http://wallet-service:8080"
    },
    "TenantService": {
      "Address": "http://tenant-service:8080"
    },
    "ValidatorService": {
      "Address": "http://validator-service:8080"
    }
  }
}
```

#### Rate Limiting

MCP rate limits use the centralised `RateLimiting` section from `RateLimitSettings` in ServiceDefaults:

```json
{
  "RateLimiting": {
    "McpPerUserRequestsPerMinute": 100,
    "McpPerTenantRequestsPerMinute": 1000,
    "McpAdminToolsRequestsPerMinute": 50
  }
}
```

Default values are very relaxed for development. Tighten these in production `appsettings.Production.json`.

## Getting a JWT Token

Use the utility script to get a JWT token:

**PowerShell:**
```powershell
.\scripts\get-jwt-token.ps1 -Email "admin@sorcha.local" -Password "Admin123!"
```

**Bash:**
```bash
./scripts/get-jwt-token.sh -e admin@sorcha.local -p Admin123!
```

Or get it manually via API:
```bash
curl -X POST http://localhost/api/tenant/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@sorcha.local","password":"Admin123!"}'
```

## Integration with Claude Desktop

To use the MCP server with Claude Desktop:

1. Obtain a JWT token (see above)
2. Configure Claude Desktop's MCP settings to launch the server:

```json
{
  "mcpServers": {
    "sorcha": {
      "command": "docker-compose",
      "args": ["run", "--rm", "mcp-server", "--jwt-token", "<your-token>"],
      "cwd": "/path/to/sorcha"
    }
  }
}
```

## Available Tools

**64 registered tools**, auto-discovered from `[McpServerToolType]` classes. Do not hand-count from
this README — the authoritative catalogue is `GET /api/mcp/tools` (or a live `tools/list`), and
`ManifestIntegrityTests` fails the build if the gateway catalogue or `server.json` drifts from the
served set.

| Slice | Tools | Surface |
|---|---|---|
| Admin | 34 | health, logs, metrics, org/user admin + audit, platform settings, register stats/subscribe/sync/federation, validator control, credential lifecycle (offer/suspend/reinstate/revoke/refresh), presentations |
| Designer | 13 | blueprint create/validate/simulate/version, schema + template management |
| Participant | 10 | inbox, pending actions, action submission, transactions, wallet ops |
| Citizen | 8 | self-service wallet, devices (list/rename/revoke), credentials, persona |

`tools/list` on a live session is **tier-filtered** (F136): a platform-tier token sees ~56 of 64;
consumer-only tools require a consumer-tier token. One further tool (`sorcha_wallet_sign`) exists in
source but is deliberately unregistered (T029 — signing stays in the Wallet Service).

## Security

- **JWT Validation**: All requests validate JWT tokens against configured authority
- **Role-Based Access**: Tools are filtered by user roles in JWT claims
- **Rate Limiting**: Prevents abuse through configurable rate limits
- **Audit Trail**: All tool invocations are logged with user context
- **Secure Defaults**: Minimal permissions, explicit grants required

## Development

### Project Dependencies

- `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` (v1.4.1, centrally pinned in `Directory.Packages.props`) - MCP C# SDK
- `Sorcha.ServiceClients` - Backend service communication
- `Sorcha.ServiceDefaults` - Shared configuration
- `FluentValidation` - Input validation
- `System.IdentityModel.Tokens.Jwt` - JWT authentication

### Adding New Tools

1. Create a class under `Tools/<Slice>/` marked `[McpServerToolType]`
2. Mark the tool method `[McpServerTool(Name = "sorcha_...")]` with a `[Description]` of **at least two sentences** (FR-017 — the catalogue test checks the name, reviewers check the prose)
3. Enforce the caller's tier/role inside the tool via `ICallerContext` (tools are dispatch-filtered per tier, and each tool re-checks — defence in depth)
4. Add the tool name to BOTH the gateway `appsettings.json` `McpManifest` catalogue and the repo-root `server.json` — `ManifestIntegrityTests` fails the build until all three agree
5. Point the tool at a route a service actually maps. `scripts/check-mcp-routes.ps1` (CI: `mcp-routes-gate`) extracts every `api/…` request path a `[McpServerToolType]` class issues — inline against `HttpClient` **and** inside the typed `Sorcha.ServiceClients*` methods it calls — reduces both sides to a route family (query dropped, route parameters collapsed to `*`), and fails when no `MapGroup`/`Map<Verb>` in `src/Services/**` maps it. Nothing else verifies that join: an unmapped path compiles fine and reaches the agent as a generic "failed to retrieve", so a permanently broken tool reads as a transient outage. Known-broken families are ratcheted in `.mcp-routes-allowlist`, which may only shrink

Example:

```csharp
[McpTool("create_blueprint")]
[RequireRole("sorcha:designer")]
public class CreateBlueprintTool : IMcpTool
{
    // Implementation
}
```

## Testing

```bash
# Run unit tests
dotnet test tests/Sorcha.McpServer.Tests

# Run with test coverage
dotnet test tests/Sorcha.McpServer.Tests --collect:"XPlat Code Coverage"
```

## Troubleshooting

### "JWT token is required"

Ensure you provide the JWT token via `--jwt-token` argument or `SORCHA_JWT_TOKEN` environment variable.

### "Service unavailable"

Check that required backend services are running and accessible:

```bash
# Verify services are up
docker-compose ps

# Check service logs
docker-compose logs -f blueprint-service
```

### Connection refused

Verify service addresses in configuration match Docker network DNS names (e.g., `http://blueprint-service:8080`).

## License

SPDX-License-Identifier: MIT
Copyright (c) 2026 Sorcha Contributors
