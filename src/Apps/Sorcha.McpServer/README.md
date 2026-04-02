# Sorcha MCP Server

A Model Context Protocol (MCP) server for the Sorcha decentralised register platform. This server enables AI assistants like Claude Desktop to interact with Sorcha's Blueprint, Register, Wallet, and other services through a standardized protocol.

## Overview

The MCP server provides role-based access to Sorcha platform operations through a set of tools organized by user role:

- **Administrator (`sorcha:admin`)**: Platform health, logs, metrics, tenant/user management
- **Designer (`sorcha:designer`)**: Blueprint creation, validation, simulation, versioning
- **Participant (`sorcha:participant`)**: Inbox, actions, transactions, wallet operations

## Features

- **JWT Authentication**: Secure access using JWT bearer tokens from Tenant Service
- **Role-Based Authorization**: Tools are filtered based on user's assigned roles
- **Rate Limiting**: Protects backend services from excessive API calls
- **Audit Logging**: Tracks all tool invocations for security and compliance
- **Service Discovery**: Automatically connects to Sorcha backend services
- **Stdio Transport**: Standard MCP stdio protocol for AI assistant integration

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

```json
{
  "RateLimiting": {
    "PermitLimit": 100,
    "WindowSeconds": 60
  }
}
```

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

The MCP server auto-discovers tools from the assembly. Tools are organized by role:

### Administrator Tools (13 tools)
- Health checks and service status
- Log viewing and analysis
- Metrics and performance monitoring
- Tenant and user management
- System configuration

### Designer Tools (13+ tools)
- Blueprint creation and editing
- Schema validation
- Blueprint versioning
- Workflow simulation
- Template management

### Participant Tools (10+ tools)
- Action inbox viewing
- Transaction submission
- Wallet operations
- Register queries
- Notification management

## Security

- **JWT Validation**: All requests validate JWT tokens against configured authority
- **Role-Based Access**: Tools are filtered by user roles in JWT claims
- **Rate Limiting**: Prevents abuse through configurable rate limits
- **Audit Trail**: All tool invocations are logged with user context
- **Secure Defaults**: Minimal permissions, explicit grants required

## Development

### Project Dependencies

- `ModelContextProtocol` (v0.7.0-preview.1) - MCP SDK
- `Sorcha.ServiceClients` - Backend service communication
- `Sorcha.ServiceDefaults` - Shared configuration
- `FluentValidation` - Input validation
- `System.IdentityModel.Tokens.Jwt` - JWT authentication

### Adding New Tools

1. Create a tool class implementing `IMcpTool`
2. Decorate with `[McpTool]` attribute
3. Add role-based authorization attributes
4. Tool will be auto-discovered on startup

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
