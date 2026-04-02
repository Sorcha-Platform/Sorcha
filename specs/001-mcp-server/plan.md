# Implementation Plan: Sorcha MCP Server

**Branch**: `001-mcp-server` | **Date**: 2026-01-29 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-mcp-server/spec.md`

## Summary

Build an MCP (Model Context Protocol) server that enables AI assistants to interact with the Sorcha decentralised register platform. The server exposes 33 tools and 6 resources across three user personas (Administrator, Designer, Participant), using the official Microsoft C# MCP SDK and leveraging existing Sorcha.ServiceClients for backend communication.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**:
- ModelContextProtocol (official C# SDK, NuGet prerelease)
- Microsoft.Extensions.Hosting (for DI integration)
- Sorcha.ServiceClients (existing service clients)
- Sorcha.ServiceDefaults (Aspire integration)
- FluentValidation 11.10.0 (input validation)
- System.IdentityModel.Tokens.Jwt 8.3.0 (JWT validation)

**Storage**: N/A (stateless server, backend services handle persistence)
**Testing**: xUnit + FluentAssertions + Moq (consistent with project standards)
**Target Platform**: Linux/Windows server, Docker container
**Project Type**: Console application (MCP server)
**Performance Goals**: Tool invocations complete within 2 seconds (SC-001), 95% success rate (SC-005)
**Constraints**: <500ms for authorization checks (SC-006), graceful degradation when services unavailable (SC-009)
**Scale/Scope**: 10-50 concurrent MCP connections, 3 user personas, 33 tools, 6 resources

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Microservices-First | ✅ PASS | MCP server is independently deployable, uses service clients for backend |
| II. Security First | ✅ PASS | JWT validation, RBAC, no secrets in code, FluentValidation for inputs |
| III. API Documentation | ✅ PASS | MCP tools self-document via descriptions; Scalar not applicable (not REST) |
| IV. Testing Requirements | ✅ PASS | Target >85% coverage, xUnit, integration tests planned |
| V. Code Quality | ✅ PASS | C# 13, async/await, DI, nullable enabled |
| VI. Blueprint Standards | ✅ PASS | N/A - server consumes blueprints, doesn't create them |
| VII. Domain-Driven Design | ✅ PASS | Uses Sorcha terminology (Blueprint, Action, Participant, Disclosure) |
| VIII. Observability | ✅ PASS | OpenTelemetry via ServiceDefaults, structured logging, health endpoints |

**Gate Result**: PASS - No violations requiring justification.

## Project Structure

### Documentation (this feature)

```text
specs/001-mcp-server/
├── plan.md              # This file
├── research.md          # Phase 0 output (complete)
├── data-model.md        # Phase 1 output (complete)
├── quickstart.md        # Phase 1 output (complete)
├── contracts/           # Phase 1 output (complete)
│   ├── mcp-tools.json
│   └── mcp-resources.json
├── checklists/
│   └── requirements.md  # Quality checklist
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/Apps/Sorcha.McpServer/
├── Program.cs                    # Entry point, MCP server startup
├── Sorcha.McpServer.csproj       # Project file
├── appsettings.json              # Configuration
├── Tools/                        # MCP tool implementations
│   ├── Admin/                    # Administrator tools (FR-010 to FR-019)
│   │   ├── HealthCheckTool.cs
│   │   ├── LogQueryTool.cs
│   │   ├── MetricsTool.cs
│   │   ├── TenantManagementTools.cs
│   │   ├── UserManagementTools.cs
│   │   ├── PeerStatusTool.cs
│   │   ├── ValidatorStatusTool.cs
│   │   ├── RegisterStatsTool.cs
│   │   ├── AuditLogTool.cs
│   │   └── TokenRevocationTool.cs
│   ├── Designer/                 # Designer tools (FR-020 to FR-032)
│   │   ├── BlueprintListTool.cs
│   │   ├── BlueprintGetTool.cs
│   │   ├── BlueprintCreateTool.cs
│   │   ├── BlueprintUpdateTool.cs
│   │   ├── BlueprintValidateTool.cs
│   │   ├── BlueprintSimulateTool.cs
│   │   ├── DisclosureAnalysisTool.cs
│   │   ├── BlueprintDiffTool.cs
│   │   ├── BlueprintExportTool.cs
│   │   ├── SchemaValidateTool.cs
│   │   ├── SchemaGenerateTool.cs
│   │   ├── JsonLogicTestTool.cs
│   │   └── WorkflowInstancesTool.cs
│   └── Participant/              # Participant tools (FR-033 to FR-042)
│       ├── InboxListTool.cs
│       ├── ActionDetailsTool.cs
│       ├── ActionSubmitTool.cs
│       ├── ActionValidateTool.cs
│       ├── TransactionHistoryTool.cs
│       ├── WorkflowStatusTool.cs
│       ├── DisclosedDataTool.cs
│       ├── WalletInfoTool.cs
│       ├── WalletSignTool.cs
│       └── RegisterQueryTool.cs
├── Resources/                    # MCP resource providers (FR-043 to FR-048)
│   ├── BlueprintsResource.cs     # sorcha://blueprints
│   ├── BlueprintResource.cs      # sorcha://blueprints/{id}
│   ├── InboxResource.cs          # sorcha://inbox
│   ├── WorkflowResource.cs       # sorcha://workflows/{id}
│   ├── RegisterResource.cs       # sorcha://registers/{id}
│   └── SchemaResource.cs         # sorcha://schemas/{name}
├── Services/                     # MCP-specific services
│   ├── McpSessionService.cs      # Session/context management (JWT-driven lifetime)
│   ├── McpAuthorizationService.cs # RBAC enforcement
│   ├── RateLimitService.cs       # Per-user/tenant rate limiting
│   └── ToolAuditService.cs       # Tool invocation logging
├── Infrastructure/               # Cross-cutting concerns
│   ├── JwtValidationHandler.cs   # JWT token validation
│   ├── McpErrorHandler.cs        # Consistent error responses
│   ├── GracefulDegradation.cs    # Backend availability handling
│   └── ToolMetrics.cs            # OpenTelemetry instrumentation
└── Models/                       # DTOs for tool inputs/outputs
    ├── ToolInputs/               # Input schemas for tools
    └── ToolOutputs/              # Output schemas for tools

