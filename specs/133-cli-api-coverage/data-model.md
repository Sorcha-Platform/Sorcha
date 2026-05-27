# Data Model: CLI API Surface Catch-Up

The CLI is a stateless client. "Data model" here = the request/response model types each new command serialises, and whether each is **new in the CLI**, **reused from `Sorcha.ServiceClients.Http`**, or **reused from an existing Sorcha.* model package**. No database entities, no migrations.

Legend: 🆕 new CLI model · ♻️ reuse shared library · 📦 reuse existing Sorcha.* models package

## Phase 1

### Transaction trust-hardening (CLI `IRegisterServiceClient` + Models)

| Type | Direction | Source | Fields |
|------|-----------|--------|--------|
| `MerkleInclusionProof` | response | 📦 `Sorcha.Register.Models` (verify present) / else 🆕 mirror | transactionHash, docketNumber:long, merkleRoot, proofPath: MerkleProofStep[], leafIndex:int, treeSize:int |
| `MerkleProofStep` | nested | 📦 / 🆕 | (hash + position — confirm at task time) |
| `VerifyMerkleInclusionProofRequest` | request | 🆕 | transactionHash, merkleRoot, proofPath |
| `VerifyProofResult` | response | 🆕 | isValid:bool, computedRoot |
| `RevokeTransactionRequest` | request | 🆕 | originalTxId, reason: RevocationReason, supersededByTxId?, metadata?, signerWalletAddress? |
| `RevocationReason` | enum | 🆕 | Superseded, Erroneous, Compromised, Expired, Withdrawn, Regulatory |
| `RevokeTransactionResponse` | response | 🆕 | revocationTxId, originalTxId, status |
| `TransactionStatusResponse` | response | 🆕 (**replaces** stale `SubmitTransactionResponse` typing) | transactionId, status: TransactionLifecycleStatus, revocationTxId?, supersededByTxId?, revokedAt?, reason? |
| `TransactionLifecycleStatus` | enum | 🆕 | Active, Revoked, Superseded |

### Register sync diagnostics (CLI `IRegisterServiceClient` + Models)

| Type | Direction | Source | Fields |
|------|-----------|--------|--------|
| `RegisterLocalRelationship` | response | 📦 `Sorcha.Register.Models.LocalRelationship` if referenceable, else 🆕 mirror | derived role set (owner/validator/subscriber flags) |
| `RegisterSyncStateView` | response | 📦 `Sorcha.Register.Models.SyncState` / 🆕 mirror | state: Indeterminate/Syncing/CaughtUp/Error + heights |
| `SyncHealthResponse` | response | 🆕 | status, registers: RegisterSyncStatus[], checkedAt |
| `RegisterSyncStatus` | nested | 🆕 | per-register status |

### Validator roster governance (CLI `IValidatorServiceClient` + Models)

| Type | Direction | Source | Fields |
|------|-----------|--------|--------|
| `RegisterValidatorRequest` | request | 🆕 | registerId, validatorId, publicKey, grpcEndpoint, metadata? |
| `RegisterValidatorResponse` | response | 🆕 | validatorId, registerId, transactionId, orderIndex:int, status, message |
| `ValidatorCountResponse` | response | 🆕 | registerId, activeCount:int, minValidators:int, maxValidators:int, hasQuorum:bool |
| `ValidatorAuditResponse` | response | 🆕 | registerId, entries: ValidatorAuditEntry[], total:int |
| `ValidatorAuditEntry` | nested | 🆕 | validatorId, previousStatus, newStatus, performedBy, reason, timestamp |
| `SuspendValidatorRequest` | request | 🆕 | suspendedBy, reason |
| `ReactivateValidatorRequest` | request | 🆕 | reactivatedBy, notes? |
| `RevokeValidatorRequest` | request | 🆕 | revokedBy, reason |
| `ValidatorSequenceResponse` | response | 🆕 | registerId, walletAddress, lastSequenceNumber:long, nextSequenceNumber:long |

### Org key derivation — REUSE, no new CLI models

| Type | Source |
|------|--------|
| `IWalletServiceClient` (`ProvisionOrgMasterKeyAsync`/`DeriveOrgKeyAsync`/`RotateOrgKeyAsync`/`RevokeOrgKeyAsync`) | ♻️ `Sorcha.ServiceClients.Http`, ns `Sorcha.ServiceClients.Wallet` |
| `OrgMasterKeyProvisionResponse` { organizationId, masterPublicKey, mnemonic, algorithm } | ♻️ |
| `DerivedKeyResponse` { derivedKeyId:Guid, walletAddress, derivationPath, keyUsage, keyIndex:uint, status, custodyMode, createdAt } | ♻️ |
| `RevokeKeyResponse` { derivedKeyId, status, revokedAt, walletLocked, didRevocationPublished } | ♻️ |

