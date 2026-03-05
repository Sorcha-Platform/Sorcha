# Implementation Plan: System Administration Tooling

**Branch**: `049-system-admin-tooling` | **Date**: 2026-03-05 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/049-system-admin-tooling/spec.md`

## Summary

Build admin UI pages and CLI commands for ~25 backend API endpoints that currently have no admin surface. 6 UI features (service principal CRUD, register policy, validator consent queue, validator metrics dashboard, system register page, threshold signing) + 5 CLI command groups. All backend endpoints exist — this is pure frontend/CLI work with one YARP route addition.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Blazor WASM, MudBlazor, System.CommandLine 2.0.2, Refit, Spectre.Console
**Storage**: N/A (frontend/CLI only — backend handles all persistence)
**Testing**: xUnit + FluentAssertions + Moq (unit), Playwright (E2E)
**Target Platform**: Blazor WASM (browser) + CLI (.NET cross-platform)
**Project Type**: Web (existing multi-project solution)
**Performance Goals**: Metrics dashboard updates within 10 seconds of system state (SC-004)
**Constraints**: All pages restricted to Administrator/SystemAdmin roles (SC-008)
**Scale/Scope**: ~15 new Blazor components, ~5 new CLI command groups (~17 subcommands), ~4 service interface extensions

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Notes |
|------|--------|-------|
| I. Microservices-First | PASS | No new services. UI calls existing services through API Gateway. |
| II. Security First | PASS | All pages behind `[Authorize(Roles = "Administrator")]`. Service principal secrets shown once via modal. |
| III. API Documentation | PASS | No new API endpoints. Existing endpoints already documented with Scalar. |
| IV. Testing Requirements | PASS | Unit tests for all service implementations + CLI commands. E2E Playwright tests for admin pages. |
| V. Code Quality | PASS | Follows existing async/await patterns, DI, nullable reference types. |
| VI. Blueprint Standards | N/A | No blueprint changes. |
| VII. Domain-Driven Design | PASS | Uses existing domain terminology (Register, Validator, Blueprint, Participant). |
| VIII. Observability | PASS | No new services to instrument. UI calls go through API Gateway which already has telemetry. |

**Post-Phase 1 re-check**: All gates still pass. No new projects, patterns, or dependencies introduced.

## Project Structure

### Documentation (this feature)

```text
specs/049-system-admin-tooling/
├── plan.md              # This file
├── research.md          # Phase 0: Research findings & decisions
├── data-model.md        # Phase 1: View models & DTOs
├── quickstart.md        # Phase 1: Build order & verification
├── contracts/           # Phase 1: Service & CLI contracts
│   ├── ui-services.md   # UI service interface contracts
│   └── cli-commands.md  # CLI command specs & Refit extensions
└── tasks.md             # Phase 2: Task breakdown (via /speckit.tasks)
```

### Source Code (repository root)

```text
src/Apps/Sorcha.UI/
├── Sorcha.UI.Web.Client/Pages/Admin/
│   ├── Validator.razor                        # Existing — extend with tabs
│   ├── ServicePrincipals.razor                # Existing — replace placeholder
│   └── SystemRegister.razor                   # NEW page
├── Sorcha.UI.Core/Components/Admin/
│   ├── Validator/
│   │   ├── ValidatorPanel.razor               # Existing — refactor to multi-tab
│   │   ├── ValidatorConsentPanel.razor         # NEW
│   │   ├── ValidatorMetricsPanel.razor         # NEW
│   │   ├── ValidatorThresholdPanel.razor       # NEW
│   │   └── ValidatorConfigPanel.razor          # NEW
│   ├── ServicePrincipals/
│   │   ├── ServicePrincipalList.razor          # Existing — replace with CRUD
│   │   ├── ServicePrincipalCrud.razor          # NEW — DataGrid with actions
│   │   ├── CreateServicePrincipalDialog.razor  # NEW
│   │   ├── EditScopesDialog.razor              # NEW
│   │   └── SecretDisplayDialog.razor           # NEW
│   ├── SystemRegister/
│   │   └── SystemRegisterDashboard.razor       # NEW
│   └── Registers/
│       ├── RegisterPolicyStep.razor            # NEW — wizard step
│       ├── RegisterPolicyTab.razor             # NEW — detail tab
│       ├── PolicyHistoryDialog.razor           # NEW
│       └── PolicyDiffDialog.razor              # NEW
├── Sorcha.UI.Core/Services/
│   ├── IValidatorAdminService.cs              # Existing — extend
│   ├── ValidatorAdminService.cs               # Existing — extend
│   ├── IRegisterService.cs                    # Existing — extend
│   ├── RegisterService.cs                     # Existing — extend
│   ├── IServicePrincipalService.cs            # NEW
│   ├── ServicePrincipalService.cs             # NEW
│   ├── ISystemRegisterService.cs              # NEW
│   └── SystemRegisterService.cs               # NEW
├── Sorcha.UI.Core/Models/Admin/
│   ├── ServicePrincipalViewModel.cs           # Replace existing placeholder models
│   ├── ValidatorMetricsViewModels.cs          # NEW
│   ├── ValidatorConsentViewModels.cs          # NEW
│   ├── ThresholdConfigViewModel.cs            # NEW
│   ├── RegisterPolicyViewModels.cs            # NEW
│   └── SystemRegisterViewModels.cs            # NEW
└── Sorcha.UI.Web.Client/Components/Layout/
    └── MainLayout.razor                       # Existing — add system-register nav link

src/Apps/Sorcha.Cli/
├── Commands/
│   ├── RegisterCommands.cs                    # Existing — add PolicyCommand + SystemCommand groups
│   ├── ValidatorCommands.cs                   # Existing — add ConsentCommand + MetricsCommand + ThresholdCommand groups
│   └── RegisterPolicyCommands.cs              # NEW (or inline in RegisterCommands.cs)
├── Services/
│   ├── IRegisterServiceClient.cs              # Existing — extend with policy/system methods
│   └── IValidatorServiceClient.cs             # Existing — extend with consent/metrics/threshold methods
└── Models/
    ├── RegisterPolicy.cs                      # NEW — CLI DTOs for policy
    └── ValidatorMetrics.cs                    # NEW — CLI DTOs for metrics

src/Services/Sorcha.ApiGateway/
└── appsettings.json (or reverseProxy.json)    # Existing — add admin-validators route

tests/
├── Sorcha.UI.Core.Tests/Services/             # NEW test files for service implementations
├── Sorcha.Cli.Tests/Commands/                 # NEW test files for CLI commands
└── Sorcha.UI.E2E.Tests/                       # NEW E2E test files
```

**Structure Decision**: No new projects. All work goes into existing `Sorcha.UI.Core`, `Sorcha.UI.Web.Client`, `Sorcha.Cli`, and `Sorcha.ApiGateway` projects. This follows the existing architecture where UI components live in Core (shared) and pages live in Web.Client.

## Complexity Tracking

No constitution violations. All work extends existing projects and patterns:
- No new microservices
- No new databases
- No new NuGet dependencies
- No new project files
