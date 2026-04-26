# Sorcha

A decentralised register platform for secure, multi-participant data flow orchestration built on .NET 10 and .NET Aspire.

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
| Runtime | .NET 10 / C# 14 | LTS runtime with latest features |
| Orchestration | .NET Aspire 13+ | Service discovery, health checks, telemetry |
| API | Minimal APIs + Scalar | REST endpoints with OpenAPI docs |
| Real-time | SignalR + Redis | WebSocket notifications |
| Databases | PostgreSQL / MongoDB / Redis | Relational, document, cache |
| Auth | JWT Bearer | Service-to-service and user authentication |
| Crypto | NBitcoin + Sorcha.Cryptography | HD wallets (BIP32/39/44), ED25519, P-256, RSA-4096 |
| Testing | xUnit + FluentAssertions + Moq | 1,200+ tests across 30 projects |

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
| Wallet | 98% | internal / 7001 | Crypto operations, HD wallets |
| Tenant | 98% | 5110 / 7110 | Multi-tenant auth, JWT issuer, Participant Identity, Platform Identity, Register Invitations |
| Validator | 95% | internal / 7004 | Consensus, chain integrity |
| Peer | 70% | 5002 / 7002 | P2P network, gRPC |
| API Gateway | 95% | 80 / 7082 | YARP reverse proxy |

**Designer UI:** `/designer/blueprint` is the canonical route (replaces legacy `/designer` and `/designer/chat`).

Full project tree: `docs/reference/project-structure.md`. Architecture diagrams: `docs/reference/architecture.md`.

---

## Feature API References

Feature-specific endpoint tables, domain models, and cross-cutting patterns for Participant Identity, Register Invitations, Trust Hardening (079), Stored Data / file attachments (085), Validator Roster (086), Org Key Derivation (083), Platform Org Topology, Consumer Persona (092), System Register Genesis (099), Open Participants / late binding (103), `x-review` / credential id-cards (107), ownership-agnostic submission / derived relationship (108), Timebound Presentation Lifecycle (111), and the transactional email architecture (112 — facade / template renderer / branding resolver / welcome dispatcher, see Tenant Service README) are consolidated in the **`sorcha-architecture`** skill (`.claude/skills/sorcha-architecture/SKILL.md`). Load it when touching any of those features — it carries what used to live inline here.

Full REST/gRPC reference: `docs/reference/API-DOCUMENTATION.md`.

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

### 6. Instance Reference Configuration
Blueprints should define an `instanceReference` to generate human-readable identifiers for workflow instances (e.g., "CP-RIV-14-A7K3"). The reference is auto-generated from first-action payload fields and stored as public metadata on the instance.
```json
"instanceReference": {
  "prefix": "CP",
  "components": [
    { "field": "/projectName", "transform": "FirstWord", "chars": 3 },
    { "field": "/siteAddress", "transform": "FirstWord", "chars": 3 }
  ]
}
```
- **prefix**: 1-5 uppercase alpha chars identifying the workflow type
- **components**: 1-5 field extractions from the starting action's schema
- **transforms**: `FirstWord` (split on space, take first), `Truncate` (take first N chars). All output is uppercased.
- A 4-char uniqueness hash is auto-appended
- The reference is **public metadata** — field values referenced here will be visible in plaintext

### 7. License Header (Required)
```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
```

### 8. Centralised Rate Limiting (SEC-002)
All services use `builder.AddRateLimiting()` from ServiceDefaults. Limits are driven by `RateLimitSettings` bound from `"RateLimiting"` in `appsettings.json`. **Do NOT add custom `AddRateLimiter` calls in individual services.**

```csharp
// All services — registers all standard policies
builder.AddRateLimiting();

// Endpoints reference shared policy names
.RequireRateLimiting(RateLimitPolicies.Api)           // default
.RequireRateLimiting(RateLimitPolicies.PlatformAuth)   // login/register
.RequireRateLimiting(RateLimitPolicies.TotpValidation) // 2FA
.RequireRateLimiting(RateLimitPolicies.Strict)         // wallet ops
```

Default values are very relaxed (100k/min) for pre-release development. Tighten in `appsettings.Production.json`. Inject `IOptions<RateLimitSettings>` for non-HTTP rate limiting (e.g. wallet notifications, MCP server).

### 9. Transactional Email (Feature 112)

