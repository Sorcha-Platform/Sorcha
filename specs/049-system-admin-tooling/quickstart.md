# Quickstart: System Administration Tooling (Feature 049)

## Prerequisites

- .NET 10 SDK
- Docker Desktop (for services)
- Branch: `049-system-admin-tooling`

## Build Order

Feature 049 is UI-first, then CLI. Build sequence:

### Phase 1: Infrastructure (YARP + Service Interfaces)
1. Add YARP route for `/api/admin/validators/{**}`
2. Create `IServicePrincipalService` + implementation in `Sorcha.UI.Core`
3. Create `ISystemRegisterService` + implementation in `Sorcha.UI.Core`
4. Extend `IValidatorAdminService` with consent, metrics, threshold, config methods
5. Extend `IRegisterService` with policy methods
6. Register new services in DI

### Phase 2: UI — Service Principals (P1)
7. Replace `ServicePrincipalList` with `ServicePrincipalCrud` component
8. Create `SecretDisplayDialog` for one-time credential display
9. Create `EditScopesDialog` for inline scope editing
10. Wire up suspend/reactivate/revoke/rotate actions

### Phase 3: UI — Register Policy (P1)
11. Create `RegisterPolicyStep` for Create Register wizard
12. Create `RegisterPolicyTab` for Register detail page
13. Create `PolicyHistoryDialog` for version history
14. Create `PolicyDiffDialog` for propose-update flow

### Phase 4: UI — Validator Tabs (P1-P3)
15. Refactor `ValidatorPanel` to multi-tab layout
16. Create `ValidatorConsentPanel` with bulk approve/reject
17. Create `ValidatorMetricsPanel` with KPI cards + subsystem panels
18. Create `ValidatorThresholdPanel` with status + setup form
19. Create `ValidatorConfigPanel` with read-only config display

### Phase 5: UI — System Register Page (P2)
20. Create `/admin/system-register` page
21. Create `SystemRegisterDashboard` component
22. Add nav link to MainLayout

### Phase 6: CLI Commands
23. Add `register policy get|history|update` commands
24. Add `register system status|blueprints` commands
25. Add `validator consent pending|approve|reject|refresh` commands
26. Add `validator metrics [subsystem]` commands
27. Add `validator threshold status|setup` commands
28. Extend Refit interfaces

### Phase 7: Tests
29. Unit tests for all new UI service implementations
30. Unit tests for all new CLI commands
31. E2E Playwright tests for admin pages

## Verification

```bash
# Build
dotnet build

# Run tests
dotnet test

# Start services
docker-compose up -d

# Verify UI
# Navigate to http://localhost/app/admin/principals
# Navigate to http://localhost/app/admin/validator
# Navigate to http://localhost/app/admin/system-register

# Verify CLI
dotnet run --project src/Apps/Sorcha.Cli -- register policy get --register-id <id>
dotnet run --project src/Apps/Sorcha.Cli -- validator metrics
dotnet run --project src/Apps/Sorcha.Cli -- register system status
```
