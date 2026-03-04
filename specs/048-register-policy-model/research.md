# Research: Unified Register Policy Model & System Register

**Feature**: 048-register-policy-model | **Date**: 2026-03-04

## Research Areas

### 1. Where Does RegisterPolicy Live on RegisterControlRecord?

**Decision**: Add a nullable `RegisterPolicy?` property to `RegisterControlRecord`, parallel to the existing `CryptoPolicy?` property.

**Rationale**: The `CryptoPolicy` pattern (added in Feature 045) established the precedent: nullable for backward compatibility, serialized with `JsonIgnoreCondition.WhenWritingNull`, versioned independently. `RegisterPolicy` follows the same pattern — a versioned, nullable policy object that defaults when absent.

**Alternatives considered**:
- *Embed sections directly on RegisterControlRecord* — Rejected: pollutes the control record with 20+ fields; no versioning; harder to validate as a unit.
- *Separate RegisterPolicy document stored outside Control TX* — Rejected: breaks the "policy is on-chain" requirement (FR-019). Policy must be reconstructable from docket 0.
- *Merge CryptoPolicy into RegisterPolicy* — Rejected: spec explicitly assumes CryptoPolicy remains separate (Assumptions section). Different evolution cadence. Breaking change to existing registers.

### 2. How GenesisConfigService Reads Policy (Fallback Chain)

**Decision**: Three-tier fallback: (1) Read `RegisterPolicy` from Control record if present → (2) Parse legacy `controlBlueprint`/`configuration` JSON properties → (3) Apply hardcoded defaults.

**Rationale**: GenesisConfigService currently reads from genesis TX payloads looking for `controlBlueprint` or `configuration` properties, then falls back to hardcoded defaults. The new model adds a first-priority check: if `RegisterControlRecord.RegisterPolicy` is non-null, use it directly. This satisfies FR-035/FR-036 (backward compatibility) while giving new registers a formal model.

**Current defaults (preserved as fallback)**:
| Section | Parameter | Default |
|---------|-----------|---------|
| Consensus | SignatureThresholdMin | 2 |
| Consensus | SignatureThresholdMax | 10 |
| Consensus | DocketTimeout | 30s |
| Consensus | MaxTransactionsPerDocket | 1000 |
| Consensus | DocketBuildInterval | 100ms |
| Validators | RegistrationMode | "public" |
| Validators | MinValidators | 1 |
| Validators | MaxValidators | 100 |
| Validators | RequireStake | false |
| LeaderElection | Mechanism | "rotating" |
| LeaderElection | HeartbeatInterval | 1s |
| LeaderElection | LeaderTimeout | 5s |
| LeaderElection | TermDuration | 1 min |

**New governance defaults**:
| Parameter | Default | Source |
|-----------|---------|--------|
| QuorumFormula | strict-majority | FR-005 |
| ProposalTtl | 7 days | FR-006 |
| OwnerCanBypassQuorum | true | FR-007 |
| BlueprintVersion | "register-governance-v1" | FR-008 |

### 3. System Register Bootstrap Architecture

**Decision**: `SystemRegisterBootstrapper` as an `IHostedService` that runs on Register Service startup, gated by `SORCHA_SEED_SYSTEM_REGISTER` env var.

**Rationale**: The existing `SystemRegisterService` stores blueprints in a dedicated MongoDB collection (`system_register`). The spec requires a real on-chain register with a deterministic ID. The bootstrapper will:
1. Check env flag (default: false)
2. Check if System Register already exists (idempotent, FR-023)
3. Create a "system-setup" wallet via Wallet Service (FR-020a)
4. Call `RegisterCreationOrchestrator` to create the register with deterministic ID
5. Publish system blueprints as Control transactions (`control.blueprint.publish`)

**Deterministic ID**: SHA-256 of `"sorcha-system-register"`, truncated to 32 hex chars (matching RegisterId format).

**Alternatives considered**:
- *Keep existing MongoDB-only SystemRegisterService* — Rejected: spec requires on-chain register with peer replication (FR-025, SC-007).
- *Bootstrap in a separate CLI command* — Rejected: spec requires automatic bootstrap on startup in dev mode (Story 2, Scenario 1).
- *Use a migration script* — Rejected: must be idempotent and environment-aware.

### 4. Approved Validator List: On-Chain vs Operational

