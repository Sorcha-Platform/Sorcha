---
name: aspire
description: |
  Configures .NET Aspire 13.x orchestration, service discovery, and telemetry.
  Use when: Adding services to AppHost, configuring service defaults, setting up health checks, troubleshooting service discovery, or using Aspire CLI commands.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash, mcp__context7__resolve-library-id, mcp__context7__query-docs
---

# .NET Aspire 13.x Skill

.NET Aspire 13.x provides orchestration for this decentralised register platform. The AppHost (`src/Apps/Sorcha.AppHost/AppHost.cs`) orchestrates 7 microservices with PostgreSQL, MongoDB, and Redis. Services use `AddServiceDefaults()` for consistent OpenTelemetry, health checks, and service discovery. JWT signing keys are generated once and shared across all services via environment variables.

## Version Info

| Component | Version | Notes |
|-----------|---------|-------|
| Aspire.AppHost.Sdk | 13.0.0 | SDK in `.csproj` — legacy dual-SDK format |
| Aspire.Hosting.* | 13.1.0 | AppHost hosting packages |
| Aspire.StackExchange.Redis | 13.1.0 | Service-level Redis integration |
| Aspire.Hosting.Testing | 13.1.0 | Integration/E2E test infrastructure |
| Aspire Dashboard | 9.0.0 | Docker image (separate versioning) |

**Sorcha Package Locations:**
- AppHost: `src/Apps/Sorcha.AppHost/Sorcha.AppHost.csproj`
- ServiceDefaults: `src/Common/Sorcha.ServiceDefaults/Extensions.cs`
- Services: Blueprint, Register, Wallet, Validator (each has `Aspire.StackExchange.Redis`)
- Tests: Gateway Integration, Wallet Integration, UI E2E (each has `Aspire.Hosting.Testing`)

## Quick Start

### Adding a Service to AppHost

```csharp
// In AppHost.cs - Add project with resource references
var myService = builder.AddProject<Projects.Sorcha_MyService>("my-service")
    .WithReference(redis)                                    // Service discovery
    .WithReference(walletDb)                                // Database connection
    .WithEnvironment("JwtSettings__SigningKey", jwtSigningKey); // Shared config
```

### Named References (v13+)

```csharp
// Control the environment variable prefix for a resource reference
var myService = builder.AddProject<Projects.Sorcha_MyService>("my-service")
    .WithReference(postgres.AddDatabase("mydb"), "primary-db");  // ConnectionStrings__primary-db
```

### Consuming Aspire in a Service

```csharp
// In Program.cs - Every service starts with this
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();                    // OpenTelemetry, health checks, discovery
builder.AddRedisOutputCache("redis");            // Reference by resource name
builder.AddRedisDistributedCache("redis");

var app = builder.Build();
app.MapDefaultEndpoints();                       // /health and /alive
```

## Key Concepts

| Concept | Usage | Example |
|---------|-------|---------|
| Resource Name | Identifier for service discovery | `"redis"`, `"tenant-service"` |
| WithReference | Injects connection string/URL | `.WithReference(postgres)` |
| Named Reference | Custom env var prefix (v13+) | `.WithReference(db, "mydb")` |
| WithEnvironment | Pass config to service | `.WithEnvironment("Key", value)` |
| WithExternalHttpEndpoints | Expose outside Aspire network | `.WithExternalHttpEndpoints()` |
| AddServiceDefaults | Shared Aspire configuration | `builder.AddServiceDefaults()` |
| Connection Properties | Access individual fields (v13+) | `resource.GetConnectionProperty("HostName")` |
| WithHttpsCertificate | TLS termination (v13.1+) | `.WithHttpsCertificate(cert)` |
| ContainerRegistryResource | Registry config (v13.1+ exp.) | `builder.AddContainerRegistry(...)` |

## What's New in Aspire 13.x

### v13.0 Highlights
- **New SDK format**: Single `<Project Sdk="Aspire.AppHost.Sdk/13.0.0">` replaces dual-SDK
- **Polyglot connections**: Resources expose `HostName`, `Port`, `JdbcConnectionString`
- **Named references**: `WithReference(resource, "customName")` for env var prefixes
- **MCP dashboard**: Aspire dashboard exposes MCP endpoints for AI assistants
- **Container file artifacts**: `PublishWithContainerFiles()` for frontend-in-backend
- **Pipeline system**: `aspire do` for coordinated build/publish/deploy
- **aspire init**: Interactive solution initialization and discovery
- **VS Code extension**: Native project creation, debugging, deployment

