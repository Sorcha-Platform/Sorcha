# Sorcha

A distributed ledger platform for secure, multi-participant data flow orchestration built on .NET 10 and .NET Aspire.

Sorcha implements the **DAD** (Disclosure, Alteration, Destruction) security model - creating cryptographically secured registers where disclosure is managed through defined schemas, alteration is recorded on immutable ledgers, and destruction risk is eliminated through peer network replication.

**Current Status:** 100% MVD Complete | Production Readiness: 30%

---

## Quick Start

```bash
# Prerequisites: .NET 10 SDK, Docker Desktop

# Start all services (recommended)
docker-compose up -d

# Access points:
# - API Gateway:      http://localhost:80
# - Main UI:          http://localhost/app
# - Aspire Dashboard: http://localhost:18888

# CLI tool (after build):
# dotnet run --project src/Apps/Sorcha.Cli -- --help

# Alternative: Run with Aspire (debugging with breakpoints)
dotnet run --project src/Apps/Sorcha.AppHost
# Services available on HTTPS ports (7000-7290)

# Build and test
dotnet restore && dotnet build && dotnet test
```

---

## Tech Stack

| Layer | Technology | Purpose |
|-------|------------|---------|
| Runtime | .NET 10 / C# 13 | LTS runtime with latest features |
| Orchestration | .NET Aspire 13+ | Service discovery, health checks, telemetry |
| API | Minimal APIs + Scalar | REST endpoints with OpenAPI docs |
| Real-time | SignalR + Redis | WebSocket notifications |
| Databases | PostgreSQL / MongoDB / Redis | Relational, document, cache |
| Auth | JWT Bearer | Service-to-service and user authentication |
| Crypto | NBitcoin + Sorcha.Cryptography | HD wallets (BIP32/39/44), ED25519, P-256, RSA-4096 |
| Testing | xUnit + FluentAssertions + Moq | 1,100+ tests across 30 projects |

---

## Architecture

```
┌─────────────┐     ┌─────────────────┐     ┌──────────────────┐
│  Sorcha UI  │────▶│   API Gateway   │────▶│  Blueprint Svc   │
│  (Blazor)   │     │    (YARP)       │     │  (Workflows)     │
└─────────────┘     └─────────────────┘     └────────┬─────────┘
                            │                         │
                    ┌───────┴───────┐        ┌───────┴────────┐
              ┌─────▼─────┐   ┌─────▼─────┐  │  ┌────────────▼┐
              │  Wallet   │   │ Register  │◀─┘  │  Validator  │
              │  Service  │   │  Service  │     │   Service   │
              └─────┬─────┘   └─────┬─────┘     └─────────────┘
              │PostgreSQL │   │  MongoDB  │     │   Redis     │
```

**Key Services:**
| Service | Status | Port (Docker/Aspire) | Purpose |
|---------|--------|---------------------|---------|
| Blueprint | 100% | 5000 / 7000 | Workflow management, SignalR |
| Register | 100% | 5290 / 7290 | Distributed ledger, OData |
| Wallet | 95% | internal / 7001 | Crypto operations, HD wallets |
| Tenant | 90% | 5110 / 7110 | Multi-tenant auth, JWT issuer, Participant Identity |
| Validator | 95% | internal / 7004 | Consensus, chain integrity |
| Peer | 70% | 5002 / 7002 | P2P network, gRPC |
| API Gateway | 95% | 80 / 7082 | YARP reverse proxy |

---

## Participant Identity API

The Participant Identity Registry bridges Tenant Service users with Blueprint workflow participants and their Wallet signing keys.

### Endpoints (via API Gateway /api/*)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/organizations/{orgId}/participants` | Register participant (admin) |
| GET | `/organizations/{orgId}/participants` | List org participants |
| GET | `/organizations/{orgId}/participants/{id}` | Get participant details |
| PUT | `/organizations/{orgId}/participants/{id}` | Update participant |
| DELETE | `/organizations/{orgId}/participants/{id}` | Deactivate participant |
| POST | `/participants/search` | Search across accessible orgs |
| GET | `/participants/by-wallet/{address}` | Lookup by wallet address |
| POST | `/participants/{id}/wallet-links` | Initiate wallet link challenge |
| POST | `/participants/{id}/wallet-links/{challengeId}/verify` | Verify wallet signature |
| GET | `/participants/{id}/wallet-links` | List linked wallet addresses |
| DELETE | `/participants/{id}/wallet-links/{linkId}` | Revoke wallet link |
| POST | `/me/register-participant` | Self-register as participant |
| GET | `/me/participant-profiles` | Get all user's participant profiles |

