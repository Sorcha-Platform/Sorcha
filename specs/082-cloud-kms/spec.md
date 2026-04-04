# Feature Specification: Cloud KMS Key Management

**Feature Branch**: `082-cloud-kms`
**Created**: 2026-04-04
**Status**: Draft
**Input**: Multi-cloud KMS integration for wallet key protection and KMS-resident signing

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Envelope Encryption with Cloud KMS (Priority: P1)

A platform operator deploys Sorcha to a cloud environment and configures a cloud KMS provider (Azure Key Vault, AWS KMS, or GCP Cloud KMS). All wallet private keys are encrypted at rest using data encryption keys (DEKs) that are themselves protected by the cloud KMS. The operator gains hardware-backed key protection, an audit trail of all key access, and compliance with FIPS 140-2 requirements — without changing any wallet workflows.

**Why this priority**: This is the core security improvement. Every wallet benefits from cloud-managed DEK protection. Without this, private keys rely on OS-level protection (DPAPI, Linux Secret Service) which lacks audit trails and hardware backing.

**Independent Test**: Deploy with Azure Key Vault configured. Create a wallet, sign a transaction, verify the signing succeeds. Inspect Key Vault audit logs to confirm wrap/unwrap operations occurred.

**Acceptance Scenarios**:

1. **Given** the platform is configured with a cloud KMS provider, **When** a new wallet is created, **Then** the wallet's DEK is wrapped by the cloud KMS and the encrypted private key is stored in the database.
2. **Given** a wallet exists with a cloud-KMS-protected DEK, **When** a transaction is signed, **Then** the DEK is unwrapped (or served from cache), the private key is decrypted locally, and signing completes successfully.
3. **Given** the platform is configured with the Local provider (Docker/development), **When** a wallet is created, **Then** the existing platform-specific key protection is used (DPAPI, Secret Service, etc.) with no cloud KMS dependency.
4. **Given** the cloud KMS is temporarily unreachable, **When** a signing operation is attempted and the DEK is still in cache, **Then** the operation succeeds using the cached DEK and a warning is logged.
5. **Given** the cloud KMS is unreachable and the DEK cache has expired beyond the grace period, **When** a signing operation is attempted, **Then** the operation fails with a clear error indicating the KMS is unavailable.

---

### User Story 2 — KMS-Resident Signing for High-Security Wallets (Priority: P2)

A platform operator designates specific wallets (such as system attestation, docket signing, or blueprint publishing wallets) as "KMS-resident". For these wallets, the private key is created inside the cloud KMS and never leaves it. All signing operations are performed by the KMS directly. This provides the highest level of key protection for wallets that represent the platform's identity and trust anchors.

**Why this priority**: This addresses the highest-security use case where key material must never exist in application memory. Important for compliance but not required for basic operation — envelope encryption (P1) is sufficient for most wallets.

**Independent Test**: Create a wallet with KMS-resident signing mode. Sign a transaction. Verify the signature is valid and that no private key material was stored in the database.

**Acceptance Scenarios**:

1. **Given** KMS-resident signing is configured, **When** a system wallet is created at a designated derivation path, **Then** the signing key is created inside the cloud KMS, the public key is retrieved, and no private key is stored locally.
2. **Given** a KMS-resident wallet exists, **When** a transaction is signed, **Then** the signing operation is performed entirely within the cloud KMS and a valid signature is returned.
3. **Given** a KMS-resident wallet exists, **When** the cloud KMS is unreachable, **Then** signing fails immediately with a clear error. There is no local fallback.
4. **Given** the Local provider is active (Docker/development), **When** a KMS-resident wallet creation is attempted, **Then** the system returns an error indicating KMS-resident signing requires a cloud provider.

---

### User Story 3 — Signing Mode Policy and Override (Priority: P3)

A platform operator configures which wallets default to KMS-resident signing via a policy based on derivation paths. System wallets (attestation, control record, docket, blueprint) default to KMS-resident in production. All other wallets default to local envelope encryption. API callers can override the default when creating wallets if the configuration allows it.

**Why this priority**: Provides operational flexibility without requiring manual configuration per wallet. Depends on both P1 and P2 being implemented.

**Independent Test**: Configure KMS-resident paths in settings. Create wallets at system paths and verify they are KMS-resident. Create wallets at other paths and verify they use local signing. Override via API and verify the override takes effect.

**Acceptance Scenarios**:

