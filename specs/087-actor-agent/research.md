# Research: Autonomous Actor Agent Framework

**Date**: 2026-04-07
**Feature**: 087-actor-agent

## Decision 1: JSON Logic Library

**Decision**: Use `JsonLogic` v6.0.1 (already in `Directory.Packages.props`)

**Rationale**: The Blueprint Engine already uses this package via `IJsonLogicEvaluator` with caching, validation, and security constraints (max 100 calculations). Using the same library avoids adding a new dependency and ensures consistent behaviour with how blueprints evaluate conditions.

**Alternatives considered**:
- `JsonLogic.Net` v1.1.11 — also in the codebase (MCP Server) but uses Newtonsoft.Json. Legacy dependency, not idiomatic for .NET 10.
- Custom evaluator — unnecessary when a tested evaluator exists.

## Decision 2: Authentication Approach

**Decision**: Reuse `Sorcha.Cli` authentication patterns (email/password login, token caching, auto-refresh)

**Rationale**: The CLI already implements `IAuthenticationService` with encrypted `TokenCache`, profile-based config, and OAuth2 password grant. The agent needs the same flow: authenticate with credentials from the actor file, cache the JWT, and refresh on expiry.

**Alternatives considered**:
- Service principal auth (client_credentials) — would work but requires provisioning a service principal per actor. User auth is simpler for walkthrough use cases.
- Raw HTTP auth — reinventing what the CLI already does.

## Decision 3: Inbox Discovery Mechanism

**Decision**: SignalR primary (via `SorchaHubConnectionBuilder`) + HTTP polling fallback

**Rationale**: `SorchaHubConnectionBuilder.Build()` already provides JWT-authenticated SignalR connections with infinite reconnection and exponential backoff (1s→2s→5s→10s→30s). The agent receives `InboundActionEvent` via the hub. Polling fallback queries register transactions by wallet to catch missed events.

**Key finding**: `InboundActionEvent` is delivered via Redis pub/sub (`wallet:notifications`) to the SignalR hub. The event includes `BlueprintId`, `InstanceId`, `ActionId`, `NextActionId`, `SenderAddress`, `TransactionId`, `RegisterId` — sufficient context for the decision engine.

**Alternatives considered**:
- Polling only — simpler but adds 60s latency. Unacceptable for demos.
- Redis direct — would bypass the SignalR abstraction layer and couple to infrastructure.

## Decision 4: Action Execution Path

**Decision**: Use `IValidatorServiceClient.SubmitTransactionAsync()` + `IWalletServiceClient.SignTransactionAsync()`

**Rationale**: This is the C# equivalent of the PowerShell `Invoke-SorchaAction`. The agent:
1. Builds the payload from the decision engine
2. Signs the transaction via the Wallet Service
3. Submits the signed transaction to the Validator Service
4. Receives `TransactionSubmissionResult` with success/error

`SequenceNumber` (replay protection) is obtained via `GetNextSequenceNumberAsync()`.

**Alternatives considered**:
- Blueprint Service action execution endpoint — exists but is higher-level and may not be available via HTTP client.
- Direct register submission — bypasses validation.

## Decision 5: Polly Resilience Configuration

**Decision**: Mirror `Sorcha.Cli` Polly pipeline exactly: Timeout (30s) → Retry (3, exponential backoff) → Circuit Breaker (5 failures, 30s break)

**Rationale**: The CLI already implements this exact pipeline in `HttpClientFactory.cs`. Using the same configuration ensures consistent behaviour and the actor definition file allows overriding defaults.

**Alternatives considered**:
- Custom resilience — unnecessary when the CLI pattern is proven.
- No resilience — unacceptable for a long-running process.

## Decision 6: Project Structure

**Decision**: New project at `src/Apps/Sorcha.Agent/` following `Sorcha.Cli` conventions

**Rationale**: Mirror the CLI's modular structure: DI in `Program.cs`, commands via `System.CommandLine`, services via interfaces, consistent exit codes. Reference `Sorcha.ServiceClients.Http` for all HTTP/SignalR communication, `Sorcha.Blueprint.Models` for domain models, and `Sorcha.Blueprint.Engine` for `IJsonLogicEvaluator`.

**Key dependencies** (all already in `Directory.Packages.props`):
- `System.CommandLine`
- `Sorcha.ServiceClients.Http`
- `Sorcha.Blueprint.Models`
- `Sorcha.Blueprint.Engine` (for `IJsonLogicEvaluator`)
- `Microsoft.Extensions.Http.Polly`
- `Spectre.Console` (for rich console output)

## Decision 7: AI Mode Integration

**Decision**: Call Claude API directly via Anthropic SDK for v1. No MCP dependency.

**Rationale**: The actor needs to send action context + persona prompt and receive a structured payload. Direct API call is simpler than standing up an MCP connection. The prompt file contains the persona; the agent constructs a message with the action schema and previous payload.

**Alternatives considered**:
- MCP Server integration — more complex, requires running an MCP server per actor. Better for v2.
- No AI mode in v1 — reduces scope but was explicitly requested.

## Assumptions Verified

1. **Inbox endpoint exists**: `InboundActionEvent` is delivered via SignalR hub. For polling, `IRegisterServiceClient.GetTransactionsByWalletAsync()` provides transaction history per wallet.
2. **SignalR hub events**: The `InboundActionEvent` model contains `BlueprintId`, `InstanceId`, `ActionId`, `NextActionId` — sufficient for the decision engine to determine if this is an action the actor should handle.
3. **JSON Logic library**: `JsonLogic` v6.0.1 is already available and battle-tested in the Blueprint Engine.
4. **State.json format**: Walkthrough setup scripts produce state files with `registerId`, `orgId`, `walletAddress` and other IDs needed by actor configs.

## Open Items

None — all NEEDS CLARIFICATION items resolved through codebase research.
