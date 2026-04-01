# Implementation Plan: CLI Modernisation and Feature Completion

**Branch**: `080-cli-modernisation` | **Date**: 2026-04-01 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/080-cli-modernisation/spec.md`

## Summary

Modernise the Sorcha CLI with consistent output formatting, branded help, real-time event streaming, full API coverage for all services, bulk operations, export/import, and MCP/AI integration. Cleanup stale models and dead code.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: System.CommandLine 2.0.5, Spectre.Console 0.54.0, Refit 10.1.6, Microsoft.Extensions.Http.Polly 10.0.5, Microsoft.AspNetCore.SignalR.Client 10.0.5 (new), YamlDotNet 16.3.0 (new to CLI)
**Storage**: Local file config (~/.sorcha/), JWT token cache (platform-specific encryption)
**Testing**: xUnit + FluentAssertions + Moq
**Target Platform**: Cross-platform CLI (.NET 10 global tool)
**Project Type**: Single project (src/Apps/Sorcha.Cli/)
**Performance Goals**: Event stream latency <3s, health check <10s, bulk 100 items <60s
**Constraints**: Backward-compatible with existing command syntax, no breaking changes
**Scale/Scope**: ~15 new files, ~30 modified files, 10 user stories

## Constitution Check

| Gate | Status | Notes |
|------|--------|-------|
| Microservices-First | PASS | CLI is a client — no coupling between services added |
| Security First | PASS | Auth required for all protected commands, token caching uses platform encryption |
| API Documentation | PASS | All new commands will have --help text with examples |
| Testing Requirements | PASS | Tests for new formatters, event streaming, bulk operations |
| Code Quality | PASS | Async/await, DI, nullable types, consistent patterns |
| Observability | N/A | CLI is a client tool, not a service |
| DDD | PASS | Uses Sorcha domain terms consistently |

No violations. No complexity justification needed.

## Project Structure

### Documentation (this feature)

```text
specs/080-cli-modernisation/
├── plan.md
├── spec.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── cli-commands.md
└── checklists/
    └── requirements.md
```

### Source Code

```text
src/Apps/Sorcha.Cli/
├── Branding/
│   └── SorchaBanner.cs              # ASCII art + version display
├── Commands/
│   ├── (20 existing command files)   # MODIFIED: formatting, examples, pagination
│   ├── EventWatchCommand.cs          # NEW: SignalR event streaming
│   ├── HealthCommand.cs              # NEW: Aggregated health
│   ├── InvitationCommands.cs         # NEW: Register invitations
│   ├── AuditCommands.cs              # NEW: Audit log queries
│   ├── VerifyCommands.cs             # NEW: Receipt/bundle/proof
│   ├── PlatformCommands.cs           # NEW: Platform management
│   ├── CompletionCommand.cs          # NEW: Shell completion
│   ├── HelpCommand.cs                # NEW: Getting-started guide
│   ├── YamlOutputFormatter.cs        # NEW: YAML output
│   ├── MachineReadableFormatter.cs   # NEW: JSON envelope
│   ├── IOutputFormatter.cs           # MODIFIED: add Yaml format
│   ├── TableOutputFormatter.cs       # MODIFIED: consistency
│   ├── JsonOutputFormatter.cs        # MODIFIED: valid arrays
│   └── CsvOutputFormatter.cs         # MODIFIED: actually works
├── Models/
│   ├── (existing models)             # MODIFIED: sync with service DTOs
│   └── EventStreamMessage.cs         # NEW
├── Services/
│   ├── (9 existing Refit clients)    # MODIFIED: match current APIs
│   ├── IInvitationServiceClient.cs   # NEW
│   ├── IAuditServiceClient.cs        # NEW
│   ├── IVerificationServiceClient.cs # NEW
│   └── EventStreamService.cs         # NEW: SignalR wrapper
├── Program.cs                        # MODIFIED: new commands, banner, global opts
└── Sorcha.Cli.csproj                 # MODIFIED: new package refs

tests/Sorcha.Cli.Tests/              # NEW test project (or extend existing)
├── Formatters/
│   ├── YamlOutputFormatterTests.cs
│   ├── MachineReadableFormatterTests.cs
│   └── OutputConsistencyTests.cs
├── Commands/
│   ├── HealthCommandTests.cs
│   └── EventWatchCommandTests.cs
└── Services/
    └── EventStreamServiceTests.cs
```

**Structure Decision**: All changes within existing `src/Apps/Sorcha.Cli/` project. New `Branding/` subdirectory for banner. No new projects except potentially a test project.