The org-key command injects the shared `IWalletServiceClient` via the CLI's DI; **no Refit method or DTO is added to the CLI** (selective-reuse rule, research R-001).

## Phase 2

### Wallet diagnostics (CLI `IWalletServiceClient` (CLI's own) + Models)

| Type | Direction | Source | Notes |
|------|-----------|--------|-------|
| DID document | response | 🆕 (or string passthrough) | `GET …/did-document` |
| `GapStatusResponse` | response | 🆕 | fields confirmed at task time |
| account list | response | 🆕 | `GET …/accounts` |
| `AddressListResponse` | response | 🆕 | `GET …/addresses` |
| `WalletAccessDto[]` | response | 🆕 mirror | `GET …/delegations` |

### System register governance (CLI `IRegisterServiceClient` + Models)

| Type | Direction | Source | Fields |
|------|-----------|--------|--------|
| `PublishBlueprintRequest` | request | 🆕 (CLI already has a publish DTO for blueprint service — keep distinct) | blueprintId, blueprint:JsonElement, previousTransactionId?, metadata? |
| `PublishBlueprintResponse` | response | 🆕 | transactionId, blueprintId, version:long, publishedAt |
| `ClassifyChangeRequest` | request | 🆕 | newBlueprint:JsonElement |
| `ClassifyChangeResponse` | response | 🆕 | changeType, currentVersion?, proposedVersion, structuralHash*, structuralFieldsChanged:bool |
| `BlueprintVersionsResponse` | response | 🆕 | blueprintId, latestVersion, versions: BlueprintVersion[] |
| `BlueprintVersion` | nested | 🆕 | major:int, minor:int, changeType, structuralHash, publishedAt, publishedBy, transactionId |

### Citizen device admin (CLI `ITenantServiceClient` + Models)

| Type | Direction | Source | Fields |
|------|-----------|--------|--------|
| `DeviceListResponse` | response | 🆕 | devices: DeviceSummary[] |
| `DeviceSummary` | nested | 🆕 | deviceId:Guid, label, platform, status: DeviceStatus, enrolledAt, revokedAt?, lastSeenAt?, delegationExpiresAt? |
| `DeviceStatus` | enum | 🆕 | Active, Revoked |
| (revoke) | — | — | `DELETE …/{deviceId}` → 204, no body |

### Auth/token (CLI `ITenantServiceClient`/auth service + Models)

| Type | Direction | Source | Fields |
|------|-----------|--------|--------|
| `TokenIntrospectionRequest` | request | 🆕 (or ♻️ `ITokenIntrospectionClient` in shared lib — check) | token, tokenTypeHint? |
| `TokenIntrospectionResponse` | response | 🆕 / ♻️ | active, scope?, clientId?, sub?, exp?, iat?, iss?, aud?, tokenType?, jti?, orgId?, roles? |
| `SwitchOrgRequest` | request | 🆕 | organizationId:Guid |
| `TokenResponse` | response | 📦 reuse CLI's existing `TokenResponse` | access_token, refresh_token, token_type, expires_in, scope? |
| `OrgMembershipListResponse` | response | 🆕 | items: OrgMembershipEntry[] |
| `OrgMembershipEntry` | nested | 🆕 | organizationId:Guid, organizationName, subdomain, role, isCurrent:bool |

> `auth switch-org` reuses the CLI's existing token-cache write path (research R-006).

### Trust-anchor administration (CLI `ITenantServiceClient` + Models) — corrected scope (R-003)

| Command | Endpoint | Notes |
|---------|----------|-------|
| `trust anchor provision` | `POST /api/v1/trust/tenants/{tenantId}/provision` | provision tenant trust anchor |
| `trust anchor get` | `GET /api/v1/trust/tenants/{tenantId}/trust-anchor` | |
| `trust org enrol` | `POST /api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}/enrol` | |
| `trust org cert-chain` | `GET /api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}/cert-chain` | |
| `trust org revoke` | `POST /api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}/revoke` | |
| `trust crl` | `GET /api/v1/trust/tenants/{tenantId}/crl` | certificate revocation list |

DTO field lists confirmed at task time from `TrustEndpoints.cs`; a shared `IOrgCertChainProvider` exists in `Sorcha.ServiceClients.Http/Trust/` — check for reuse before adding CLI Refit methods (selective-reuse rule).

## Cross-cutting model conventions

- All new DTOs are immutable `record` types with `[JsonPropertyName]` where the wire name differs from C# casing (e.g. `access_token`).
- Enums serialise to their string form to match the platform (`JsonStringEnumConverter`, consistent with existing CLI models).
- Reused shared-library types are NOT re-declared in the CLI (FR-028).