**Decision**: Two-tier model — on-chain `approvedValidators` list in `RegisterPolicy.Validators` for authorization, Redis entries with TTL for operational presence.

**Rationale**: The existing `ValidatorRegistry` already uses Redis with TTL for operational presence. The spec adds an authorization layer: in consent mode, validators must be on the on-chain approved list before they can register in Redis (FR-026). In public mode, the check is skipped (FR-027).

**Flow**:
```
Validator starts → calls ValidatorRegistry.RegisterAsync()
  → if consent mode: check on-chain approvedValidators list
    → if DID not found: reject registration
    → if DID found: proceed
  → create Redis entry with TTL (default 60s, FR-028)
  → heartbeat refreshes TTL every 30s
  → if TTL expires: validator disappears from operational list
  → consensus engine uses Redis list only (FR-029)
```

**Revocation flow** (FR-030):
```
Admin proposes removing validator from approved list
  → quorum reached → Control TX committed
  → validator's DID removed from on-chain list
  → validator's current Redis entry continues until TTL expires
  → on next heartbeat: Redis refresh rejected (DID not on approved list)
```

### 5. Policy Update via Governance

**Decision**: New action ID `control.policy.update` processed by `ControlDocketProcessor`. Payload contains full `RegisterPolicy` snapshot (FR-032) with incremented version.

**Rationale**: The existing `ControlDocketProcessor` already handles 8 control action types. Adding `control.policy.update` follows the same pattern: extract → validate → apply → event. The payload is a full snapshot (not a delta) to ensure any peer can reconstruct current policy from a single transaction.

**Validation before vote (FR-033)**:
- `RegisterPolicy.Version` must be `currentVersion + 1`
- `maxValidators >= minValidators`
- `signatureThresholdMin <= signatureThresholdMax`
- `quorumFormula` must be a known enum value
- `leaderElectionMechanism` must be a known enum value
- If `registrationMode` changes from public→consent: `transitionMode` required

**Transition modes (FR-034a)**:
- `immediate`: Unapproved validators cannot refresh Redis TTL after commit
- `grace-period` (default): Unapproved validators continue for one TTL cycle

### 6. Quorum Formula Parameterization

**Decision**: Add `QuorumFormula` enum to `GovernanceModels.cs` and parameterize `GovernanceRosterService.GetQuorumThreshold()`.

**Rationale**: Currently hardcoded to `floor(m/2) + 1` (strict majority). The spec requires three formulas (FR-005):
- `StrictMajority`: `floor(m/2) + 1` (current default)
- `Supermajority`: `floor(2*m/3) + 1`
- `Unanimous`: `m` (all voting members)

The quorum formula is read from `RegisterPolicy.Governance.QuorumFormula`. For registers without a policy, the default `StrictMajority` is used (backward-compatible).

**Impact**: `GovernanceRosterService.GetQuorumThreshold()` and `ValidateQuorumAsync()` need the formula as a parameter (read from the register's current policy).

### 7. Register Creation Request Extension

**Decision**: Add optional `RegisterPolicy? Policy` property to `InitiateRegisterCreationRequest`.

**Rationale**: The two-phase creation flow (initiate → finalize) already exists. Adding an optional policy to the initiate request lets creators specify policy at genesis (FR-017). If omitted, defaults are applied (FR-018). The policy is embedded in the genesis Control TX payload alongside the control record (FR-019).

**No breaking change**: The property is optional/nullable. Existing callers that don't provide it get default policy behavior.

### 8. Redis TTL Configuration

**Decision**: Default operational TTL = 60 seconds (FR-028), configurable via `RegisterPolicy.Validators` or `ValidatorRegistryConfiguration`.

**Rationale**: The existing `ValidatorRegistryConfiguration.CacheTtl` is 5 minutes (for the validator list cache, not individual validator presence). The spec requires a separate TTL for individual validator operational entries, defaulting to 60 seconds (2x the 30-second heartbeat interval). This ensures a validator that misses 2 heartbeats is removed from the operational list.

**Note**: The existing `CacheTtl` (5 min) is for the L2 Redis cache of the full validator list. The new operational TTL (60s) is per-validator entry in the operational presence keyspace.

## Unresolved Items

None — all NEEDS CLARIFICATION items resolved through codebase research.
