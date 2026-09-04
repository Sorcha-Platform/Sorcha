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
├── Apps/                            # 8 standalone apps + the Sorcha.UI group (4 projects)
│   ├── Sorcha.AppHost/              # .NET Aspire orchestrator
│   ├── Sorcha.Agent/               # Autonomous rules/decision agent (e.g. AIAS)
│   ├── Sorcha.Cli/                  # Administrative CLI tool
│   ├── Sorcha.Demo/                 # Demo application
│   ├── Sorcha.McpServer/            # MCP Server for AI assistants
│   ├── Sorcha.Verifier/            # Desk verifier (web)
│   ├── Sorcha.Verifier.Pwa/        # Doorstep verifier (PWA)
│   ├── Sorcha.Wallet.Pwa/          # Citizen wallet (PWA)
│   └── Sorcha.UI/                   # Main Blazor Web App
│       ├── Sorcha.UI.Core/             # Admin/designer/explorer components
│       ├── Sorcha.UI.Components.User/  # Shared user-facing components (web + PWA)
│       ├── Sorcha.UI.Web/              # Web host
│       └── Sorcha.UI.Web.Client/       # Web (WASM) client
├── Common/                          # Cross-cutting libraries (26 projects)
│   ├── Sorcha.Blueprint.Models/     # Domain models with JSON-LD
│   ├── Sorcha.Cryptography/         # Multi-algorithm crypto
│   ├── Sorcha.Mdoc/                # ISO 18013-5 mDoc (F185 proximity)
│   ├── Sorcha.Proximity.*/         # BLE proximity sharing (F185)
│   ├── Sorcha.ServiceClients/       # HTTP/gRPC service clients
│   ├── Sorcha.ServiceDefaults/      # Aspire shared configuration
│   ├── Sorcha.Storage.*/            # Storage abstraction (In-memory / MongoDB / Redis)
│   ├── Sorcha.TransactionHandler/   # Transaction building/serialization
│   ├── Sorcha.Validator.Core/       # Enclave-safe validation
│   └── Sorcha.Wallet.Contracts/    # Canonical wallet HTTP DTOs
├── Core/                            # Business logic (10 projects)
│   ├── Sorcha.Blueprint.Engine/     # Portable execution (WASM-compatible library)
│   ├── Sorcha.Blueprint.Fluent/     # Fluent API for blueprint construction
│   ├── Sorcha.Verifier.Engine/     # Presentation verification
│   └── Sorcha.Register.*/           # Register storage (In-memory / MongoDB)
├── Providers/                       # 1 project (address-lookup)
└── Services/                        # 8 microservices
    ├── Sorcha.ApiGateway/           # YARP reverse proxy
    ├── Sorcha.Blueprint.Service/    # Workflow management + SignalR
    ├── Sorcha.Haip.Service/         # OpenID4VCI/VP external-wallet surface (HAIP 1.0)
    ├── Sorcha.Peer.Service/         # P2P networking (gRPC)
    ├── Sorcha.Register.Service/     # Distributed ledger + OData
    ├── Sorcha.Tenant.Service/       # Multi-tenant auth + JWT
    ├── Sorcha.Validator.Service/    # Consensus + chain integrity
    └── Sorcha.Wallet.Service/       # Crypto wallet management

tests/                               # 59 test projects (unit / integration / E2E)
├── *.Tests/                         # Unit tests per component
├── *.E2E.Tests/                     # Playwright E2E (e.g. Sorcha.UI.E2E.Tests)
└── ...
```

**Project count:** 57 source projects, 59 test projects (csproj-counted).

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
| Runtime | .NET 10 / C# 14 | LTS runtime |
| Orchestration | .NET Aspire 13+ | Service discovery, health checks, telemetry |
| API | Minimal APIs + Scalar | REST endpoints with OpenAPI docs |
| Real-time | SignalR + Redis | WebSocket notifications |
| Databases | PostgreSQL / MongoDB / Redis | Relational, document, cache |
| Auth | JWT Bearer | Service-to-service and user authentication |
| Crypto | NBitcoin + Sorcha.Cryptography | HD wallets, ED25519, P-256, RSA, ML-DSA, ML-KEM |
| Testing | xUnit + FluentAssertions + Moq | 15,000+ tests across 63 test projects |

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

**Use storage abstractions** — depend on a service-specific repository interface (there is **no** generic `IRepository<T>`; it was removed). The concrete backend (EF Core / MongoDB / Redis / in-memory) is chosen at registration time. See CLAUDE.md Pattern #5.
```csharp
public class WalletService(IWalletRepository repository) { }
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
| [docs/architecture.md](docs/architecture.md) | System architecture diagrams |
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

**Current:** MVD (Minimum Viable Deliverable) complete; hardening toward production. See `.specify/MASTER-TASKS.md` and `docs/reference/development-status.md` for current state (not pinned as a percentage here — it only goes stale).

See [docs/reference/development-status.md](docs/reference/development-status.md) for detailed component status.

### Remaining for Production

- Azure Key Vault integration (Wallet Service)
- Azure AD B2C (Tenant Service)
- Decentralized consensus / leader election
- Fork detection and enclave support
- BLS threshold coordination
- Cloud deployment templates
