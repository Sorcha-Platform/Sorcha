# Sorcha

A decentralised register platform for secure, multi-participant data flow orchestration built on .NET 10 and .NET Aspire.

Sorcha implements the **DAD** (Disclosure, Alteration, Destruction) security model - creating cryptographically secured registers where disclosure is managed through defined schemas, alteration is recorded on immutable ledgers, and destruction risk is eliminated through peer network replication.

**Current Status:** MVD complete; hardening toward production. Current feature/task state lives in `.specify/MASTER-TASKS.md` and `docs/reference/development-status.md` — not tracked as a fixed percentage here (it only goes stale).

---

## Quick Start

```bash
# Prerequisites: .NET 10 SDK, Docker Desktop

# Generate per-deploy config (.env, incl. service-to-service secrets), then start
# all services. A bare `docker-compose up` without a generated .env fails by
# design — docker-compose.yml guards the 8 ServiceAuth secrets with ${VAR:?...}.
./scripts/sorcha-setup.sh

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
| Testing | xUnit + FluentAssertions + Moq | 11,000+ tests across 50+ projects |

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

**Key Services** (8 services; ports are Docker-host / Aspire-HTTPS — authoritative list: `docs/getting-started/PORT-CONFIGURATION.md`):
| Service | Port (Docker/Aspire) | Purpose |
|---------|---------------------|---------|
| Blueprint | 5000 / 7000 | Workflow management, SignalR |
| Register | 5380 / 7290 | Distributed ledger, OData |
| Wallet | internal / 7001 | Crypto operations, HD wallets |
| Tenant | 5450 / 7110 | Multi-tenant auth, JWT issuer, Participant Identity, Platform Identity, Register Invitations |
| Validator | 5800 (HTTP), 5801 (gRPC) / 7004 | Consensus, chain integrity |
| Peer | 50051 (gRPC) / 7002 | P2P network, gRPC |
| HAIP | internal / — | OpenID4VCI/VP external-wallet surface (issue + verify), reached via the gateway |
| API Gateway | 80 / 7082 | YARP reverse proxy |

**Designer UI:** `/designer/blueprint` is the canonical route (replaces legacy `/designer` and `/designer/chat`). The page is a rail-driven Describe → Understand → Rehearse → Go live shell (Feature 142); Go-live is gated by a server-side `RehearsalPass` on the executable-definition hash.

**Sorcha.UI.Core audience convention (Feature 123):** user-facing and admin-facing code in `Sorcha.UI.Core` is partitioned at folder level into `Services/User/`, `Services/Admin/`, `Services/Shared/` (and the same pattern under `Models/`). Folders carry the audience; namespaces stay at the subject level so consumer `using` directives are stable across moves. See `src/Apps/Sorcha.UI/Sorcha.UI.Core/README.md` for the full convention and bi-modal smell detector.

**Shared user-facing component library (Feature 122):** user-facing components shared between `Sorcha.UI` (web) and `Sorcha.Wallet.Pwa` (PWA) live in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User`. Admin / designer / explorer components remain in `Sorcha.UI.Core`. The PWA references `Sorcha.UI.Components.User` directly; `Sorcha.UI.Core` re-exports via ProjectReference so web hosts pick the same components up transparently. See `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/README.md` for placement rules.

Full project tree: `docs/reference/project-structure.md`. Architecture diagrams: `docs/architecture.md`.

---

## Feature API References