1. **Given** system derivation paths are configured as KMS-resident, **When** a wallet is created at path `m/44'/0'/0'/0/100`, **Then** the wallet is created with KMS-resident signing mode automatically.
2. **Given** default signing mode is Local, **When** a wallet is created at a non-system path, **Then** the wallet uses local envelope encryption.
3. **Given** signing mode override is enabled, **When** a wallet creation request specifies `signingMode: KmsResident`, **Then** the wallet is created with KMS-resident mode regardless of path.
4. **Given** signing mode override is disabled, **When** a wallet creation request specifies a signing mode, **Then** the override is ignored and the policy default applies.

---

### Edge Cases

- What happens when a KMS-resident wallet is requested with an algorithm other than P-256? The system rejects the request with a clear error explaining that only P-256 is supported for KMS-resident signing.
- What happens when the cloud KMS provider is changed after wallets have been created? Existing wallets remain associated with the original provider. A migration tool would be needed (deferred scope).
- What happens during DEK cache grace period expiry under sustained KMS outage? After the grace period (default 15 minutes), all signing operations for wallets whose DEKs have expired fail closed. A warning is logged when entering and exiting the grace period.
- What happens if the KMS key referenced by a wallet is deleted in the cloud console? Signing and decryption operations fail with an error indicating the key was not found. The wallet becomes unusable until the key is recovered (if the cloud provider supports it).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support pluggable key management providers selectable via configuration, with Local (existing platform-specific) and Azure Key Vault as initial options.
- **FR-002**: System MUST protect wallet data encryption keys (DEKs) using the configured key management provider's wrap/unwrap operations.
- **FR-003**: System MUST cache unwrapped DEKs in memory with a configurable TTL (default 30 minutes) to minimise cloud KMS round-trips.
- **FR-004**: System MUST extend DEK cache TTL by a configurable grace period (default 15 minutes) when the cloud KMS is unreachable, logging a warning.
- **FR-005**: System MUST fail closed when both the DEK cache and grace period are exhausted and the KMS is unreachable.
- **FR-006**: System MUST support KMS-resident signing where the private key is created and used entirely within the cloud KMS, with P-256 (ECDSA) as the initial supported algorithm.
- **FR-007**: System MUST record a signing mode (Local or KMS-resident) and optional KMS key reference on each wallet.
- **FR-008**: System MUST resolve signing mode at wallet creation using a configurable policy (derivation path matching) with optional API override.
- **FR-009**: System MUST reject KMS-resident wallet creation when the active provider does not support signing operations, returning a clear error.
- **FR-010**: System MUST log all key management operations (wrap, unwrap, sign, key creation) for audit trail purposes.
- **FR-011**: System MUST continue to support the existing Local provider for Docker and development environments with no cloud KMS dependency.
- **FR-012**: System MUST ensure existing wallets continue to function without migration when upgrading to the new key management architecture.

### Key Entities

- **Wallet**: Extended with signing mode (Local or KMS-resident) and optional KMS key reference. Existing fields (encrypted private key, encryption key ID) remain for Local wallets.
- **Key Protection Provider**: Responsible for wrapping and unwrapping DEKs. One active provider per deployment. Implementations: Local (existing), Azure Key Vault (new).
- **Signing Provider**: Responsible for creating signing keys and performing sign/verify operations within a cloud KMS. Only available with cloud providers. P-256 only initially.
- **Signing Mode Policy**: Configuration-driven rules that determine the default signing mode for new wallets based on derivation path, with override capability.

### Assumptions

- Only one cloud KMS provider is active per deployment. Multi-provider configurations are out of scope.
- AWS KMS and GCP Cloud KMS providers are deferred to future work. The interface design accommodates them but implementation is Azure-only initially.
- Multi-region KMS failover is deferred. Single-region deployment is the initial target.
- Batch re-keying of existing wallets from Local to cloud KMS is deferred.
- The existing AES-256-GCM encryption of private keys remains unchanged. Only the DEK protection layer changes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Wallet signing operations complete within 1 second for local-mode wallets (cache hit) and within 2 seconds for KMS-resident wallets, measured under normal cloud KMS availability.
- **SC-002**: Platform continues signing operations for at least 15 minutes (grace period) after a cloud KMS outage begins, for wallets using envelope encryption with cached DEKs.
- **SC-003**: All key management operations (wrap, unwrap, sign, key creation) produce audit log entries visible to platform operators.
- **SC-004**: Existing wallets created before this feature function without any data migration or manual intervention after upgrade.
- **SC-005**: KMS-resident wallets have no private key material stored in the platform database or file system — verified by database inspection.
- **SC-006**: Platform can be deployed with only the Local provider configured (no cloud credentials) for development and testing, with all wallet operations functioning as before.
- **SC-007**: Cloud KMS integration adds no more than $50/month in KMS costs for a deployment with up to 1000 wallets and 100,000 signing operations per month (Azure Key Vault pricing).