### On-Register Participant Publishing (Tenant Service)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/organizations/{orgId}/participants/publish` | Publish participant record to register |
| PUT | `/organizations/{orgId}/participants/publish/{participantId}` | Update published participant record |
| DELETE | `/organizations/{orgId}/participants/publish/{participantId}` | Revoke published participant record |

### Published Participant Queries (Register Service)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/registers/{registerId}/participants` | List published participants (status filter, pagination) |
| GET | `/registers/{registerId}/participants/by-address/{walletAddress}` | Get participant by wallet address |
| GET | `/registers/{registerId}/participants/{participantId}` | Get participant by ID |
| GET | `/registers/{registerId}/participants/by-address/{walletAddress}/public-key` | Resolve public key for encryption |

### Key Models

- **ParticipantIdentity**: User + Organization + Status + DisplayName
- **LinkedWalletAddress**: WalletAddress + VerifiedAt + Status (max 10 per participant)
- **WalletLinkChallenge**: Nonce + Expiration (5 min) for signature verification
- **PublishedParticipantRecord**: On-register identity with addresses, version, status
- **PublicKeyResolution**: Resolved public key for field-level encryption (410 Gone if revoked)

### Service Client

```csharp
// Use IParticipantServiceClient from Sorcha.ServiceClients
var participant = await participantClient.GetByIdAsync(orgId, participantId);
var canSign = await participantClient.ValidateSigningCapabilityAsync(orgId, participantId);
```

---

## Project Structure

```
src/
├── Apps/
│   ├── Sorcha.AppHost/              # .NET Aspire orchestrator
│   ├── Sorcha.Admin/                # Blazor WASM admin UI (host)
│   │   └── Sorcha.Admin.Client/     # Admin UI client components
│   ├── Sorcha.Cli/                  # Administrative CLI tool
│   ├── Sorcha.Demo/                 # Demo application
│   ├── Sorcha.McpServer/            # MCP Server for AI assistants (Claude Desktop, etc.)
│   └── Sorcha.UI/                   # Main UI application
│       ├── Sorcha.UI.Core/          # Shared UI components
│       ├── Sorcha.UI.Web/           # Web host
│       └── Sorcha.UI.Web.Client/    # Web client (Blazor WASM)
├── Common/
│   ├── Sorcha.Blueprint.Models/     # Domain models with JSON-LD
│   ├── Sorcha.Cryptography/         # Multi-algorithm crypto (ED25519, P-256, RSA)
│   ├── Sorcha.Register.Models/      # Register domain models
│   ├── Sorcha.ServiceClients/       # Consolidated HTTP/gRPC clients
│   ├── Sorcha.ServiceDefaults/      # Aspire shared configuration
│   ├── Sorcha.Storage.*/            # Storage abstraction layer (5 projects)
│   │   ├── Abstractions/            # IRepository<T>, IUnitOfWork interfaces
│   │   ├── EFCore/                  # Entity Framework Core implementation
│   │   ├── InMemory/                # In-memory implementation (testing)
│   │   ├── MongoDB/                 # MongoDB implementation
│   │   └── Redis/                   # Redis caching implementation
│   ├── Sorcha.Tenant.Models/        # Tenant domain models
│   ├── Sorcha.TransactionHandler/   # Transaction building/serialization
│   ├── Sorcha.Validator.Core/       # Enclave-safe validation library
│   └── Sorcha.Wallet.Core/          # Wallet domain logic
├── Core/
│   ├── Sorcha.Blueprint.Engine/     # Portable execution (WASM-compatible)
│   ├── Sorcha.Blueprint.Fluent/     # Fluent API for blueprint construction
│   ├── Sorcha.Blueprint.Schemas/    # Schema management with caching
│   ├── Sorcha.Register.Core/        # Ledger business logic
│   └── Sorcha.Register.Storage.*/   # Register-specific storage (3 projects)
│       ├── Sorcha.Register.Storage/ # Storage abstractions
│       ├── InMemory/                # In-memory implementation
│       └── MongoDB/                 # MongoDB implementation
└── Services/                        # 7 microservices
    ├── Sorcha.ApiGateway/           # YARP reverse proxy
    ├── Sorcha.Blueprint.Service/    # Workflow management
    ├── Sorcha.Peer.Service/         # P2P networking (gRPC)
    ├── Sorcha.Register.Service/     # Distributed ledger
    ├── Sorcha.Tenant.Service/       # Multi-tenant authentication
    ├── Sorcha.Validator.Service/    # Blockchain validation
    └── Sorcha.Wallet.Service/       # Crypto wallet management

tests/                               # 30 test projects
├── *.Tests/                         # Unit tests per component
├── *.IntegrationTests/              # Integration tests
├── *.PerformanceTests/              # Performance/load tests
└── Sorcha.UI.E2E.Tests/             # End-to-end Playwright tests
```

