# Data Model: Unified Register Policy Model & System Register

**Feature**: 048-register-policy-model | **Date**: 2026-03-04

## Entity Relationship Overview

```
RegisterControlRecord (existing)
├── Attestations[]          (existing - admin roster)
├── CryptoPolicy?           (existing - crypto algorithms)
└── RegisterPolicy?         (NEW - operational policy)
    ├── GovernanceConfig
    ├── ValidatorConfig
    │   └── ApprovedValidator[]
    ├── ConsensusConfig
    └── LeaderElectionConfig
```

---

## New Entities

### RegisterPolicy (Value Object)

The unified per-register operational policy. Embedded on `RegisterControlRecord`. Nullable for backward compatibility with pre-feature registers.

| Field | Type | Required | Default | Constraints | FR |
|-------|------|----------|---------|-------------|-----|
| Version | uint | Yes | 1 | >= 1, monotonically increasing | FR-004 |
| Governance | GovernanceConfig | Yes | (see below) | | FR-001 |
| Validators | ValidatorConfig | Yes | (see below) | | FR-001 |
| Consensus | ConsensusConfig | Yes | (see below) | | FR-001 |
| LeaderElection | LeaderElectionConfig | Yes | (see below) | | FR-001 |
| UpdatedAt | DateTimeOffset | Yes | CreatedAt | Set on each update | |
| UpdatedBy | string? | No | null | DID of updater (null for genesis) | |

**Factory method**: `RegisterPolicy.CreateDefault()` returns a fully populated policy with all default values.

**JSON serialization**: `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` on `RegisterControlRecord.RegisterPolicy` (matching `CryptoPolicy` pattern).

---

### GovernanceConfig (Value Object)

| Field | Type | Required | Default | Constraints | FR |
|-------|------|----------|---------|-------------|-----|
| QuorumFormula | QuorumFormula | Yes | StrictMajority | Enum: StrictMajority, Supermajority, Unanimous | FR-005 |
| ProposalTtlDays | int | Yes | 7 | >= 1, <= 90 | FR-006 |
| OwnerCanBypassQuorum | bool | Yes | true | | FR-007 |
| BlueprintVersion | string | Yes | "register-governance-v1" | Non-empty, max 100 chars | FR-008 |

**QuorumFormula enum**:
```csharp
public enum QuorumFormula
{
    StrictMajority = 0,   // floor(m/2) + 1
    Supermajority = 1,    // floor(2*m/3) + 1
    Unanimous = 2         // m (all voting members)
}
```

**Quorum calculation**:
| Formula | m=1 | m=2 | m=3 | m=5 | m=7 | m=10 |
|---------|-----|-----|-----|-----|-----|------|
| StrictMajority | 1 | 2 | 2 | 3 | 4 | 6 |
| Supermajority | 1 | 2 | 3 | 4 | 5 | 7 |
| Unanimous | 1 | 2 | 3 | 5 | 7 | 10 |

---

### ValidatorConfig (Value Object)

| Field | Type | Required | Default | Constraints | FR |
|-------|------|----------|---------|-------------|-----|
| RegistrationMode | RegistrationMode | Yes | Public | Enum: Public, Consent | FR-009 |
| ApprovedValidators | List\<ApprovedValidator\> | Yes | [] | Max 100 entries | FR-010 |
| MinValidators | int | Yes | 1 | >= 1 | FR-011 |
| MaxValidators | int | Yes | 100 | >= MinValidators, <= 100 | FR-011 |
| RequireStake | bool | Yes | false | | FR-012 |
| StakeAmount | decimal? | No | null | > 0 when RequireStake=true | FR-012 |
| OperationalTtlSeconds | int | Yes | 60 | >= 10, <= 600 | FR-028 |

**RegistrationMode enum**:
```csharp
public enum RegistrationMode
{
    Public = 0,    // Any validator can register
    Consent = 1    // Only approved validators can register
}
```

---

### ApprovedValidator (Value Object)

An entry in the on-chain approved validator list. Represents authorization, not operational presence.

| Field | Type | Required | Default | Constraints | FR |
|-------|------|----------|---------|-------------|-----|
| Did | string | Yes | — | DID format, max 255 chars | FR-010 |
| PublicKey | string | Yes | — | Base64-encoded | FR-010 |
| ApprovedAt | DateTimeOffset | Yes | — | | FR-010 |
| ApprovedBy | string? | No | null | DID of approver | |

---

### ConsensusConfig (Value Object)

| Field | Type | Required | Default | Constraints | FR |
|-------|------|----------|---------|-------------|-----|
| SignatureThresholdMin | int | Yes | 2 | >= 1 | FR-013 |
| SignatureThresholdMax | int | Yes | 10 | >= SignatureThresholdMin | FR-013 |
| MaxTransactionsPerDocket | int | Yes | 1000 | >= 1, <= 10000 | FR-014 |
| DocketBuildIntervalMs | int | Yes | 100 | >= 10, <= 60000 | FR-014 |
| DocketTimeoutSeconds | int | Yes | 30 | >= 5, <= 300 | FR-014 |

---

### LeaderElectionConfig (Value Object)