Feature-specific endpoint tables, domain models, and cross-cutting patterns for Participant Identity, Register Invitations, Trust Hardening (079), Stored Data / file attachments (085), Validator Roster (086), Org Key Derivation (083), Platform Org Topology, Consumer Persona (092), System Register Genesis (099), Open Participants / late binding (103), `x-review` / credential id-cards (107), ownership-agnostic submission / derived relationship (108), Timebound Presentation Lifecycle (111), the transactional email architecture (112 — facade / template renderer / branding resolver / welcome dispatcher, see Tenant Service README), the Citizen Wallet PWA server-side surface (114 — holder/device delegation, status-list publisher + worker, enrolment endpoint, Tenant device registry), the F126 council-page enrolment gate (`EnrolGateComponent` + `IEnrolPairingSignal`), the F127 credential gate (`SorchaWalletPresentationConsumer` + `CredentialGateComponent` + claims-fetch endpoint — extends F111's lifecycle with the first non-HAIP consumer), and the F128 cold-start onboarding surface (enrol-session `mode` discriminator, pairing short-code transport, has-any-device aggregate, `IHasPairedDeviceProbe`, `PairingTakeover`, `PairingHandoffSurface`, `PairingNagBanner`, pairing-resumption email) are consolidated in the **`sorcha-architecture`** skill (`.claude/skills/sorcha-architecture/SKILL.md`). Load it when touching any of those features — it carries what used to live inline here.

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
Depend on a **service-specific repository interface** (e.g. `IWalletRepository`, `IRegisterRepository`, `IInstanceStore`, `IActionStore`) — there is **no** generic `IRepository<T>`. The concrete backend (EF Core / MongoDB / Redis / in-memory) is chosen at registration time and recorded via `IStorageRegistrationLog` (Pattern #10/#13).
```csharp
public class WalletService(IWalletRepository repository)
{
    // domain logic delegates persistence to the injected repository
}
```
For tiered / cache storage the seams live in `Sorcha.Storage.Abstractions`: `ICacheStore`, `IDocumentStore`, `IWormStore`, `IVerifiedCache`.

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

The audited list also covers the SignalR backplane (Feature 118 — synthetic interface name `Sorcha.ServiceDefaults.Hubs.SignalRBackplane`). Production / Staging refuse to start when a hub-hosting service has no Redis backplane — silent multi-replica fan-out misses are a correctness bug, not a degraded mode.

### 11. Notification Hubs (Feature 118)

Every Sorcha notification hub (TenantHub, BlueprintHub, WalletHub, RegisterHub) registers through `services.AddSorchaHub<THub, TClient>(IConfiguration, routePath, serviceShortName)` from `Sorcha.ServiceDefaults.Hubs`. ChatHub is the deliberate exception — RPC-streaming wire shape, documented inline.

```csharp
using Sorcha.ServiceDefaults.Hubs;

builder.Services.AddSorchaHub<BlueprintHub, IBlueprintHubClient>(
    builder.Configuration, "/hubs/blueprint", "blueprint");
// ...
app.MapSorchaHubs();   // maps every AddSorchaHub registration
```

The extension wires SignalR + Redis backplane (`ChannelPrefix = sorcha:signalr:{serviceShortName}` so cross-service backplane traffic is isolated) + reconnect-with-jitter + the storage-providers audit. The Redis connection comes from the SorchaConnections cascade (`ConnectionStrings:{Service}:Redis` → `ConnectionStrings:Sorcha:Redis`).

Group strings are constructed only via `*HubGroups` builder classes alongside each hub (e.g., `BlueprintHubGroups.Wallet(addr)`) — no inline `$"wallet:{addr}"` interpolation in service code. CI grep gate enforces this (Phase 7 / US5).

Hub events follow the **thin-signal contract** — opaque IDs and timestamps only, no domain payload. Each event method on the typed-client interface carries an XML doc `<see cref="..."/>` linking to the authenticated REST detail endpoint. ChatHub is exempt (it streams content by design).

Full architecture: `specs/118-notifications-architecture/spec.md`. Design rationale: `docs/superpowers/specs/2026-05-05-notifications-architecture-design.md`.

### 12. Notification Routing (Snackbar retirement)

**Do NOT inject `ISnackbar` in new user-facing code.** The Sorcha UI has retired MudBlazor's `Snackbar.Add(...)` toast surface from every user-facing page and PWA component (PRs #740-#755). The remaining `MudSnackbarProvider` mount stays only for in-flight admin / designer pages still on the allowlist.

**The three surfaces, by intent:**

| Surface | Use when | Where |
|---------|----------|-------|
| `IInlineFeedback` | Actor's own-action feedback in the current page (success / error / info / warning). Default 4s auto-dismiss; pass `autoDismissMs: 0` for errors the user must acknowledge. | `Sorcha.UI.Core.Services.Feedback.IInlineFeedback` (scoped in Web, singleton in PWA). Renders via `InlineFeedbackHost.razor` mounted at the top of the content region. |
| Server-side inbox writer | Workflow / lifecycle / security event that should appear in the durable bell drawer across sessions. Always wrap the writer call in `try` / `LogError` / swallow — a writer failure must NOT roll back the underlying operation. | `WalletWorkflowInboxWriter`, `WalletInboxWriter`, `CitizenDeviceInboxWriter`, `TenantSecurityInboxWriter`, plus `WriteOrgMembership*` on the membership writer. Bell drawer is Feature 118 / `MainLayout.razor`. |
| `CopyButton` primitive | "Copy this value to clipboard" affordances. Use `Variant.Button` for labelled buttons, `Variant.IconButton` for icon-only triggers inside lists / detail views. The button morphs to "Copied ✓" for ~2s on success and reverts. | `Sorcha.UI.Core.Components.Forms.CopyButton` (in `Sorcha.UI.Components.User`). |

**Dialog content** is its own micro-rule: dialog success closes the dialog with `MudDialog.Close(DialogResult.Ok(...))` and the parent surfaces inline feedback; dialog errors render an inline `<MudAlert Severity="Severity.Error" Dense="true" Class="mb-2">…</MudAlert>` inside the `DialogContent` body. Do NOT call `IInlineFeedback` from inside a dialog — `InlineFeedbackHost` mounts in the layout, not inside dialog surfaces.

A CI gate at `scripts/check-no-snackbar.ps1` enforces the ratchet via `.snackbar-allowlist`. The allowlist may only shrink — any new `Snackbar.Add(` or `ISnackbar` reference outside the allowed paths fails the build.

Full architecture: `specs/118-notifications-architecture/MIGRATION.md`.

### 13. Tiered JWT audiences + issuer hardening (Feature 136)

The JWT `aud` claim is the **trust-tier boundary**. Every token carries an installation-namespaced, tier-scoped audience — `{installation}:consumer | platform | service | enrol-session` — from the single source of truth `SorchaAudiences` (`Sorcha.ServiceDefaults.Auth`). **Never hand-build an audience string** — use `new SorchaAudiences(installationName).For(Tier.X)` / `.All`. `InstallationName` (default `sorcha`, set per deploy via `JwtSettings:InstallationName`) drives both the audience namespace and the issuer.

**Authenticate-broad / authorize-narrow.** Bearer validation accepts any of the installation's four tier audiences (`ValidAudiences = SorchaAudiences.All`), rejecting cross-installation tokens. The tier is enforced **per endpoint** by policies registered in `AddSorchaAuthorizationPolicies` (every service calls it):

```csharp
group.MapGroup("/api/v1/wallet").RequireAuthorization("RequireConsumerAudience");   // citizen surface
adminGroup.RequireAuthorization("RequireAdministrator", "RequirePlatformAudience");   // tier gate AND role gate
internalGroup.RequireAuthorization("RequireService");   // token_type==service AND aud==:service
```

- **`RequireConsumerAudience`** — consumer/wallet surfaces. **`RequirePlatformAudience`** — admin/org/designer; **compose it on top of the role policy** (it doesn't replace `RequireAdministrator`). **`RequireService`** — `/api/internal/*` (now also asserts the `:service` audience). Genuinely cross-tier endpoints (e.g. `/me/inbox`) stay plain `.RequireAuthorization()` — there is no "any-human" tier, so don't force-classify them.
- **The tier follows the person, not the UI host.** A citizen is `:consumer` on both `/app` (web) and `/wallet` (PWA); an admin is `:platform` in org context. Login derives the tier from `returnTo` (`/wallet`⇒consumer, `/app`⇒platform) as a *preference* that **downgrades to entitlement** (a citizen on `/app` → consumer); an explicit `tier=platform` request by a non-entitled user is **refused (403)**. A consumer token **carries `org_id` (its home/public org)** so the citizen can do their own org-scoped operations (wallet, application submission); it **omits roles + wallet binding** — the tier boundary is the audience + the absence of roles, not the absence of org context.
- **Issuer**: no shared default — `SorchaIssuer.Resolve` gives `urn:sorcha:{installation}`, fail-closed in Production/Staging if unconfigured. Mint and validate MUST resolve issuer + audiences through the same `SorchaIssuer`/`SorchaAudiences` or tokens self-reject.

Full reference: the **`jwt` skill** ("Tiered audiences + issuer hardening"). Metrics: `sorcha_token_minted_total{tier}` + `sorcha_tier_request_rejected_total{requested,reason}` on the `Sorcha.Identity` meter.

### 14. Unified versioning (build-time derived)

**Every component shares ONE version**, defined once in the **root `Directory.Build.props`** —
`Major.Minor.Patch` where **Major** is a manual `<SorchaMajor>` (currently `2`), **Minor** =
`GITHUB_RUN_NUMBER` (increments every CI run), **Patch** = `GITHUB_RUN_ATTEMPT` (PR retries). Local
(non-CI) builds are `2.0.0-dev`. The 7 per-area `Directory.Build.props` import the root via
`GetPathOfFileAbove`, so services, web/app clients, NuGet libs, the CLI, and the agent all inherit it.

```xml
<!-- DON'T do this in any .csproj — it overrides the root and re-fragments versioning: -->
<Version>2.0.0</Version>
```

- **NEVER hard-code `<Version>` / `<AssemblyVersion>` / `<FileVersion>` in a `.csproj`.** New projects inherit automatically.
- **Every `Dockerfile` MUST declare the build-args** — docker build-args are *opt-in*, so a Dockerfile
  without `ARG GITHUB_RUN_NUMBER` / `ARG GITHUB_RUN_ATTEMPT` (plus the matching `ENV`) silently
  discards what `docker-publish.yml` passes it. The assembly then ships stamped `2.0.0-dev` while the
  image is tagged `2.<run>.<attempt>` — tag and payload disagree and **nothing fails**. Put the block
  after the `COPY src/ …` line (post-restore, so the cacheable restore layer isn't invalidated).
  Enforced by `scripts/check-dockerfile-version-args.ps1` (CI: `version-args-gate`).
- **Never hand-bump the local `-dev` version** in the root props. It is always `<Major>.0.0-dev`. A
  deployed artefact reporting `-dev` means the Dockerfile is missing the ARG block — fix that, not
  the version string.
- **Display the version via `SorchaVersion.Current`** (`Sorcha.UI.Core.Utilities`, in
  `Sorcha.UI.Components.User`) — never re-resolve the assembly attribute per surface and never
  hardcode a version in a `.razor` page.
- Publish workflows **derive** the version (`dotnet pack`/`build` reads the env in CI) — do **not** reintroduce "bump `<Version>` + commit" steps. Docker images are tagged `:2.<run>.<attempt>`.
- A **Major** bump is a deliberate edit to `<SorchaMajor>` in the root `Directory.Build.props`.

### 15. Derivation contexts have exactly one home

Every `"sorcha:*"` key-derivation context lives in **`Sorcha.Wallet.Contracts.Constants.SorchaDerivationPaths`** (`src/Common/Sorcha.Wallet.Contracts/Constants/SorchaDerivationPaths.cs`). That project is a **zero-dependency leaf**, so every consumer can reference it — services, CLI, Blazor UI, and the WASM wallet PWA alike.

```csharp
using Sorcha.Wallet.Contracts.Constants;

// DO
derivationPath: SorchaDerivationPaths.DocketSigning,

// DON'T — no compiler or runtime check will ever catch a typo here
derivationPath: "sorcha:docket-signing",
```

- **Never hard-code a context literal, and never re-declare one as a local `private const`.** A mistyped context **does not throw** — it derives a *different but perfectly valid* key. The damage surfaces far away and silently: a wrong `sorcha:docket-signing` gives the validator a signing key that no longer matches its own roster entry, `RegisterMonitoringBootstrap` never enrols the register, and dockets simply stop sealing.
- The constants deliberately do **not** live in `Sorcha.Wallet.Portable` — its `Sorcha.Cryptography` dependency P/Invokes libsodium and cannot load under browser-wasm. That was the original reason services hand-copied the literals and the PWA got a hand-mirrored second constants class; both are gone.
- Adding a context means adding **both** `Foo` and `FooPath`, plus a `ResolvePath` arm. Reflection tests in `Sorcha.Wallet.Contracts.Tests` enforce all three, and that no two contexts share a BIP44 slot.
- Enforced by `scripts/check-derivation-contexts.ps1` (CI: `derivation-contexts-gate`). The gate reads the context list *from* the canonical file, so it cannot itself drift. Comments are ignored — illustrative prose in XML docs is fine. **Tests are out of scope**: an assertion like `DocketSigning.Should().Be("sorcha:docket-signing")` is what pins the wire value. `.derivation-contexts-allowlist` is currently **empty** and may only shrink.

### 16. Cross-boundary validation codes have exactly one home

A validation code that **one project emits and another names** lives in the shared leaf `Sorcha.Blueprint.Models` — `ValidationErrorCodes` for `VAL_*`, `ValidationWarningCodes` for `WARN_*`.

```csharp
using Sorcha.Blueprint.Models;

// DO
if (string.Equals(result.ErrorCode, ValidationErrorCodes.ChainFork, StringComparison.Ordinal))

// DON'T — two independently-typed literals; the compiler cannot relate them
if (string.Equals(result.ErrorCode, "VAL_CHAIN_FORK", StringComparison.Ordinal))
```

- **Some of these codes are matched on, not just logged.** Blueprint Service's `RedisPresentationSealCoordinator` compares the Validator's error code against `ChainFork` to recognise "already sealed via another path" and dedupe silently (Feature 119). Rename the producer's literal and that comparison stops matching — no compile error, no exception, no log, just a duplicate-submission path that quietly stops being deduped.
- **Service-internal codes stay put.** The Validator's ~70 internal codes (`VAL_SCHEMA_*`, `VAL_STRUCT_*`, `VAL_PERM_*`, …) are declared and consumed in the same file and carry no cross-boundary drift risk. **Promote a code only when a second project needs to name it** — that is the trigger, not family membership. `VAL_BP_CRED_004` deliberately stays local while its siblings moved.
- Keep the taxonomy honest: `ValidationErrorCodes` holds blocking `VAL_*` only, `ValidationWarningCodes` non-blocking `WARN_*` only. An operator filtering logs on the prefix must not miss a blocking error. Enforced by `ValidationCodeContractTests`, which also pins each code's wire value (they are an operator-facing contract) and rejects duplicate values.
- Enforced by `scripts/check-error-code-contract.ps1` (CI: `error-code-contract-gate`). The gate derives its guarded set *from* the two canonical files, so it cannot drift. Comments ignored; tests exempt (asserting the literal is what pins it). `.error-code-contract-allowlist` is **empty** and may only shrink.

### 17. Service addresses resolve through one cascade

A Sorcha service's base address is resolved via **`SorchaServiceAddresses`** (`Sorcha.ServiceClients.Configuration`), never by reading a config key literal at the call site.

```csharp
using Sorcha.ServiceClients.Configuration;

// DO — one cascade, every accepted spelling, your own default
var address = SorchaServiceAddresses.TryResolve(configuration, SorchaService.Tenant)
              ?? "https+http://tenant-service";

// DON'T — a deployment setting a different spelling silently yields null
var address = configuration["ServiceClients:TenantService:Address"] ?? "...";
```

- **Why**: an audit found **19 distinct key spellings addressing 8 services**. The Tenant Service alone had four (`ServiceClients:TenantService:Address`, `ServiceClients:Tenant:BaseAddress`, `Services:TenantService:BaseAddress`, `Services:Tenant:Url`) and **six call sites each hand-rolled a different fallback chain over them** — so which key a deployment had to set depended on which client resolved it. Nothing fails loudly: the client gets null and falls back to a hardcoded address, quietly talking to the wrong place.
- **Every historical spelling still resolves.** Deployments in the wild set `ServiceClients__{X}Service__Address`; dropping a spelling would silently unbind a running node. The resolver ends the drift, it does not break deployments. New config should use `SorchaServiceAddresses.CanonicalKey(...)`.
- **The resolver supplies no default, deliberately.** Existing call-site defaults are not interchangeable — `http://tenant-service`, the Aspire `https+http://` discovery scheme, an explicit `:8080`, or throwing. Unifying them changes runtime behaviour differently under Aspire and compose; that is a separate decision, not one to settle silently. Keep your own `?? default`.
- **The API Gateway's `Services:{X}:Url` section is out of scope by design** — its own namespace for the aggregation views, what compose sets for that container, and already accepted by the resolver as a fallback spelling.
- **`ServiceClients:PeerService:HttpAddress` is deliberately NOT resolver-routed** — it is a *different endpoint*, not a spelling variant. The Peer service exposes gRPC on its resolved address and HTTP separately, so that key stays ahead of the resolver at its call sites and is absent from `KeysFor(Peer)`; folding it in would let a gRPC address answer an HTTP lookup.
- Enforced by `scripts/check-service-address-keys.ps1` (CI: `service-address-keys-gate`). `.service-address-keys-allowlist` was a ratchet seeded with 19 files and is now **empty** — every service-address read in `src/` resolves through `SorchaServiceAddresses`, so a new literal fails CI outright. It may only shrink; never add an entry to make a build pass.

### 18. CLI DTOs must agree with the server on the wire

The `Sorcha.Cli` keeps its own request/response DTOs for most commands (only some clients are shared — pattern-style guidance is in `src/Apps/Sorcha.Cli/README.md`). That is allowed, but a CLI DTO **must serialise to the same JSON property names as the server type its endpoint binds**, or the command silently misbehaves — no crash, no compile error, just wrong output, dropped data, or a request the server ignores.

- **`tests/Sorcha.Cli.ContractTests` is the guard.** It references *both* the CLI and the services (a layering combination no production assembly may have — legitimate only in a test project) and asserts every CLI↔server name pair agrees. A DRIFT-002 audit found **30** broken commands this way; all are fixed and the **baseline is empty**, so a new mismatch fails CI outright.
- **When you add or change a CLI command's DTO**, check it against the type the endpoint actually `.Produces<T>()` / `[FromBody]`-binds — *not* a same-named entity or a same-named type in a service the CLI doesn't call. Several "mismatches" in the audit were name collisions where the CLI was already correct; the harness's `NotAWireContract` list documents each with a reason.
- **Reflection can't see everything.** A server type that is a `private record` or an anonymous `Results.Ok(new { ... })` is invisible to the harness, so name-discovery may pair the CLI type against an unrelated public collision. When the CLI genuinely matches an unreachable server shape, record it in `NotAWireContract`.
- **Deliberate request subsets** (a CLI request that omits *optional* server fields on purpose) are fine but must be justified in `NotAWireContract` **and** guarded by a test asserting every *required* server field is still present (see `IssueCredentialRequest_SendsEveryRequiredServerField`).
- **A command that cannot be driven from flags** (raw `transaction submit` needs a full signed `TransactionModel`) should return a clear "not supported via CLI" error, not a fake success path.

---

### 19. EF migrations: squash while pre-release, add-only after

**Today (pre-release): every schema change is folded into that service's `InitialCreate`.** All four
migration sets — Blueprint, Tenant, Peer, Wallet.Core — carry exactly **one** migration each, and they
stay that way. Amend `InitialCreate.cs`, its `.Designer.cs`, and the `*ModelSnapshot.cs` together.

```bash
# DON'T, while pre-release — it re-fragments a deliberately single migration:
dotnet ef migrations add AddSomeColumn
```

- **The cost is real and must be understood, not discovered.** Amending an applied migration is
  invisible to any database that already recorded it: `MigrateAsync` compares MigrationIds, sees
  `InitialCreate` present, and does nothing. The columns never appear and the failure surfaces far
  away as a raw Postgres error — `42703: column i.DecisionReasonCode does not exist` — on the first
  query that touches them, long after a green build and a green test suite.
- **So the remedy is to recreate the database, never to expect `MigrateAsync` to help.** A dev box or
  a node that predates the change is brought up to date with `docker compose down -v` + re-genesis.
- **Why it is nonetheless right now**: there are no installations to migrate. One readable
  `InitialCreate` beats an accreting chain of one-column deltas, and nothing is lost by resetting.
- **Verify, don't assume.** `dotnet ef migrations script --idempotent` should name exactly one
  migration and its `CREATE TABLE` should contain your column. Applying that script to a scratch
  database is the cheap proof.

**At release (a deliberate call by the maintainer, like a `<SorchaMajor>` bump): this rule inverts.**
From the moment a real installation exists, `InitialCreate` becomes immutable and **every** schema
change ships as a new additive migration — because an amended migration silently diverges from
deployed schema with no error at all. Switching this on also means deployment has to own an
upgrade path (ordered migration application, forward-compatibility of the running image against the
prior schema, and a rollback story), which does not exist yet and is tracked as **#1365**.
Do not flip half of this: add-only migrations without an upgrade process is worse than either end.

---

### 20. Global sanitized exception handling (issue #1433)

Every service registers `Sorcha.ServiceDefaults.SanitizedExceptionHandler` via `AddServiceDefaults()`
(`AddProblemDetails()` + `AddExceptionHandler<SanitizedExceptionHandler>()`) and applies it via
`app.UseSanitizedExceptionHandling()` — called as the **very first** line after `var app =
builder.Build();`, before any other `app.Use*`, so it wraps every other middleware's unhandled
exceptions too.

```csharp
var app = builder.Build();

// FIRST in the pipeline — wraps every other middleware's unhandled exceptions.
app.UseSanitizedExceptionHandling();
```

- **Deliberately environment-UNGATED.** ASP.NET Core auto-adds the DeveloperExceptionPage (full
  stack trace in the response body) whenever no exception-handling middleware is registered and
  `ASPNETCORE_ENVIRONMENT=Development` — and before this handler existed, Sorcha had none anywhere.
  At least one internet-facing node ran with `Development` set, so that page was the live behaviour
  in production (issue #1433). The sanitized problem+json response is the only possible unhandled-
  exception response in every environment — a misconfigured node can no longer leak a stack trace,
  exception type, or exception message.
- **Only catches UNHANDLED exceptions.** Endpoints that already return their own typed/problem+json
  errors via `IResult` (the overwhelming majority in Sorcha) are untouched. `AddProblemDetails()`
  does change the *default* shape ASP.NET Core generates for framework-level 400/415/etc. responses
  (e.g. minimal-API binding failures) — if you add a test asserting that shape, assert the new one.
- Full exception detail (type, message, stack trace) is still logged server-side via `ILogger` —
  only the HTTP response body is sanitized. Every response carries a `traceId` extension an operator
  can correlate against that log line.

### 21. An organisation's wallet is created by its OWN admin (#1525)

The platform does **not** create an organisation's canonical signing wallet. Its BIP39 recovery
phrase is **shown once and never stored**, so a service-to-service create generates a phrase with no
human present to receive it and the organisation can never be recovered. It is also not the
platform's secret to hold — it belongs to the org admin.

```csharp
// DON'T — this is what was removed. Owner would be the org, phrase discarded, org unrecoverable.
var wallet = await _walletClient.CreateWalletAsync(name, "ED25519", org.Id.ToString(), org.Id.ToString());
org.WalletAddress = wallet.Address;
```

**Create-then-link**, so the phrase never transits the Tenant Service:

1. The org admin calls `POST /api/v1/wallets` with `organizationId` — the **organisation** becomes
   the owner, and `mnemonicWords` is returned once, to them.
2. `POST /api/organizations/{id}/wallet` records it, after verifying the org owns that wallet.

- **A null `Organization.WalletAddress` is the "awaiting its wallet" state**, not a fault to be
  quietly repaired. `OrgWalletReconciliationService` — a 60s sweep that silently minted org wallets
  and discarded their phrases — is **deleted**; do not reintroduce anything like it.
- **A platform SystemAdmin is refused** at both endpoints. This is the deliberate exception to their
  usual cross-org reach, and `CallerOrganizationGate` is therefore *not* used on the link endpoint —
  it exempts SystemAdmins by design. The handler compares the caller's org to the route itself.
- **Ownership is verified** (addresses are public, so otherwise an admin could adopt any wallet whose
  address they know) and **a second link is refused** — replacing the canonical wallet orphans every
  credential issued under the old one and every governance roster entry matched against it.
- **Ordering matters downstream.** The org wallet is what `did:sorcha:org:{address}` anchors on, so
  it must exist *before* the F083 master key. Without it there is nothing to anchor a DID document
  to and `GET /orgs/{id}/did.json` 404s — that was #1518, which looked like a timing race and was a
  missing step.
- Walkthroughs: pass `-WalletUrl` to `New-SorchaOrganization`, or call `New-SorchaOrgWallet` once an
  admin session exists. See the **`walkthrough-builder`** skill.

---

### 22. A published definition's identity is its publication transaction (Feature 195)

A blueprint definition is identified by the transaction that published it — **not** by a version
number, not by a separately-computed content hash, and not by the behavioural `execDefHash`.

```csharp
using Sorcha.Blueprint.Models.Canonical;

// The ONE producer. Register Service only.
var txId = BlueprintPublicationId.Compute(registerId, blueprintId, canonicalJson);
```

`publicationTxId = SHA-256("sorcha:blueprint-publication:v1" ␟ registerId ␟ blueprintId ␟ canonicalJson)`
— register-scoped, domain-tagged, and RFC-8785 key-sorted.

- **Exactly one producer: the Register Service.** Every other component *reads the value it returns*
  and must never recompute it. Enforced by `scripts/check-publication-id-owner.ps1`
  (CI: `publication-id-owner-gate`), whose allowlist holds four call sites — two producers and two
  *verifiers*. This replaced an id with **four independent homes**, which is why a republish could be
  silently deduped away (#1563): the id was version-blind *and* doubled as the starting-action anchor,
  so the obvious one-line fix broke chaining. Anchor and pin are now the same value, so that
  coupling no longer exists.
- **Verification is self-anchoring, so there is no sealed `contentHash`.** The bytes written to the
  ledger are the canonical form, and the transaction id is their digest — a recovering node
  recomputes the id from what it received and compares it to the transaction's own id. Unlike a
  sibling field, an id cannot disagree with the content it identifies. **Do not reintroduce a
  separately-sealed digest.**
- **Do not confuse it with `execDefHash`.** They answer different questions and live in different
  value spaces. `publicationTxId` **identifies** a definition; `execDefHash` is the *behavioural
  signature* — a deliberately narrower projection (presentational `title` / `description` / `x-*`
  excluded) whose only job is deciding whether an F142 `RehearsalPass` survives a republish. Several
  publications may legitimately share one `execDefHash`; the same definition on two registers has the
  same `execDefHash` and two different ids. **An instance is pinned to the publication id.** Comparing
  one against the other silently yields a plausible-but-wrong answer rather than an error — that is
  exactly how `isPinnedToLatest` shipped hard-wired to `false`, with a green unit test whose fixture
  gave both fields the same string.
- **Every serialized property on the blueprint graph is LEDGER CONTRACT.** Adding, removing or
  renaming one changes the canonical bytes and therefore *every publication id on every register* —
  including properties that are dead. `BlueprintCanonicalJsonGoldenVectorTests` exists to catch this
  and has already fired on a change that compiled cleanly and broke nothing else in ~4,300 tests.
  Regenerating the vector is correct only when you know what moved it and the move is intended.
- **Coverage of the behavioural signature is reflective, not a list.** `ExecutableDefinitionCoverageTests`
  fails on any property nobody has classified, because both defaults are wrong in different
  directions. Adding a property means classifying it.

---

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
| `docs/architecture.md` | System architecture diagrams |
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
- Re-declare Wallet HTTP DTOs — the canonical `WalletDto` / `CreateWallet*` / `SignTransaction*` / `WalletAddressDto` / `AddressListResponse` live only in `Sorcha.Wallet.Contracts` (CI-gated by `wallet-contracts-gate`)
- Use `JsonNode` with JsonSchema.Net (use `JsonElement`)
- Commit secrets or credentials (CI-gated by `secrets-gate` / `scripts/check-secrets.ps1`; ratchet at `.secrets-allowlist`, may only shrink)
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

# Build & Test — "dotnet test" runs in Microsoft.Testing.Platform (MTP) mode, opted in via
# global.json (required by xunit.v3 4.x on the .NET 10 SDK). VSTest-style args no longer apply.
dotnet restore && dotnet build                    # Build solution
dotnet test                                       # Run all tests (MTP mode)
dotnet test --project tests/X/X.csproj            # One project
dotnet test --filter-class "*BlueprintTests*"     # Filtered (xunit MTP filters: --filter-class/-method/-namespace/-trait)
# Coverage: the old --collect:"XPlat Code Coverage" (VSTest/coverlet collector) does not run
# under MTP mode — use the coverage-analysis skill / CI coverage jobs instead.

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

**This guide — revision 3.2 | Updated: 2026-08-24** | Built with .NET 10 and .NET Aspire
_(This revision number is for CLAUDE.md itself; it is unrelated to the platform's build-derived `2.x` version — see §14.)_

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan:
`specs/195-blueprint-definition-identity/plan.md`
<!-- SPECKIT END -->