**Project Count:** 39 source projects, 30 test projects

---

## Development Guidelines

### File Naming
- **C# Files:** PascalCase (e.g., `WalletManager.cs`, `IActionStore.cs`)
- **Test Files:** `{ClassName}Tests.cs` (e.g., `WalletManagerTests.cs`)

### Code Naming
| Element | Convention | Example |
|---------|------------|---------|
| Classes/Interfaces | PascalCase, `I` prefix for interfaces | `WalletManager`, `IWalletService` |
| Methods/Properties | PascalCase | `CreateWalletAsync`, `IsEnabled` |
| Parameters/Variables | camelCase | `walletId`, `transactionData` |
| Private fields | _camelCase | `_repository`, `_logger` |
| Constants | PascalCase | `MaxRetryCount`, `DefaultTimeout` |
| Async methods | `Async` suffix | `ValidateAsync`, `ProcessAsync` |

### Test Naming
```csharp
// Pattern: MethodName_Scenario_ExpectedBehavior
public async Task ValidateAsync_ValidData_ReturnsValid() { }
public void Build_WithoutTitle_ThrowsInvalidOperationException() { }
```

### Import Order
```csharp
using System.Text.Json;           // 1. System
using Microsoft.Extensions.DI;    // 2. Microsoft
using FluentAssertions;           // 3. Third-party
using Sorcha.Blueprint.Models;    // 4. Sorcha
```

### Service Folder Structure
```
Services/Sorcha.*.Service/
├── Endpoints/           # Minimal API endpoint definitions
├── Extensions/          # Service collection extensions
├── GrpcServices/        # gRPC service implementations (if applicable)
├── Mappers/             # DTO/Model mapping
├── Models/              # Request/Response DTOs
├── Services/            # Business logic
│   ├── Interfaces/      # IWalletService, IKeyManagementService
│   └── Implementation/  # WalletManager, KeyManagementService
└── Program.cs           # Entry point
```

---

## Critical Patterns

### 1. Use Scalar for OpenAPI (NOT Swagger)
```csharp
// .NET 10 built-in OpenAPI with Scalar UI
app.MapPost("/api/wallets", handler)
    .WithName("CreateWallet")
    .WithSummary("Create a new wallet");
```

### 2. Use Consolidated Service Clients
```csharp
// Always use Sorcha.ServiceClients - NEVER create duplicate clients
builder.Services.AddServiceClients(builder.Configuration);
```

### 3. Blueprint Creation Policy
- **Primary:** Create blueprints as JSON or YAML files
- **Secondary:** Fluent API for programmatic/dynamic blueprint generation
```json
{ "title": "...", "participants": [...], "actions": [...] }
```

### 4. JsonSchema.Net Requires JsonElement
```csharp
// CRITICAL: Evaluate() expects JsonElement, not JsonNode
JsonElement element = JsonSerializer.Deserialize<JsonElement>(json);
var result = schema.Evaluate(element);
```

### 5. Storage Abstraction Pattern
```csharp
// Use IRepository<T> from Sorcha.Storage.Abstractions
public class WalletService
{
    private readonly IRepository<Wallet> _repository;
    public WalletService(IRepository<Wallet> repository) => _repository = repository;
}
```

### 6. License Header (Required)
```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
```

---

## Key Documentation

