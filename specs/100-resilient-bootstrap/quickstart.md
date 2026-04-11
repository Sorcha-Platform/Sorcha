# Quickstart: Resilient System Register Bootstrap

**Feature**: 100-resilient-bootstrap
**Date**: 2026-04-11

## What This Feature Changes

The system register bootstrapper (`SystemRegisterBootstrapper`) gains a `BootstrapMode` configuration that controls how a node obtains its system register on first startup.

## Files to Modify

| File | Change |
|------|--------|
| `src/Common/Sorcha.ServiceDefaults/SystemRegisterOptions.cs` | Add `BootstrapMode` enum and retry timing properties |
| `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs` | Replace fixed 3-retry loop with mode-driven bootstrap strategy |
| `src/Services/Sorcha.Register.Service/appsettings.json` | Add default `BootstrapMode: Auto` and retry timing defaults |
| `docker-compose.yml` | No change needed (Auto is default) |
| `docker-compose.n1.yml` | Set `SystemRegister__BootstrapMode=SyncOnly` for production node |

## Files to Create

| File | Purpose |
|------|---------|
| `tests/Sorcha.Register.Service.Tests/Services/SystemRegisterBootstrapperTests.cs` | Unit tests for all three bootstrap modes, phase transitions, cancellation |

## Files Unchanged

| File | Why |
|------|-----|
| `GenesisIngestionService.cs` | Genesis loading/verification logic is correct — only orchestration changes |
| `SystemRegisterSyncVerifier.cs` | Peer trust verification is unchanged |
| `SystemRegisterCommands.cs` (CLI) | Genesis ceremony is unchanged |
| `GenesisFileLoader.cs` | File loading logic is unchanged |

## Implementation Order

1. **Extend `SystemRegisterOptions`** — Add `BootstrapMode` enum and timing properties. This is the foundation everything depends on.

2. **Refactor `SystemRegisterBootstrapper`** — Replace `BootstrapWithRetryAsync` with mode-branching logic:
   - `Auto`: Keep current behaviour (rename internal method for clarity)
   - `GenesisFile`: Direct call to `GenesisIngestionService.LoadAndVerifyGenesisAsync()` + `IngestGenesisAsync()`
   - `SyncOnly`: New two-phase retry loop (fast → backoff) that only checks local register existence

3. **Update appsettings.json** — Add defaults for new properties.

4. **Write tests** — Cover each mode independently, phase transitions, cancellation handling, configuration validation.

5. **Update docker-compose.n1.yml** — Set production node to `SyncOnly`.

## Quick Test

```bash
# Verify Auto mode (default) still works
docker-compose up -d
# System register should be available within 30 seconds

# Verify SyncOnly mode waits for peers
# (set env var, start without peers, observe logs)
SystemRegister__BootstrapMode=SyncOnly dotnet run --project src/Services/Sorcha.Register.Service
# Should see "Bootstrap mode: SyncOnly" and periodic retry logs

# Verify GenesisFile mode ingests immediately
SystemRegister__BootstrapMode=GenesisFile dotnet run --project src/Services/Sorcha.Register.Service
# Should ingest embedded genesis immediately without peer sync
```
