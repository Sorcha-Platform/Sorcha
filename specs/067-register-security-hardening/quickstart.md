# Quickstart: Register TenantId Removal & Security Hardening

**Feature**: 067-register-security-hardening | **Date**: 2026-03-24

## Implementation Order

This feature has 9 user stories across 3 priority tiers. Implement in this order:

### Phase 1: Foundation (US1, US2, US8-partial)

1. **Add RegisterPurpose enum** to `Sorcha.Register.Models/Enums/`
2. **Add Purpose property** to `Register` entity (default `General`)
3. **Add Purpose to InitiateRegisterCreationRequest** (optional, default `General`)
4. **Update SystemRegisterBootstrapper** to set `Purpose = System`
5. **Add MongoDB index** on `Purpose` field
6. **Update RegisterCreationOrchestrator** to pass Purpose through
7. **Tighten CanManageRegisters policy** — require admin role (not just org_id presence)
8. **Add CanCreateSystemRegisters policy** — SystemAdmin only
9. **Remove AllowAnonymous** from initiate/finalize endpoints
10. **Add Purpose validation** — reject System purpose from non-system-admins
11. **Write unit tests** for enum, policies, creation flow

### Phase 2: Access Control (US3, US4, US5, US8-partial)

12. **Add ISubscriptionServiceClient** to `Sorcha.ServiceClients`
13. **Update GET /api/registers** — subscription-scoped filtering + System registers
14. **Update DELETE /api/registers/{id}** — attestation-based authorization
15. **Prevent System register deletion**
16. **Replace SignalR tenant groups** with register groups
17. **Add subscription access check** on SignalR register subscription
18. **Update RegisterEventBridgeService** routing
19. **Write API tests** for scoped queries, deletion auth, SignalR

### Phase 3: UI & CLI (US6, US7)

20. **Add Purpose dropdown** to CreateRegisterWizard Options step
21. **Filter System option** by user role (system admin only)
22. **Show Purpose in review step**
23. **Add --purpose flag** to CLI register create command
24. **Show Purpose in CLI list/get output**
25. **Write component tests** for wizard dropdown
26. **Write CLI tests** for --purpose option

### Phase 4: Cleanup (US9)

27. **Remove TenantId** from Register, RegisterControlRecord, creation DTOs
28. **Remove TenantId** from domain events
29. **Remove TenantId MongoDB index** creation
30. **Remove ?tenantId query parameter** from endpoints
31. **Remove --tenant-id** from CLI
32. **Remove TenantId** from UI models, service clients, MCP tools
33. **Update all affected tests**

## Key Files to Modify

| File | Phase | Changes |
|------|-------|---------|
| `Register.cs` | 1 + 4 | Add Purpose (P1), Remove TenantId (P4) |
| `RegisterCreationModels.cs` | 1 + 4 | Add Purpose (P1), Remove TenantId (P4) |
| `RegisterControlRecord.cs` | 4 | Remove TenantId |
| `AuthenticationExtensions.cs` (Register) | 1 | Tighten policies |
| `RegisterManager.cs` | 1 + 2 + 4 | Purpose flow (P1), access control (P2), remove tenant (P4) |
| `RegisterCreationOrchestrator.cs` | 1 + 4 | Purpose flow (P1), remove tenant (P4) |
| `SystemRegisterBootstrapper.cs` | 1 + 4 | Set Purpose (P1), remove TenantId (P4) |
| `MongoRegisterRepository.cs` | 1 + 4 | Add Purpose index (P1), remove TenantId index (P4) |
| `Register Service Program.cs` | 1 + 2 + 4 | Auth (P1), filtering (P2), remove param (P4) |
| `RegisterHub.cs` | 2 | Replace tenant→register groups |
| `RegisterEventBridgeService.cs` | 2 | Replace tenant→register routing |
| `RegisterEvents.cs` | 2 + 4 | Add Purpose to created event (P2), remove TenantId (P4) |
| `CreateRegisterWizard.razor` | 3 + 4 | Add dropdown (P3), remove TenantId (P4) |
| `RegisterCommands.cs` (CLI) | 3 + 4 | Add --purpose (P3), remove --tenant-id (P4) |
| `RegisterServiceClient.cs` | 4 | Remove TenantId from DTO |
| `SubscriptionServiceClient.cs` | 2 | **NEW** — service-to-service subscription queries |

## Build & Test

```bash
# After each phase:
dotnet build --force
dotnet test --filter "FullyQualifiedName~Register"

# Full suite:
dotnet test
```

## Risk Mitigation

- **Phase 4 is separate** — all functional replacements are in place before TenantId removal
- **MongoDB unmapped fields** — existing TenantId data harmless after removal
- **Fail-closed** — subscription resolution failure = empty results, not open access