| Document | Purpose |
|----------|---------|
| `.specify/constitution.md` | Architectural principles (read first!) |
| `.specify/MASTER-TASKS.md` | Task tracking with priorities |
| `.specify/AI-CODE-DOCUMENTATION-POLICY.md` | MANDATORY documentation requirements |
| `docs/getting-started/PORT-CONFIGURATION.md` | Complete port assignments |
| `docs/guides/AUTHENTICATION-SETUP.md` | JWT configuration guide |
| `docs/reference/development-status.md` | Current completion status |
| `docs/reference/architecture.md` | System architecture diagrams |

---

## Context Management (Core Guideline)

**Problem:** Large files auto-loaded into every session waste context window. After compaction, sessions restart with 100KB+ of reference material that may not be relevant.

**Rules:**
1. **MASTER-TASKS.md** — Active work only. Completed phases archived to `MASTER-TASKS-ARCHIVE.md`. Read the archive on-demand when historical context is needed.
2. **MEMORY.md** — Cap at 50 lines. Active patterns and preferences only. No historical fix notes or completed branch details.
3. **Plan/spec files** — Read on-demand when implementing, not at session start. Large reference docs pollute compact summaries.
4. **Current work focus** — Store in MEMORY.md under `## Current Branch` with branch name, remaining tasks, and build status. Update on session end.
5. **On continue** — Check `MEMORY.md > Current Branch` section first. Only load the plan/task files if continuing that work.
6. **Settings permissions** — Use broad patterns (`Bash(*)`) not one-off approvals. Keep the list under 20 entries.

**On session end or before compact:**
- Update `MEMORY.md > Current Branch` with progress
- Do NOT read large reference files just to summarize them

---

## AI Assistant Requirements

### MANDATORY: Update these when generating code
1. `.specify/MASTER-TASKS.md` - Task status (📋 → 🚧 → ✅)
2. README files - If features/APIs changed
3. `docs/` files - If architecture/status changed
4. OpenAPI/XML docs - All endpoints documented

**PRs without documentation updates will NOT be approved.**

### Documentation Sync Policy

When modifying code, ensure corresponding documentation stays in sync:

- **Service README** — If you add/change endpoints, configuration, or features, update the service's README.md
- **docs/reference/API-DOCUMENTATION.md** — If you add/change REST or gRPC endpoints
- **docs/guides/AUTHENTICATION-SETUP.md** — If you change auth flows, policies, or token handling
- **docs/getting-started/PORT-CONFIGURATION.md** — If you add/change port assignments
- **docs/reference/development-status.md** — If you complete a feature or change service status
- **CLAUDE.md** — If you change architectural patterns or conventions
- **XML comments** — All public API methods must have `/// <summary>` to avoid build warnings
- **OpenAPI descriptions** — All Minimal API endpoints must have `.WithSummary()` and `.WithDescription()`

Documentation debt compounds quickly. A 2-minute doc update now prevents 30 minutes of confusion later.

### DO
- Read `.specify/constitution.md` before coding
- Check `.specify/MASTER-TASKS.md` for task priorities
- Write tests alongside code (>85% coverage)
- Use `Sorcha.ServiceClients` for HTTP calls
- Use `Sorcha.Cryptography` for crypto operations
- Use `Sorcha.Storage.*` for data persistence
- Reference task IDs in commits

### DON'T
- Use Swagger/Swashbuckle (use Scalar)
- Create duplicate service clients
- Use `JsonNode` with JsonSchema.Net (use `JsonElement`)
- Commit secrets or credentials
- Skip documentation updates
- Skip documentation updates when changing code (see Documentation Sync Policy above)
- Store mnemonics (user responsibility to backup)

---

## Commands

```bash
# Docker
docker-compose up -d                              # Start services
docker-compose logs -f <service>                  # View logs
docker-compose build <service> && docker-compose up -d --force-recreate <service>  # Rebuild

# MCP Server (for AI assistants)
docker-compose run mcp-server --jwt-token <token> # Run MCP server with JWT auth
# Or use environment variable:
# SORCHA_JWT_TOKEN=<token> docker-compose run mcp-server

# .NET Aspire
dotnet run --project src/Apps/Sorcha.AppHost      # Start with Aspire

# Build & Test
dotnet restore && dotnet build                    # Build solution
dotnet test                                       # Run all tests
dotnet test --filter "FullyQualifiedName~Blueprint"  # Filtered tests
dotnet test --collect:"XPlat Code Coverage"       # With coverage

# Code Quality
dotnet format                                     # Format code
```

