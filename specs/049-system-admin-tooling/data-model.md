# Data Model: System Administration Tooling (Feature 049)

**Date**: 2026-03-05
**Branch**: `049-system-admin-tooling`

## Overview

Feature 049 is pure frontend/CLI work — no new database entities. All models below are **view models** (UI) and **DTOs** (CLI) that map to existing backend response types.

---

## UI View Models

### Service Principal Models

```
ServicePrincipalViewModel
├── Id: Guid
├── ServiceName: string
├── ClientId: string
├── Scopes: string[]
├── Status: string (Active | Suspended | Revoked)
├── CreatedAt: DateTimeOffset
├── LastUsedAt: DateTimeOffset?
└── ExpiresAt: DateTimeOffset?

CreateServicePrincipalRequest
├── ServiceName: string (required, 1-100 chars)
├── Scopes: string[] (required, at least 1)
└── ExpirationDuration: ExpirationPreset (ThirtyDays | NinetyDays | OneYear | NoExpiry)

ServicePrincipalSecretViewModel (one-time display)
├── ClientId: string
├── ClientSecret: string
└── Warning: string

ServicePrincipalListResult
├── Items: List<ServicePrincipalViewModel>
├── TotalCount: int
└── IncludesInactive: bool
```

### Register Policy Models

```
RegisterPolicyViewModel
├── RegisterId: string
├── Policy: RegisterPolicyFields
├── IsDefault: bool
└── Version: uint

RegisterPolicyFields
├── MinimumValidators: uint
├── MaximumValidators: uint
├── SignatureThreshold: uint
├── RegistrationMode: string (Open | Consent)
├── ApprovedValidators: List<ApprovedValidatorInfo>
└── TransitionMode: string?

ApprovedValidatorInfo
├── ValidatorId: string
└── ApprovedAt: DateTimeOffset

PolicyVersionViewModel
├── Version: uint
├── Policy: RegisterPolicyFields
├── UpdatedAt: DateTimeOffset
└── UpdatedBy: string?

PolicyHistoryViewModel
├── RegisterId: string
├── Versions: List<PolicyVersionViewModel>
├── Page: int
├── PageSize: int
├── TotalCount: int
└── TotalPages: int

PolicyUpdateProposalViewModel
├── RegisterId: string
├── ProposedVersion: uint
├── CurrentVersion: uint
├── RequiresGovernanceVote: bool
└── Message: string
```

### Validator Consent Models

```
PendingValidatorViewModel
├── ValidatorId: string
├── RegisterId: string
├── RegisterName: string
├── RequestedAt: DateTimeOffset
└── IsSelected: bool (UI-only, for bulk selection)

ConsentQueueViewModel
├── RegisterGroups: List<RegisterConsentGroup>
└── TotalPending: int

RegisterConsentGroup
├── RegisterId: string
├── RegisterName: string
├── RegistrationMode: string
├── PendingValidators: List<PendingValidatorViewModel>
└── ApprovedValidators: List<ApprovedValidatorInfo>
```

### Validator Metrics Models

```
AggregatedMetricsViewModel
├── Timestamp: DateTimeOffset
├── Validation: ValidationSummaryViewModel
├── Consensus: ConsensusSummaryViewModel
├── Pools: PoolSummaryViewModel
└── Caches: CacheSummaryViewModel

ValidationSummaryViewModel
├── TotalValidated: long
├── TotalSuccessful: long
├── TotalFailed: long
├── SuccessRate: double
├── AverageValidationTimeMs: double
├── InProgress: int
└── ErrorsByCategory: Dictionary<string, long>

ConsensusSummaryViewModel
├── DocketsProposed: long
├── DocketsDistributed: long
├── RegisterSubmissions: long
├── FailedSubmissions: long
├── ConsensusFailures: long
├── SuccessfulRecoveries: long
├── DocketsAbandoned: long
└── PendingDockets: int

PoolSummaryViewModel
├── QueueSizes: Dictionary<string, int>
├── OldestTransaction: DateTimeOffset?
├── NewestTransaction: DateTimeOffset?
├── TotalEnqueued: long
├── TotalDequeued: long
└── TotalExpired: long

CacheSummaryViewModel
├── BlueprintCacheHits: long
├── BlueprintCacheMisses: long
├── HitRatio: double
├── LocalEntryCount: int
└── DistributedEntryCount: int
```

### System Register Models

```
SystemRegisterViewModel
├── RegisterId: string
├── DisplayName: string
├── IsInitialized: bool
├── BlueprintCount: int
├── CreatedAt: DateTimeOffset
└── Status: string

BlueprintSummaryViewModel
├── BlueprintId: string
├── Version: long
├── PublishedAt: DateTime
├── PublishedBy: string
├── IsActive: bool
└── Metadata: Dictionary<string, string>?

BlueprintDetailViewModel
├── BlueprintId: string
├── Version: long
├── Document: string (full JSON)
├── PublishedAt: DateTime
├── PublishedBy: string
└── IsActive: bool
```

### Threshold Signing Models

```
ThresholdConfigViewModel
├── RegisterId: string
├── GroupPublicKey: string
├── Threshold: uint (t)
├── TotalValidators: uint (n)
├── ValidatorIds: string[]
├── Status: string
└── CollectedShares: int? (during signing)

ThresholdSetupRequest
├── RegisterId: string (required)
├── Threshold: uint (required, 1 ≤ t ≤ n)
├── TotalValidators: uint (required, ≥ 1)
└── ValidatorIds: string[] (required, count == TotalValidators)
```

### Validator Config Model

```
ValidatorConfigViewModel
├── Fields: Dictionary<string, string> (key-value pairs)
└── RedactedKeys: string[] (sensitive fields shown as "***")
```

---

## CLI DTOs

CLI commands use the same DTOs returned by the backend (no separate view model layer). Key response types consumed:

| Backend DTO | Used By CLI Command |
|------------|-------------------|
| `RegisterPolicyResponse` | `register policy get` |
| `PolicyHistoryResponse` | `register policy history` |
| `PolicyUpdateResponse` | `register policy update` |
| System register anonymous object | `register system status` |
| `PaginatedBlueprintResponse` | `register system blueprints` |
| `AggregatedMetrics` | `validator metrics` |
| `ValidationMetricsResponse` | `validator metrics validation` |
| `ConsensusMetricsResponse` | `validator metrics consensus` |
| `PoolMetricsResponse` | `validator metrics pools` |
| `CacheMetricsResponse` | `validator metrics caches` |
| `ConfigurationResponse` | `validator metrics config` |
| Pending validators list | `validator consent pending` |
| Success response | `validator consent approve/reject` |
| Approved validators list | `validator consent refresh` |
| `ThresholdSetupResponse` | `validator threshold status/setup` |

---

## State Transitions

### Service Principal Lifecycle

```
[Created] → Active → Suspended → Active (reactivate)
                   → Revoked (permanent, terminal)
            Active → Revoked (permanent, terminal)
```

### Validator Registration Lifecycle

```
[Requested] → Pending → Approved
                      → Rejected (terminal)
```

### Register Policy Versioning

```
Version 1 (default at creation)
  → Propose Update → 202 Accepted (governance vote)
    → Version N+1 (if approved by governance)
```

---

## Enumerations

```
ExpirationPreset: ThirtyDays | NinetyDays | OneYear | NoExpiry

ServicePrincipalStatus: Active | Suspended | Revoked

RegistrationMode: Open | Consent

ValidatorRegistrationStatus: Pending | Approved | Rejected
```