| Field | Type | Required | Default | Constraints | FR |
|-------|------|----------|---------|-------------|-----|
| Mechanism | ElectionMechanism | Yes | Rotating | Enum: Rotating, Raft, StakeWeighted | FR-015 |
| HeartbeatIntervalMs | int | Yes | 1000 | >= 100, <= 30000 | FR-016 |
| LeaderTimeoutMs | int | Yes | 5000 | > HeartbeatIntervalMs | FR-016 |
| TermDurationSeconds | int? | No | 60 | >= 10 when set | FR-016 |

**ElectionMechanism enum**:
```csharp
public enum ElectionMechanism
{
    Rotating = 0,       // Round-robin (current implementation)
    Raft = 1,           // Raft consensus (future)
    StakeWeighted = 2   // Stake-weighted election (future)
}
```

---

### PolicyUpdatePayload (DTO — extends ControlPayload)

Used for `control.policy.update` Control transactions.

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| Policy | RegisterPolicy | Yes | Full snapshot, version = current + 1 |
| TransitionMode | TransitionMode? | No | Required when RegistrationMode changes public→consent |
| UpdatedBy | string | Yes | DID of proposer |

**TransitionMode enum**:
```csharp
public enum TransitionMode
{
    Immediate = 0,     // Unapproved validators ejected at commit
    GracePeriod = 1    // Unapproved validators continue for one TTL cycle
}
```

---

## Modified Entities

### RegisterControlRecord (Existing — Modified)

**New field**:
| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| RegisterPolicy | RegisterPolicy? | No | null | Nullable for backward compatibility |

**JSON behavior**: `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` — omitted from serialization when null (legacy registers).

### ControlTransactionPayload (Existing — Modified)

No structural change. The `Roster` property already contains the full `RegisterControlRecord` snapshot, which now includes `RegisterPolicy?`. Genesis transactions automatically carry the policy.

### GovernanceOperation (Existing — Modified)

No structural change needed. Policy updates use a separate action ID (`control.policy.update`) rather than the governance proposal flow. However, policy updates still require quorum — they're submitted as Control transactions that go through the existing consensus pipeline.

### ControlActionType Enum (Existing — Modified)

**New value**:
```csharp
PolicyUpdate = 9    // Update register operational policy
```

**New action ID constant**: `"control.policy.update"`

---

## System Register Entities

### SystemRegisterConstants (New — Static)

| Constant | Value | Notes |
|----------|-------|-------|
| SystemRegisterId | SHA256("sorcha-system-register")[0:32] | 32-char hex, deterministic |
| SystemRegisterName | "Sorcha System Register" | Display name |
| DefaultBlueprintVersion | "register-governance-v1" | Default governance blueprint |
| SystemSetupWalletName | "system-setup" | Bootstrap wallet name |
| EnvSeedFlag | "SORCHA_SEED_SYSTEM_REGISTER" | Env var name (default: false) |
| EnvBlueprintOverride | "SORCHA_SYSTEM_REGISTER_BLUEPRINT" | Env var for custom blueprint |

---

## Validation Rules (FluentValidation)

### RegisterPolicyValidator

```
- Version >= 1
- Governance is not null → GovernanceConfigValidator
- Validators is not null → ValidatorConfigValidator
- Consensus is not null → ConsensusConfigValidator
- LeaderElection is not null → LeaderElectionConfigValidator
```

### GovernanceConfigValidator

```
- QuorumFormula is defined enum value
- ProposalTtlDays in [1, 90]
- BlueprintVersion is not empty, length <= 100
```

### ValidatorConfigValidator

```
- RegistrationMode is defined enum value
- ApprovedValidators.Count <= 100
- MinValidators >= 1
- MaxValidators >= MinValidators
- MaxValidators <= 100
- If RequireStake: StakeAmount > 0
- If !RequireStake: StakeAmount is null
- OperationalTtlSeconds in [10, 600]
- Each ApprovedValidator.Did is not empty, length <= 255
- Each ApprovedValidator.PublicKey is valid base64
```

### ConsensusConfigValidator

```
- SignatureThresholdMin >= 1
- SignatureThresholdMax >= SignatureThresholdMin
- MaxTransactionsPerDocket in [1, 10000]
- DocketBuildIntervalMs in [10, 60000]
- DocketTimeoutSeconds in [5, 300]
```

### LeaderElectionConfigValidator

```
- Mechanism is defined enum value
- HeartbeatIntervalMs in [100, 30000]
- LeaderTimeoutMs > HeartbeatIntervalMs
- If TermDurationSeconds != null: >= 10
```

---

## State Transitions

### RegisterPolicy Lifecycle

```
[No Policy] ──(genesis with policy)──→ [v1 Active]
[No Policy] ──(legacy register)──→ [Defaults Applied at Read Time]
[v1 Active] ──(control.policy.update)──→ [v2 Active]
[Defaults Applied] ──(control.policy.update)──→ [v1 Active]
```

### Approved Validator Lifecycle

```
[Not Listed] ──(control.validator.approve)──→ [Approved On-Chain]
[Approved On-Chain] + heartbeat ──→ [Approved + Operational (Redis)]
[Approved + Operational] - heartbeat ──→ [Approved + Offline (Redis TTL expired)]
[Approved On-Chain] ──(control.validator.remove)──→ [Removed]
[Removed + Operational] ──(TTL expires)──→ [Removed + Offline]
```

### System Register Lifecycle

```
[Not Exists] + SEED=true ──→ [Created with deterministic ID]
[Exists] + SEED=true ──→ [No-op (idempotent)]
[Any State] + SEED=false ──→ [No-op (not seeded)]
```
