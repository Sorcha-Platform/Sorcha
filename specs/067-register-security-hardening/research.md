# Research: Register TenantId Removal & Security Hardening

**Feature**: 067-register-security-hardening | **Date**: 2026-03-24

## R1: Subscription System for Register Access Control

**Decision**: Use existing `OrganizationRegisterSubscription` in Tenant Service as the authority for register access.

**Rationale**: The subscription model already links orgs to registers with status tracking (Active/Pending/Suspended/Revoked) and type (Owner/Public/Invited). It has endpoints, DB schema, UI client, and test coverage.

**Gap Identified**: No service-to-service client exists in `Sorcha.ServiceClients` for the Register Service to query subscriptions from Tenant Service. Currently only the UI calls these endpoints.

**Resolution**: Add `ISubscriptionServiceClient` to `Sorcha.ServiceClients` with a method `GetActiveRegisterIdsForOrgAsync(Guid orgId)` that calls `GET /api/organizations/{orgId}/register-subscriptions` (or the more efficient `GET /api/me/subscribed-registers` pattern adapted for service tokens). Register Service uses this to filter query results.

**Alternatives Considered**:
- Cache subscription data in Register Service Redis — rejected (adds staleness, duplication of authority)
- Push subscription changes to Register Service via events — deferred (optimisation for later, not MVP)

---

## R2: JWT Claims Available for Authorization

**Decision**: Use existing JWT claims (`org_id`, `role`, `wallet_address`, `token_type`) for all authorization decisions.

**Rationale**: User tokens already contain:
- `org_id` — current organisation context
- `role` — one or more roles (Administrator, SystemAdmin, Auditor, Designer, Member)
- `wallet_address` — first active linked wallet address
- `token_type` — "user" or "service"

**Key Finding**: The `wallet_address` claim contains the user's wallet address which can be matched against RegisterControlRecord attestation subjects (DIDs in format `did:sorcha:org:{walletAddress}`).

**No changes needed** to token generation. All required claims are already present.

---

## R3: Authorization Policy Design

**Decision**: Modify existing policies and add new ones.

| Policy | Change | Logic |
|--------|--------|-------|
| `CanManageRegisters` | **Modify** | Require `org_id` claim + `Administrator` or `SystemAdmin` role (not just presence of `org_id`) |
| `CanCreateSystemRegisters` | **New** | Require `RequireSystemAdmin` (SystemAdmin org + SystemAdmin role) |
| `CanDeleteRegisters` | **New** | Require authenticated user; attestation check done at business logic layer |

**Rationale**: Register creation is an admin-level operation. The existing `CanManageRegisters` only checks for `org_id` presence — too permissive. Attestation-based deletion cannot be a pure policy check (requires DB lookup), so it stays in business logic.

---

## R4: TenantId Removal Impact

**Decision**: Remove TenantId from domain models; MongoDB handles unmapped fields gracefully.

**Findings**:
- MongoDB C# driver ignores unmapped fields by default — existing documents with TenantId will load without errors
- The TenantId index will be dropped in code; existing MongoDB index remains harmless until manually cleaned
- `RegisterManager.GetRegistersByTenantAsync()` is replaced by subscription-based filtering
- `RegisterManager.DeleteRegisterAsync()` ownership check moves from TenantId comparison to attestation lookup
- SignalR `tenant:{tenantId}` groups → `register:{registerId}` groups
- Domain events drop TenantId; routing uses RegisterId

**Files requiring TenantId removal** (from research):
- `Register.cs` — remove property
- `RegisterControlRecord.cs` — remove property
- `RegisterCreationModels.cs` — remove from `InitiateRegisterCreationRequest`
- `RegisterEvents.cs` — remove from 3 event classes
- `MongoRegisterRepository.cs` — remove TenantId index creation
- `RegisterManager.cs` — remove `GetRegistersByTenantAsync`, update `DeleteRegisterAsync`
- `RegisterCreationOrchestrator.cs` — stop setting TenantId
- `SystemRegisterBootstrapper.cs` — stop setting TenantId, set Purpose instead
- `RegisterHub.cs` — replace tenant methods with register methods
- `RegisterEventBridgeService.cs` — replace tenant routing with register routing
- `Register Service Program.cs` — remove `?tenantId` query parameter
- `RegisterServiceClient.cs` — remove TenantId from internal DTO
- `RegisterCommands.cs` (CLI) — remove `--tenant-id` option
- `CreateRegisterWizard.razor` — remove TenantId parameter
- `RegisterService.cs` (UI) — remove tenantId parameter
- `McpSessionService.cs` — remove TenantId tracking
- All related test files

---

## R5: RegisterPurpose Enum Design

**Decision**: Simple enum (not `[Flags]`), stored as string in MongoDB.

**Rationale**: Purposes are mutually exclusive (a register is either General or System, not both). String storage in MongoDB enables human-readable queries and future extensibility without migration.

**Values**: `General` (default, value 0), `System` (value 1)

**Alternatives Considered**:
- `[Flags]` enum — rejected (purposes are mutually exclusive)
- String property instead of enum — rejected (loses type safety, validation)

---

## R6: Service-to-Service Subscription Resolution

**Decision**: Register Service calls Tenant Service via HTTP to resolve subscriptions at query time.

**Flow**:
1. User calls `GET /api/registers` with JWT
2. Register Service extracts `org_id` from JWT
3. Register Service calls Tenant Service `GET /api/organizations/{orgId}/register-subscriptions` via `ISubscriptionServiceClient`
4. Register Service filters local registers to only those with matching RegisterIds (plus System-purpose registers)
5. Returns filtered results

**Fail-closed**: If Tenant Service call fails, return empty results (deny access). Log the error.

**Alternatives Considered**:
- Redis cache of subscriptions — deferred (optimisation)
- Embed subscription IDs in JWT — rejected (JWT would grow unbounded, stale between refresh)
