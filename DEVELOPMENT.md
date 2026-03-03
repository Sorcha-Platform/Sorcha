# Development Guide

This document covers building Sorcha from source, running tests, project structure, coding conventions, and contributing.

For user-facing setup and usage, see [README.md](README.md).

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (version 10.0.100+)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)
- [Git](https://git-scm.com/)
- A code editor: [Visual Studio 2025](https://visualstudio.microsoft.com/), [VS Code](https://code.visualstudio.com/) with C# extension, or [JetBrains Rider](https://www.jetbrains.com/rider/)

## Build & Run

```bash
# Restore, build, and test
dotnet restore && dotnet build && dotnet test

# Run with Docker Compose (recommended for day-to-day development)
docker-compose up -d

# Run with .NET Aspire (for debugging with breakpoints)
dotnet run --project src/Apps/Sorcha.AppHost
```

### Docker Development Workflow

Docker Compose is the primary development environment. After code changes:

```bash
# Rebuild a single service
docker-compose build <service-name> --no-cache
docker-compose up -d --force-recreate <service-name>

# Or use the helper script
pwsh scripts/rebuild-service.ps1 <service-name>

# View logs
docker-compose logs -f <service-name>

# Full reset
docker-compose down -v && docker-compose up -d
```

See [docs/getting-started/DOCKER-DEVELOPMENT-WORKFLOW.md](docs/getting-started/DOCKER-DEVELOPMENT-WORKFLOW.md) for detailed workflows.

### .NET Aspire (Debugging)

Use Aspire when you need Visual Studio breakpoints:

```bash
dotnet run --project src/Apps/Sorcha.AppHost
```

Access points with Aspire:
- Aspire Dashboard: `http://localhost:18888`
- API Gateway: `https://localhost:7082`
- Sorcha UI: `https://localhost:7083`
- Tenant Service: `https://localhost:7110`
- Blueprint Service: `https://localhost:7000`
- Register Service: `https://localhost:7290`

See [docs/getting-started/PORT-CONFIGURATION.md](docs/getting-started/PORT-CONFIGURATION.md) for all port assignments.

## Project Structure

```
src/
├── Apps/
│   ├── Sorcha.AppHost/              # .NET Aspire orchestrator
│   ├── Sorcha.Admin/                # Blazor WASM admin UI
│   ├── Sorcha.Cli/                  # Administrative CLI tool
│   ├── Sorcha.Demo/                 # Demo application
│   ├── Sorcha.McpServer/            # MCP Server for AI assistants
│   └── Sorcha.UI/                   # Main Blazor WASM application
│       ├── Sorcha.UI.Core/          # Shared UI components
│       ├── Sorcha.UI.Web/           # Web host
│       └── Sorcha.UI.Web.Client/    # Web client
├── Common/                          # Cross-cutting libraries (15 projects)
│   ├── Sorcha.Blueprint.Models/     # Domain models with JSON-LD
│   ├── Sorcha.Cryptography/         # Multi-algorithm crypto
│   ├── Sorcha.ServiceClients/       # Consolidated HTTP/gRPC clients
│   ├── Sorcha.ServiceDefaults/      # Aspire shared configuration
│   ├── Sorcha.Storage.*/            # Storage abstraction (5 projects)
│   ├── Sorcha.TransactionHandler/   # Transaction building/serialization
│   ├── Sorcha.Validator.Core/       # Enclave-safe validation
│   └── Sorcha.Wallet.Core/         # Wallet domain logic
├── Core/                            # Business logic (5 projects)
│   ├── Sorcha.Blueprint.Engine/     # Portable execution (WASM-compatible)
│   ├── Sorcha.Blueprint.Fluent/     # Fluent API for blueprint construction
│   ├── Sorcha.Blueprint.Schemas/    # Schema management with caching
│   └── Sorcha.Register.*/           # Register storage (3 projects)
└── Services/                        # 7 microservices
    ├── Sorcha.ApiGateway/           # YARP reverse proxy
    ├── Sorcha.Blueprint.Service/    # Workflow management + SignalR
    ├── Sorcha.Peer.Service/         # P2P networking (gRPC)
    ├── Sorcha.Register.Service/     # Distributed ledger + OData
    ├── Sorcha.Tenant.Service/       # Multi-tenant auth + JWT
    ├── Sorcha.Validator.Service/    # Consensus + chain integrity
    └── Sorcha.Wallet.Service/       # Crypto wallet management

tests/                               # 30 test projects
├── *.Tests/                         # Unit tests per component
├── *.IntegrationTests/              # Integration tests
├── *.PerformanceTests/              # Performance/load tests
└── Sorcha.UI.E2E.Tests/             # Playwright E2E tests
```

**Project count:** 39 source projects, 30 test projects

### Service Folder Convention

```
Services/Sorcha.*.Service/
├── Endpoints/           # Minimal API endpoint definitions
├── Extensions/          # Service collection extensions
├── GrpcServices/        # gRPC service implementations
├── Mappers/             # DTO/Model mapping
├── Models/              # Request/Response DTOs
├── Services/            # Business logic
│   ├── Interfaces/
│   └── Implementation/
└── Program.cs           # Entry point
```

## Tech Stack

| Layer | Technology | Purpose |
|-------|------------|---------|
| Runtime | .NET 10 / C# 13 | LTS runtime |
| Orchestration | .NET Aspire 13+ | Service discovery, health checks, telemetry |
| API | Minimal APIs + Scalar | REST endpoints with OpenAPI docs |
| Real-time | SignalR + Redis | WebSocket notifications |
| Databases | PostgreSQL / MongoDB / Redis | Relational, document, cache |
| Auth | JWT Bearer | Service-to-service and user authentication |
| Crypto | NBitcoin + Sorcha.Cryptography | HD wallets, ED25519, P-256, RSA, ML-DSA, ML-KEM |
| Testing | xUnit + FluentAssertions + Moq | 1,100+ tests across 30 projects |

## Testing

```bash
# Run all tests
dotnet test

# Filtered tests
dotnet test --filter "FullyQualifiedName~Blueprint"

# With coverage
dotnet test --collect:"XPlat Code Coverage"

# Watch mode
dotnet watch test --project tests/Sorcha.Blueprint.Engine.Tests

# E2E tests (requires Playwright setup)
cd tests/Sorcha.UI.E2E.Tests
dotnet build
pwsh bin/Debug/net10.0/playwright.ps1 install --with-deps
dotnet test

# Performance tests
dotnet run --project tests/Sorcha.Performance.Tests --configuration Release -- http://localhost:5000 30 50
```

### Test Naming Convention

```csharp
// Pattern: MethodName_Scenario_ExpectedBehavior
public async Task ValidateAsync_ValidData_ReturnsValid() { }
public void Build_WithoutTitle_ThrowsInvalidOperationException() { }
```

## Coding Conventions

### Naming

| Element | Convention | Example |
|---------|------------|---------|
| Classes/Interfaces | PascalCase, `I` prefix for interfaces | `WalletManager`, `IWalletService` |
| Methods/Properties | PascalCase | `CreateWalletAsync`, `IsEnabled` |
| Parameters/Variables | camelCase | `walletId`, `transactionData` |
| Private fields | _camelCase | `_repository`, `_logger` |
| Constants | PascalCase | `MaxRetryCount`, `DefaultTimeout` |
| Async methods | `Async` suffix | `ValidateAsync`, `ProcessAsync` |

### Import Order

```csharp
using System.Text.Json;           // 1. System
using Microsoft.Extensions.DI;    // 2. Microsoft
using FluentAssertions;           // 3. Third-party
using Sorcha.Blueprint.Models;    // 4. Sorcha
```

### License Header (Required)

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
```

### Critical Patterns

**Use Scalar for OpenAPI** (not Swagger):
```csharp
app.MapPost("/api/wallets", handler)
    .WithName("CreateWallet")
    .WithSummary("Create a new wallet");
```

**Use consolidated service clients**:
```csharp
builder.Services.AddServiceClients(builder.Configuration);
```

**Use storage abstractions**:
```csharp
public class WalletService(IRepository<Wallet> repository) { }
```

**JsonSchema.Net requires JsonElement** (not JsonNode):
```csharp
JsonElement element = JsonSerializer.Deserialize<JsonElement>(json);
var result = schema.Evaluate(element);
```

## Branch & PR Policy

All changes go through branches and pull requests. Direct pushes to `master` are blocked.

```bash
git checkout -b feature/description
# ... make changes, commit ...
git push -u origin feature/description
gh pr create --fill
gh pr merge --squash
```

### Commit Format

```
feat: [TASK-ID] - Brief description

- Implementation details
- Documentation updated

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>
```

## Key Documentation

| Document | Purpose |
|----------|---------|
| [.specify/constitution.md](.specify/constitution.md) | Architectural principles |
| [.specify/MASTER-TASKS.md](.specify/MASTER-TASKS.md) | Task tracking with priorities |
| [docs/reference/architecture.md](docs/reference/architecture.md) | System architecture diagrams |
| [docs/reference/API-DOCUMENTATION.md](docs/reference/API-DOCUMENTATION.md) | REST and gRPC endpoints |
| [docs/guides/AUTHENTICATION-SETUP.md](docs/guides/AUTHENTICATION-SETUP.md) | JWT configuration |
| [docs/getting-started/PORT-CONFIGURATION.md](docs/getting-started/PORT-CONFIGURATION.md) | Port assignments |
| [CLAUDE.md](CLAUDE.md) | AI assistant development guidelines |

## Scripts

Operational scripts in `scripts/`:

| Script | Purpose |
|--------|---------|
| `setup.ps1` / `setup.sh` | Main setup orchestrator |
| `bootstrap-sorcha.ps1` / `.sh` | First-run bootstrap |
| `rebuild-service.ps1` / `.sh` | Rebuild Docker service |
| `seed-tenant-service.ps1` / `.sh` | Seed tenant data |
| `get-jwt-token.ps1` / `.sh` | Generate JWT tokens |
| `reset-docker-state.ps1` / `.sh` | Clean Docker state |
| `push-to-dockerhub.ps1` / `.sh` | Push Docker images |

See `scripts/README.md` for the full list.

## Development Status

**Current:** 100% MVD (Minimum Viable Deliverable) | Production Readiness: 30%

See [docs/reference/development-status.md](docs/reference/development-status.md) for detailed component status.

### Remaining for Production

- Azure Key Vault integration (Wallet Service)
- Azure AD B2C (Tenant Service)
- Decentralized consensus / leader election
- Fork detection and enclave support
- BLS threshold coordination
- Cloud deployment templates