### v13.1 Highlights
- **aspire mcp init**: Configure MCP for AI coding agents
- **TLS termination**: `WithHttpsCertificate()` for YARP, Redis, Keycloak, Vite
- **Container registry**: Experimental `ContainerRegistryResource`
- **Dashboard Parameters tab**: Dedicated parameter inspection
- **GenAI visualizer**: Tool definitions, evaluations, preview media
- **Azure Managed Redis**: Replaces `AddAzureRedisEnterprise()`
- **DevTunnels stabilized**: No longer preview

### Breaking Changes (v9 -> v13)
| Old API | New API | Notes |
|---------|---------|-------|
| `AddNpmApp()` | `AddJavaScriptApp()` | Removed in v13 |
| `AddNodeApp()` | Refactored | Different parameter ordering |
| `Aspire.Hosting.NodeJs` | `Aspire.Hosting.JavaScript` | Package renamed |
| `.Model` property | `.ModelName` | OpenAI/GitHub models |
| `.Database` property | `.DatabaseName` | Milvus/MongoDB/MySQL/Oracle |
| Dual-SDK `.csproj` | Single SDK `.csproj` | Optional migration |

## Sorcha AppHost Architecture

```
AppHost.cs orchestrates:
  postgres ──┬── tenant-db ───── tenant-service
             └── wallet-db ───── wallet-service
  mongodb ───── register-db ──── register-service
  redis ────── (shared by all services)

  tenant-service ──── (JWT issuer, auth provider)
  blueprint-service ─── (workflow engine)
  validator-service ──── wallet-service, register-service, peer-service, blueprint-service
  api-gateway ──── (routes to all services, external HTTP)
  ui-web ──── api-gateway (Blazor WASM frontend)
```

## Common Patterns

### Database Resources

```csharp
// PostgreSQL with multiple databases
var postgres = builder.AddPostgres("postgres").WithPgAdmin();
var tenantDb = postgres.AddDatabase("tenant-db", "sorcha_tenant");
var walletDb = postgres.AddDatabase("wallet-db", "sorcha_wallet");

// MongoDB for document storage
var mongodb = builder.AddMongoDB("mongodb").WithMongoExpress();
var registerDb = mongodb.AddDatabase("register-db", "sorcha_register");

// Redis for caching
var redis = builder.AddRedis("redis").WithRedisCommander();
```

### Service Dependencies

```csharp
// Service references other services for discovery
var validatorService = builder.AddProject<Projects.Sorcha_Validator_Service>("validator-service")
    .WithReference(redis)
    .WithReference(walletService)
    .WithReference(registerService)
    .WithReference(peerService)
    .WithReference(blueprintService);
```

### Health Endpoints

```csharp
// ServiceDefaults provides these automatically
app.MapHealthChecks("/health");           // Readiness - all checks
app.MapHealthChecks("/alive", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live")  // Liveness - tagged checks only
});
```

### TLS Termination (v13.1+)

```csharp
// Configure HTTPS certificates on containers
var redis = builder.AddRedis("redis")
    .WithHttpsCertificate(cert);

// YARP with TLS
var gateway = builder.AddProject<Projects.Sorcha_ApiGateway>("api-gateway")
    .WithHttpsCertificate();
```

## See Also

- [patterns](references/patterns.md) - AppHost patterns, SDK format, named references, TLS, MCP
- [workflows](references/workflows.md) - CLI commands, testing, deployment, troubleshooting

## Related Skills

- See the **dotnet** skill for .NET 10 patterns
- See the **minimal-apis** skill for endpoint configuration
- See the **redis** skill for cache configuration
- See the **postgresql** skill for database patterns
- See the **mongodb** skill for document storage
- See the **docker** skill for containerization

## Documentation Resources

> Fetch latest .NET Aspire documentation with Context7.

**How to use Context7:**
1. Use `mcp__context7__resolve-library-id` to search for "aspire"
2. **Prefer website documentation** (IDs starting with `/websites/`) over source code repositories when available
3. Query with `mcp__context7__query-docs` using the resolved library ID

**Library ID:** `/dotnet/docs-aspire` _(High reputation, 3264 code snippets)_

**Recommended Queries:**
- "AppHost service orchestration configuration"
- "service discovery WithReference patterns"
- "OpenTelemetry observability setup"
- "health checks readiness liveness"
- "aspire 13 breaking changes migration"
- "TLS termination WithHttpsCertificate"
- "MCP model context protocol dashboard"