All transactional email from the Tenant Service goes through `ITransactionalEmailService` — the single, templated entry point. **Do NOT call `IEmailSender.SendAsync` directly from application code**, and **do NOT build HTML bodies with string interpolation**.

```csharp
// Inject the facade — not IEmailSender
public MyService(ITransactionalEmailService transactional) { … }

// Typed dispatch records for each flow
await _transactional.SendVerificationAsync(new VerifyEmailDispatch(
    ToEmail: user.Email,
    DisplayName: user.DisplayName,
    VerifyUrl: verifyUrl,
    ExpiresInHours: 24), ct);
```

Six Scriban templates (`verify`, `invite`, `reset`, `welcome-public`, `welcome-invited` plus shared `base`) live as embedded resources in `src/Services/Sorcha.Tenant.Service/Emails/Templates/`. Per-org branding (logo + colour + tagline) applies only to `invite` and `welcome-invited`; all other templates stay Sorcha-branded.

Welcome emails fire exactly once per user via `WelcomeEmailDispatcher.SendIfPendingAsync` — idempotent (guarded by `PlatformUser.WelcomeSentAt`) and non-throwing. Call sites: end of `EmailVerificationService.VerifyTokenAsync`, tail of each `LoginService` success path, and `SocialCallback` Razor PageModel. **Do NOT add new welcome-email trigger sites without routing through the dispatcher.**

Snapshot fixtures for every template live at `tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails/`. Regenerate with `UPDATE_EMAIL_FIXTURES=1 dotnet test --filter "~EmailTemplateSnapshotTests"` when a template copy change is intentional. Full design-history and architecture: Tenant Service README → "Transactional Email Architecture (Feature 112)" section.

> Feature-specific patterns (Open Participants / late binding, `x-review`, ownership-agnostic submission) live in the `sorcha-architecture` skill.

### 10. Storage Registration Log (Feature 113)

All storage interface registrations go through `IStorageRegistrationLog` from `Sorcha.ServiceDefaults.Storage`. Service-specific storage wiring (`AddWalletDatabase`, Register/Blueprint Program.cs, etc.) MUST call `RegisterPersistent` or `RegisterInMemory` immediately after the matching `AddScoped` / `AddSingleton`. Resolve the log via `services.GetStorageRegistrationLog()` once at the top of the storage block; use `typeof(IFoo).FullName!` for interface and implementation names so namespace renames are caught at compile time.

```csharp
var storageLog = services.GetStorageRegistrationLog();
var interfaceName = typeof(IFooRepository).FullName!;

if (!string.IsNullOrEmpty(connectionString))
{
    services.AddScoped<IFooRepository, EfCoreFooRepository>();
    storageLog.RegisterPersistent(
        interfaceName,
        typeof(EfCoreFooRepository).FullName!,
        "postgres");
}
else
{
    services.AddSingleton<IFooRepository, InMemoryFooRepository>();
    storageLog.RegisterInMemory(
        interfaceName,
        typeof(InMemoryFooRepository).FullName!,
        "no Postgres connection string in ConnectionStrings:Service:Postgres or ConnectionStrings:Sorcha:Postgres");
}
```

Six interfaces are **audited** — they fail-fast at host startup in `Production` or `Staging` if registered with an in-memory implementation: `IWalletRepository`, `IRegisterRepository`, `IInstanceStore`, `IActionStore`, `IVerifiedTransactionQueue`, `IAtomicDistributedCache`. Cache-style stores (`IBlueprintStore`, `IPublishedBlueprintStore`, etc.) emit the warning but do not gate startup.

Operators who need to run a Production-flagged container against an ephemeral environment (CI smoke tests, debugging) can set `Storage:AllowInMemoryInProduction=true` to bypass fail-fast. The bypass logs at `LogCritical`.

Health check `storage-providers` reports `Degraded` when any audited interface is on an in-memory backend. OpenTelemetry instruments on the `Sorcha.Storage` meter — `sorcha_storage_provider_info` and `sorcha_storage_fallback_active` — surface the same state for dashboards and alerting.

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
| `docs/reference/project-structure.md` | Full source tree |
| `docs/reference/API-DOCUMENTATION.md` | Full REST/gRPC reference |
| `walkthroughs/README.md` | Interactive demos and test scripts |

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
- **`.claude/skills/sorcha-architecture/SKILL.md`** — If you add/change feature-specific endpoints or cross-cutting patterns
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

**Version:** 3.0 | **Updated:** 2026-04-21 | Built with .NET 10 and .NET Aspire