tests/Sorcha.McpServer.Tests/
├── Tools/                        # Unit tests for each tool
│   ├── Admin/
│   ├── Designer/
│   └── Participant/
├── Resources/                    # Unit tests for resources
├── Services/                     # Unit tests for services
└── Integration/                  # Integration tests
    ├── AdminToolsIntegrationTests.cs
    ├── DesignerToolsIntegrationTests.cs
    └── ParticipantToolsIntegrationTests.cs
```

**Structure Decision**: Single project for MCP server under `src/Apps/` following existing CLI pattern. Leverages `Sorcha.ServiceClients` for all backend communication. Tools organized by persona for discoverability.

## Complexity Tracking

> No violations requiring justification. Design follows existing patterns.

## Clarifications Applied

From spec clarification session 2026-01-29:

| Question | Answer | Impact |
|----------|--------|--------|
| Expected concurrent scale | 10-50 concurrent MCP connections | In-memory rate limiting sufficient |
| Availability requirements | Best effort with graceful degradation | Added SC-009, GracefulDegradation.cs |
| Session timeout behavior | JWT-driven (no separate idle timeout) | Simplified session management |

## Phase 0: Research Summary

### MCP C# SDK Availability

**Decision**: Use official `ModelContextProtocol` NuGet package (prerelease)
**Rationale**: Microsoft-maintained, supports latest MCP spec, integrates with Microsoft.Extensions.Hosting
**Alternatives Considered**:
- mcpdotnet (community) - Less maintained, missing latest features
- Custom implementation - Unnecessary given official SDK

### Transport Mode Strategy

**Decision**: Support both stdio (primary) and HTTP/SSE (future)
**Rationale**: stdio is simplest for local AI assistants; HTTP/SSE enables remote/web scenarios
**Implementation**:
```csharp
.WithStdioServerTransport()  // Primary transport
// HTTP/SSE can be added later via .WithHttpServerTransport()
```

### Authentication Pattern

**Decision**: JWT token passed via MCP initialization parameters, session valid for JWT lifetime
**Rationale**: MCP servers receive configuration on startup; JWT claims determine available tools
**Flow**:
1. AI assistant initiates MCP connection with JWT token
2. McpServer validates JWT via Tenant Service
3. Claims extracted to McpSessionService (userId, roles, tenantId)
4. Tool availability filtered by role claims
5. Session ends when JWT expires (no separate idle timeout)

### Service Client Reuse

**Decision**: Reuse Sorcha.ServiceClients library directly
**Rationale**: Existing clients cover all needed operations; maintains consistency
**Available Clients**:
- `IBlueprintServiceClient` - Blueprint operations
- `IRegisterServiceClient` - Transaction/register operations
- `IWalletServiceClient` - Wallet/signing operations
- `IParticipantServiceClient` - Participant queries
- `IPeerServiceClient` - Peer network status
- `IValidatorServiceClient` - Validator status

### Rate Limiting Strategy

**Decision**: In-memory rate limiting with sliding window
**Rationale**: Simple, effective for 10-50 concurrent connections; Redis integration available for scale
**Limits**:
- Per-user: 100 requests/minute
- Per-tenant: 1000 requests/minute
- Admin tools: 50 requests/minute (more expensive operations)

### Graceful Degradation

**Decision**: Report partial availability when backend services unavailable
**Rationale**: Supports SC-009; allows independent tools to continue functioning
**Implementation**:
- Health check each backend before tool execution
- Return structured error for unavailable service
- Tools not dependent on failed service remain operational

## Dependency Summary

| Dependency | Purpose | Version |
|------------|---------|---------|
| ModelContextProtocol | MCP SDK | prerelease |
| Microsoft.Extensions.Hosting | DI/Lifetime | 10.0.x |
| Sorcha.ServiceClients | Backend communication | (project ref) |
| Sorcha.ServiceDefaults | Aspire integration | (project ref) |
| FluentValidation | Input validation | 11.10.0 |
| System.IdentityModel.Tokens.Jwt | JWT parsing | 8.3.0 |

## Risk Assessment

| Risk | Mitigation |
|------|------------|
| MCP SDK is prerelease | Pin version, monitor for breaking changes |
| Backend service unavailable | Graceful degradation per SC-009 |
| JWT token expiration during session | Clear error message, session ends cleanly |
| Rate limit bypass | Server-side enforcement, no client trust |
| Scale beyond 50 connections | Upgrade to Redis-backed rate limiting |

## Generated Artifacts

| Artifact | Status | Path |
|----------|--------|------|
| research.md | ✅ Complete | [research.md](research.md) |
| data-model.md | ✅ Complete | [data-model.md](data-model.md) |
| mcp-tools.json | ✅ Complete | [contracts/mcp-tools.json](contracts/mcp-tools.json) |
| mcp-resources.json | ✅ Complete | [contracts/mcp-resources.json](contracts/mcp-resources.json) |
| quickstart.md | ✅ Complete | [quickstart.md](quickstart.md) |

## Next Step

Run `/speckit.tasks` to generate the implementation task list from this plan.
