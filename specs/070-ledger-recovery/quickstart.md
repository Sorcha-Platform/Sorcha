# Quickstart: 070-ledger-recovery

## What This Feature Does

Makes published blueprints survive Blueprint Service restarts by recovering state from the register ledger on startup. Also provides accurate register health status and gates the health check during recovery.

## Key Changes

### 1. Register Service — New Endpoint
- **File**: `src/Services/Sorcha.Register.Service/Endpoints/` (new or existing file)
- **What**: `GET /api/registers/{registerId}/blueprints/published` — returns all blueprint-publish control transactions for a register
- **Why**: Blueprint Service needs to query published blueprints from the ledger during recovery

### 2. Blueprint Service — Recovery Hosted Service
- **File**: `src/Services/Sorcha.Blueprint.Service/Services/` (new hosted service)
- **What**: `BlueprintRecoveryService : BackgroundService` — on startup, queries all registers for published blueprints and populates `InMemoryPublishedBlueprintStore`. Runs periodic refresh timer.
- **Why**: Core recovery logic

### 3. Blueprint Service — Health Check Gating
- **File**: `src/Services/Sorcha.Blueprint.Service/Program.cs` (health endpoint)
- **What**: Health endpoint returns 503 "recovering" until recovery completes, then 200 "healthy" with register status
- **Why**: Prevents users seeing empty blueprint lists during recovery window

### 4. Register Status Model
- **File**: `src/Services/Sorcha.Blueprint.Service/Models/` (new models)
- **What**: `RegisterHealthStatus` enum, `RegisterRecoveryState` class, `RecoveryState` tracker
- **Why**: Track per-register health and recovery progress

### 5. Configuration
- **File**: `src/Services/Sorcha.Blueprint.Service/` (appsettings or options)
- **What**: `RecoveryOptions` — refresh interval (default 60s), startup timeout (default 30s), max retry attempts
- **Why**: Configurable recovery behaviour

## Implementation Order

1. **Register Service**: Add published blueprints query endpoint
2. **Blueprint Service**: Add recovery models (RegisterHealthStatus, RecoveryState)
3. **Blueprint Service**: Implement BlueprintRecoveryService hosted service
4. **Blueprint Service**: Update health check to gate on recovery
5. **Tests**: Unit tests for recovery logic, integration test for full restart cycle
6. **Verification**: docker-compose down -v, up, verify blueprints recovered

## Testing

- Unit tests for recovery logic (idempotent rebuild, offline handling, version ordering)
- Unit tests for register health status transitions
- Integration test: publish blueprint → restart service → verify recovered
- E2E test: full docker-compose restart cycle with walkthrough verification
