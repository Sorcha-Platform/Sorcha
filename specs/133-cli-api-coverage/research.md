# Phase 0 Research: CLI API Surface Catch-Up

All "unknowns" for this feature were resolved by reading the CLI's existing Refit clients and the platform's endpoint handlers. No external/library research was required — the stack (System.CommandLine 2.0.2, Refit 9.0.2, Spectre.Console) is current and unchanged.

## R-001: Client strategy — selective reuse, not full convergence

**Decision**: Keep the CLI's own thin Refit interfaces for the admin/operator surface; reuse `Sorcha.ServiceClients.Http` only where it already exposes the exact capability.

**Rationale**: `Sorcha.ServiceClients.Http` is purpose-built for service-to-service calls. Its `IWalletServiceClient` already has `ProvisionOrgMasterKeyAsync` / `DeriveOrgKeyAsync` / `RotateOrgKeyAsync` / `RevokeOrgKeyAsync` (and `DownloadFileAsync`) — the CLI must reuse these (satisfies CLAUDE.md Critical Pattern #2). But its `IValidatorServiceClient` is only `SubmitTransactionAsync` + `GetNextSequenceNumberAsync`, and there is **no register sync-state/relationship or validator-roster surface** anywhere in the shared library — those are operator/admin reads no service needs. Re-pointing the whole CLI at the s2s library is therefore impossible without first building that admin surface into the shared library, which is out of scope and would bloat a service-to-service package with operator concerns.

**Selective-reuse rule (to be documented in the sorcha-cli skill)**: Before adding a new CLI client method, check `Sorcha.ServiceClients.Http` for an existing method with the same capability. If present → reuse it (reference the package, inject the client). If absent → add a thin Refit method to the CLI's own `I*ServiceClient`, but do NOT add operator-only methods to the shared s2s library.

**Alternatives considered**: (a) Full convergence on `Sorcha.ServiceClients.Http` — rejected: requires building an admin surface into an s2s package. (b) Status quo (always bespoke CLI clients) — rejected: re-introduces the org-key duplication CLAUDE.md forbids.

## R-002: `transaction status` is a latent DTO bug, not a missing command

**Decision**: Fix the existing command rather than add a new one. Re-type the `/api/registers/{registerId}/transactions/{txId}/status` response to the lifecycle shape.

**Rationale**: There is exactly one registration of `/transactions/{txId}/status` (Feature 079's lifecycle endpoint, `Sorcha.Register.Service/Endpoints/VerificationEndpoints.cs`). It returns `TransactionStatusResponse { transactionId, status: Active|Revoked|Superseded, revocationTxId?, supersededByTxId?, revokedAt?, reason? }`. The CLI's `GetTransactionStatusAsync` still types the response as `SubmitTransactionResponse` (a submission-acknowledgement shape), so the existing `transaction status` command deserializes the wrong fields. Fixing the DTO is the correct change; no route is missing.

**Alternatives considered**: Adding a separate `transaction lifecycle` command — rejected: would leave the broken `status` command in place and confuse the surface.

## R-003: Trust scope correction (US9 / FR-023)

**Decision**: Re-scope the trust commands from the assumed `/api/trust/{address}` CRUD to the **actual** trust-hardening surface: `/api/v1/trust/tenants/{tenantId}/...`.

**Rationale**: Research found no `/api/trust/{address}`, `POST /api/trust`, or `DELETE /api/trust/{id}` endpoints. The real trust surface in `Sorcha.Tenant.Service/Endpoints/TrustEndpoints.cs` is org trust-anchor / certificate-chain administration:
- `POST /api/v1/trust/tenants/{tenantId}/provision` — provision the tenant trust anchor
- `GET  /api/v1/trust/tenants/{tenantId}/trust-anchor`
- `POST /api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}/enrol`
- `GET  /api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}/cert-chain`
- `POST /api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}/revoke`
- `GET  /api/v1/trust/tenants/{tenantId}/crl`

The CLI `trust` command becomes: `trust-anchor get/provision`, `org enrol/cert-chain/revoke`, `crl`. This is genuinely operator-relevant (PKI administration) and self-contained. **Spec FR-023 / User Story 9 updated to match.**

**Alternatives considered**: Dropping trust from scope — rejected: the real surface is valuable and has no CLI path. Leaving FR-023 as-written — rejected: it points at non-existent endpoints.

## R-004: Confirmed endpoint shapes (Phase 1)

From `Sorcha.Register.Service/Endpoints/VerificationEndpoints.cs`, `RelationshipEndpoints.cs`, `RecoveryHealthEndpoints.cs`, and `Sorcha.Validator.Service/Endpoints/ValidatorRegistrationEndpoints.cs`:

- **Inclusion proof** `GET …/inclusion-proof` → `MerkleInclusionProof { transactionHash, docketNumber:long, merkleRoot, proofPath: MerkleProofStep[], leafIndex:int, treeSize:int }`.
- **Verify proof** `POST …/inclusion-proofs/verify` body `VerifyMerkleInclusionProofRequest { transactionHash, merkleRoot, proofPath }` → `{ isValid:bool, computedRoot }`.
- **Revoke** `POST …/transactions/revoke` body `RevokeTransactionRequest { originalTxId, reason (enum), supersededByTxId?, metadata?, signerWalletAddress? }` → 202 `{ revocationTxId, originalTxId, status }`. `RevocationReason` enum: Superseded, Erroneous, Compromised, Expired, Withdrawn, Regulatory.
- **Lifecycle status** → `TransactionStatusResponse` (see R-002).
- **Local relationship** `GET …/local-relationship` → `RegisterLocalRelationship` (`Sorcha.Register.Models.LocalRelationship`).
- **Sync state** `GET …/sync-state` → `RegisterSyncStateView` (`Sorcha.Register.Models.SyncState`).
- **Sync health** `GET /health/sync` → `{ status, registers: RegisterSyncStatus[], checkedAt }`.
- **Validator register** `POST /api/validators/register` body `RegisterValidatorRequest { registerId, validatorId, publicKey, grpcEndpoint, metadata? }` → 201 `{ validatorId, registerId, transactionId, orderIndex, status, message }`.
- **Count** → `{ registerId, activeCount, minValidators, maxValidators, hasQuorum }`.
- **Audit** (query: validatorId?, limit, offset) → `{ registerId, entries:[{validatorId, previousStatus, newStatus, performedBy, reason, timestamp}], total }`.
- **Suspend** body `{ suspendedBy, reason }`; **Reactivate** body `{ reactivatedBy, notes? }`; **Revoke** body `{ revokedBy, reason }` — each returns a small status object.
- **Sequence** `GET …/sequence/{walletAddress}` → `{ registerId, walletAddress, lastSequenceNumber:long, nextSequenceNumber:long }`.

**Org key derivation** (reused from `Sorcha.ServiceClients.Http`, namespace `Sorcha.ServiceClients.Wallet`):
- `ProvisionOrgMasterKeyAsync(orgId, algorithm="ED25519", ct)` → `OrgMasterKeyProvisionResponse { organizationId, masterPublicKey, mnemonic, algorithm }`.
- `DeriveOrgKeyAsync(orgId, userId, departmentId:uint, keyUsage, ct)` → `DerivedKeyResponse { derivedKeyId:Guid, walletAddress, derivationPath, keyUsage, keyIndex:uint, status, custodyMode, createdAt }`.
- `RotateOrgKeyAsync(orgId, derivedKeyId, ct)` → `DerivedKeyResponse`.
- `RevokeOrgKeyAsync(orgId, derivedKeyId, ct)` → `RevokeKeyResponse { derivedKeyId, status, revokedAt, walletLocked, didRevocationPublished }`.

## R-005: Confirmed endpoint shapes (Phase 2)

- **Wallet diagnostics** (`Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs` + `DelegationEndpoints.cs`): `did-document`, `gap-status` → `GapStatusResponse`, `accounts`, `addresses` → `AddressListResponse`, `delegations` → `WalletAccessDto[]`. Exact field lists confirmed at task time by reading the handlers (routes verified present).
- **System register** (`SystemRegisterEndpoints.cs`): `initialize` (no body) → `{ message, status }`; `publish` body `PublishBlueprintRequest { blueprintId, blueprint:JsonElement, previousTransactionId?, metadata? }` → `PublishBlueprintResponse { transactionId, blueprintId, version:long, publishedAt }`; `classify-change` body `{ newBlueprint:JsonElement }` → `{ changeType, currentVersion?, proposedVersion, structuralHash*, structuralFieldsChanged }`; `versions` → `{ blueprintId, latestVersion, versions: BlueprintVersion[] }`.
- **Device admin** (`PlatformUserDeviceEndpoints.cs`): `GET /api/v1/me/devices` → `DeviceListResponse { devices: DeviceSummary[] }` (`DeviceSummary { deviceId:Guid, label, platform, status: Active|Revoked, enrolledAt, revokedAt?, lastSeenAt?, delegationExpiresAt? }`); `DELETE /api/v1/me/devices/{deviceId}` → 204.
- **Auth/token** (`AuthEndpoints.cs`): `introspect` body `TokenIntrospectionRequest { token, tokenTypeHint? }` → `TokenIntrospectionResponse { active, scope?, clientId?, sub?, exp?, iat?, iss?, aud?, tokenType?, jti?, orgId?, roles? }`; `switch-org` body `{ organizationId:Guid }` → `TokenResponse { access_token, refresh_token, token_type, expires_in, scope? }`; `me/organizations` → `OrgMembershipListResponse { items: OrgMembershipEntry[] }` (`{ organizationId, organizationName, subdomain, role, isCurrent }`).
- **Trust** — see R-003 for corrected routes/shapes (cert-chain, trust-anchor, CRL, enrol/revoke).

## R-006: Reuse note for `switch-org`

**Decision**: `auth switch-org` must persist the returned `TokenResponse` into the existing token cache (same path the login command uses) so subsequent commands pick up the new org context.

**Rationale**: The endpoint re-issues a JWT bound to the new active org. Without writing it back to the encrypted token cache, the switch would have no effect on later commands. Mirror `AuthLoginCommand`'s token-persistence path.

## Open items deferred to task execution (not blocking)

- Exact field lists of the Phase 2 wallet-diagnostic response types (`GapStatusResponse`, `AddressListResponse`, account/DID-document shapes) — confirmed by reading the handlers when those tasks run; routes are verified present.
- Whether validator roster mutations (`suspend`/`reactivate`/`revoke`) require an admin role vs. governance-quorum — the CLI surfaces whatever authorisation error the endpoint returns (edge-case handling in spec).
