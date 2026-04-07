# Implementation Plan: Autonomous Actor Agent Framework

**Branch**: `087-actor-agent` | **Date**: 2026-04-07 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/087-actor-agent/spec.md`

## Summary

Build a standalone `Sorcha.Agent` CLI application that runs an autonomous actor as a long-lived process. The actor authenticates, listens for pending actions via SignalR (with polling fallback), and responds using either JSON Logic rules or AI (Claude API). First validation: port ConstructionPermit walkthrough to run with 5 independent actor processes.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: System.CommandLine 2.0.2, Sorcha.ServiceClients.Http, Sorcha.Blueprint.Models, Sorcha.Blueprint.Engine (IJsonLogicEvaluator), Microsoft.Extensions.Http.Polly, Spectre.Console, Anthropic SDK
**Storage**: N/A (stateless — JSONL append-only audit log for observability)
**Testing**: xUnit + FluentAssertions + Moq (following existing 30-project test pattern)
**Target Platform**: Windows, Linux (cross-platform .NET 10 console app)
**Project Type**: Console application (long-running process)
**Performance Goals**: Action response within 5 seconds of SignalR notification
**Constraints**: One actor per process, stateless, portable (actor.json + state.json only)
**Scale/Scope**: 5 concurrent actor processes for ConstructionPermit port

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | New CLI app, no coupling to services. Uses ServiceClients.Http for communication. |
| II. Security First | PASS | Credentials via `$env:` variables, not in config files. JWT token caching with OS-specific encryption (matches CLI). Input validation on actor definition files. |
| III. API Documentation | N/A | CLI tool, not an API service. |
| IV. Testing Requirements | PASS | Unit tests for decision engines, config loader, dedup logic. Integration test for full actor lifecycle. Target >85% coverage. |
| V. Code Quality | PASS | async/await throughout, DI, nullable reference types, .NET 10/C# 13. |
| VI. Blueprint Creation | N/A | Actor consumes blueprints, does not create them. |
| VII. Domain-Driven Design | PASS | Uses Sorcha domain language: Blueprint, Action, Participant. |
| VIII. Observability | PASS | Structured logging via ILogger, JSONL audit trail, health output on shutdown. |

No violations. No complexity justifications needed.

## Project Structure

### Documentation (this feature)

```text
specs/087-actor-agent/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research
├── data-model.md        # Data model
├── quickstart.md        # Getting started guide
├── contracts/
│   ├── cli-interface.md           # CLI command contracts
│   └── actor-definition-schema.json  # JSON Schema for actor files
└── checklists/
    └── requirements.md  # Quality checklist
```

### Source Code (repository root)

```text
src/Apps/Sorcha.Agent/
├── Program.cs                     # Entry point, DI, System.CommandLine setup
├── ExitCodes.cs                   # Standard exit codes (mirrors Sorcha.Cli)
├── Commands/
│   ├── RunCommand.cs              # "run" command — main actor loop
│   └── ValidateCommand.cs         # "validate" command — config checks
├── Configuration/
│   ├── ActorDefinition.cs         # Strongly-typed config model
│   ├── ActorDefinitionLoader.cs   # Load, resolve vars, validate
│   └── VariableResolver.cs        # $env: and {{placeholder}} resolution
├── Inbox/
│   ├── IInboxListener.cs          # Async enumerable of pending actions
│   ├── SignalRInboxListener.cs    # Real-time via SorchaHubConnectionBuilder
│   ├── PollingInboxListener.cs    # HTTP fallback on timer
│   └── CompositeInboxListener.cs  # Merges + deduplicates
├── Decision/
│   ├── IDecisionEngine.cs         # DecideAsync interface
│   ├── RulesDecisionEngine.cs     # JSON Logic via IJsonLogicEvaluator
│   └── AiDecisionEngine.cs        # Claude API integration
├── Execution/
│   ├── ActionExecutor.cs          # Sign + submit via ServiceClients
│   └── AuditLogger.cs             # JSONL append-only log
├── Auth/
│   └── AgentAuthService.cs        # Login, token cache, auto-refresh
└── Sorcha.Agent.csproj

tests/Sorcha.Agent.Tests/
├── Configuration/
│   ├── ActorDefinitionLoaderTests.cs
│   └── VariableResolverTests.cs
├── Inbox/
│   └── CompositeInboxListenerTests.cs
├── Decision/
│   ├── RulesDecisionEngineTests.cs
│   └── AiDecisionEngineTests.cs
├── Execution/
│   ├── ActionExecutorTests.cs
│   └── AuditLoggerTests.cs
└── Sorcha.Agent.Tests.csproj

walkthroughs/ConstructionPermit/
├── actors/                        # NEW: actor definition files
│   ├── contractor.json
│   ├── structural-engineer.json
│   ├── planning-officer.json
│   ├── building-inspector.json
│   └── council-admin.json
└── run-agents.ps1                 # NEW: launcher for all 5 actors
```

**Structure Decision**: Single console application project with one test project. Follows `src/Apps/Sorcha.Cli/` conventions. No separate library project — the agent is a standalone tool, not a reusable SDK.
