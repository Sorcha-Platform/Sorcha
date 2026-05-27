# Implementation Plan: CLI API Surface Catch-Up

**Branch**: `133-cli-api-coverage` | **Date**: 2026-05-20 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/133-cli-api-coverage/spec.md`

## Summary

Add operator- and automation-relevant commands to the Sorcha CLI to close the gap between its frozen command surface (last touched at Features 080/099) and the platform's current API surface. All backing endpoints already exist; this feature adds CLI surface only — Refit client methods, `System.CommandLine` command classes, models/DTOs, `Program.cs` registration, xUnit tests, and command-reference documentation. Where the shared service-client library (`Sorcha.ServiceClients.Http`) already provides a capability (organisation key derivation), the CLI reuses it rather than introducing a duplicate client; a selective-reuse rule governs that choice. Delivered in two phases: Phase 1 (P1/P2 — transaction trust-hardening, register sync diagnostics, validator roster governance, org key derivation) and Phase 2 (P3 — wallet diagnostics, system-register governance, citizen-device admin, auth/token automation, trust-anchor administration).

## Technical Context

**Language/Version**: C# on .NET 10 (matches `src/Apps/Sorcha.Cli`)
**Primary Dependencies**: System.CommandLine 2.0.2 (command parsing), Refit 9.0.2 + Refit.HttpClientFactory 9.0.2 (HTTP clients), Spectre.Console 0.54.0 (rich output), System.IdentityModel.Tokens.Jwt 8.3.0 (token handling). Reuses `Sorcha.ServiceClients.Http` for org-key derivation.
**Storage**: N/A (CLI is a stateless client; only the encrypted token cache on disk, unchanged)
**Testing**: xUnit + FluentAssertions + Moq, per the existing CLI test project and the `sorcha-cli` skill testing pattern
**Target Platform**: Cross-platform .NET global tool / local build (Windows, macOS, Linux)
**Project Type**: Single project (console app + its test project)
**Performance Goals**: N/A (interactive CLI; each command is a small number of HTTP round-trips). Existing 30s HTTP timeout applies.
**Constraints**: Respect platform rate-limit policies; honour existing global options (profile / output / quiet / verbose / machine-readable); destructive commands must require an explicit target; one-time secrets (org master-key mnemonic) surfaced once and never persisted.
**Scale/Scope**: ~24 new command surfaces + 1 bug fix across 5 command areas in Phase 1 and 5 in Phase 2. No backend changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applicability | Status |
|-----------|---------------|--------|
| I. Microservices-First Architecture | CLI is a downstream client; adds no service coupling, no upward dependencies. | ✅ Pass (N/A) |
| II. Security First | No secrets committed; command inputs validated at the boundary; org master-key mnemonic surfaced once, never persisted; bearer token via existing encrypted cache. | ✅ Pass |
| III. API Documentation | No new HTTP endpoints created. Equivalent obligation met via the CLI command reference (`commands.md`) and skill docs (FR-026). | ✅ Pass (N/A for OpenAPI) |
| IV. Testing Requirements | xUnit, deterministic, AAA; every new command tested (FR-027); target >85% for new command code. | ✅ Pass |
| V. Code Quality | C# conventions, async/await, DI via the existing `HttpClientFactory`, nullable enabled, no Release warnings. | ✅ Pass |
| VI. Blueprint Creation Standards | `system-register publish` consumes blueprint JSON files (no Fluent generation). | ✅ Pass |
| VII. Domain-Driven Design | Command wording uses ubiquitous language (Blueprint, Publish, Participant); "publish" not "deploy". | ✅ Pass |
| VIII. Observability by Default | CLI is a client tool, not a service; no `/health`/`/alive` or OTel export obligation. Verbose flag already surfaces diagnostics. | ✅ Pass (N/A) |

**Result**: No violations. Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/133-cli-api-coverage/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output — CLI command→endpoint contracts
│   ├── phase1-commands.md
│   └── phase2-commands.md
├── checklists/
│   └── requirements.md  # From /speckit.specify
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Apps/Sorcha.Cli/
├── Commands/            # System.CommandLine command classes (one file per area)
│   ├── TransactionCommands.cs      # + proof / verify-proof / revoke; FIX status
│   ├── RegisterCommands.cs         # + sync-state / relationship / sync-health
│   ├── ValidatorCommands.cs        # + register / suspend / reactivate / revoke / count / audit / sequence
│   ├── WalletCommands.cs           # + org-key group; + did-document/gap-status/accounts/addresses/delegations
│   ├── SystemRegisterCommands.cs   # + publish / initialize / classify-change / versions
│   ├── DeviceCommands.cs           # NEW — list / revoke
│   ├── AuthCommands.cs             # + introspect / switch-org / orgs
│   └── TrustCommands.cs            # NEW — trust-anchor administration (corrected scope)
├── Services/            # Refit client interfaces (admin/operator surface)
│   ├── IRegisterServiceClient.cs   # + proof/revoke/status(fix)/relationship/sync-state/sync-health/system-register
│   ├── IValidatorServiceClient.cs  # + roster governance methods
│   ├── ITenantServiceClient.cs     # + devices / auth-token / trust
│   └── (reuse Sorcha.ServiceClients.Http IWalletServiceClient for org-key)
├── Models/              # Request/response DTOs for the new commands
└── Program.cs           # Register new/changed command classes

tests/Sorcha.Cli.Tests/  # xUnit tests mirroring the command + client additions
docs/ (or CLI commands.md reference)  # Command reference updates (FR-026)
.claude/skills/sorcha-cli/  # Skill doc refresh: command surface + selective-reuse rule
```

**Structure Decision**: Single-project layout. New commands extend existing command files in `src/Apps/Sorcha.Cli/Commands/` (two genuinely new files: `DeviceCommands.cs`, `TrustCommands.cs`). Admin/operator endpoints get thin Refit methods on the existing CLI client interfaces; org-key derivation reuses `Sorcha.ServiceClients.Http`. Tests live in the existing CLI test project. No backend, no new service.

## Complexity Tracking

*No constitution violations — section intentionally empty.*