---

## Claude Code Skills

| Command | Purpose |
|---------|---------|
| `/speckit.specify` | Create/update feature specification |
| `/speckit.plan` | Generate implementation plan |
| `/speckit.tasks` | Generate task list |
| `/speckit.implement` | Execute implementation |
| `/speckit.clarify` | Ask clarification questions |
| `/speckit.analyze` | Cross-artifact analysis |

---

## Walkthroughs

Interactive demos and test scripts are in `walkthroughs/`:

| Walkthrough | Status | Purpose |
|-------------|--------|---------|
| `BlueprintStorageBasic/` | ✅ | Docker startup, bootstrap, JWT auth |
| `AdminIntegration/` | ✅ | Blazor WASM behind API Gateway |
| `UserWalletCreation/` | 🚧 | User management, wallet creation |

See `walkthroughs/README.md` for guidelines on creating new walkthroughs.

---

## Branch & PR Policy

**All changes MUST go through branches and pull requests.** Direct pushes to `master` are blocked by GitHub branch protection.

```bash
# Standard workflow
git checkout -b feature/description    # Create branch
# ... make changes, commit ...
git push -u origin feature/description # Push branch
gh pr create --fill                    # Create PR
gh pr merge --squash                   # Merge after review
```

- Never commit directly to `master` — it will be rejected
- Use descriptive branch names: `feature/`, `fix/`, `docs/`, `chore/`
- PRs can be self-merged (0 approvals required for solo dev)
- Keep PRs focused — one logical change per PR

---

## Commit Format

```
feat: [TASK-ID] - Brief description

- Implementation details
- Documentation updated: README.md, MASTER-TASKS.md

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
```

---

**Version:** 2.5 | **Updated:** 2026-01-24 | Built with .NET 10 and .NET Aspire


## Skill Usage Guide

When working on tasks involving these technologies, invoke the corresponding skill:

| Skill | Invoke When |
|-------|-------------|
| postgresql | Manages PostgreSQL databases and Entity Framework Core integration |
| scalar | Generates and configures Scalar OpenAPI UI for API documentation |
| redis | Implements Redis caching and session management |
| signalr | Implements real-time WebSocket communication using SignalR |
| minimal-apis | Defines REST endpoints using Minimal APIs with OpenAPI documentation |
| yarp | Configures YARP reverse proxy for API gateway routing |
| mongodb | Configures MongoDB document storage and query operations |
| aspire | Configures .NET Aspire orchestration, service discovery, and telemetry |
| dotnet | Manages .NET 10 runtime, C# 13 syntax, and project configuration |
| blazor | Builds Blazor WASM components for admin and main UI applications |
| fluent-assertions | Creates readable test assertions with FluentAssertions library |
| grpc | Defines gRPC services for peer-to-peer network communication |
| entity-framework | Handles Entity Framework Core database access and migrations |
| moq | Mocks dependencies in unit tests using Moq framework |
| cryptography | Applies multi-algorithm cryptography (ED25519, P-256, RSA-4096) |
| jwt | Implements JWT Bearer authentication for service-to-service authorization |
| nbitcoin | Utilizes NBitcoin for HD wallet operations (BIP32/39/44) |
| xunit | Writes unit tests with xUnit framework across 30 test projects |
| docker | Manages Docker containerization and docker-compose orchestration |
| playwright | Develops end-to-end UI tests with Playwright for Blazor applications |
| frontend-design | Styles Blazor WASM components with CSS and responsive design patterns |
| sorcha-cli | Builds and maintains the Sorcha CLI tool using System.CommandLine 2.0.2, Refit HTTP clients, and Spectre.Console. Use when creating CLI commands, adding options/arguments, implementing Refit service clients, writing CLI tests, or fixing command structure issues |
| sorcha-ui | Builds Sorcha.UI Blazor WASM pages with accompanying Playwright E2E tests using the Docker test infrastructure |
| blueprint-builder | Creates blueprint JSON templates, defines participants/actions/routes/schemas, configures cycle detection, troubleshoots blueprint publishing |
