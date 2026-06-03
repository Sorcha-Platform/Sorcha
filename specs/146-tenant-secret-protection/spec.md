# Feature Specification: Tenant Service At-Rest Secret Protection

**Feature Branch**: `146-tenant-secret-protection`
**Created**: 2026-06-03
**Status**: Draft
**Input**: Approved design — `docs/superpowers/specs/2026-06-03-tenant-secret-protection-design.md` (authoritative technical source). Origin: security review finding C1 (CRITICAL) + two siblings (`docs/reviews/2026-06-02-architecture-review.md`).

## Overview

Three secrets/keys managed by the Tenant Service are stored or generated in ways that defeat their purpose:

1. **Two-factor (TOTP) secrets** are stored in a reversible, plaintext-equivalent form — anyone with read access to the Tenant database can recover every user's 2FA seed and generate valid codes.
2. **Identity-provider (OIDC) client secrets** are stored as a one-way hash — both insecure *and* functionally broken, because the real secret can never be recovered to authenticate to the external identity provider.
3. **The signing key for the short-lived 2FA intermediate token** is regenerated on every process start, so those tokens fail to validate across multiple service instances or after a restart.

This feature replaces all three with a single, consistent secret-protection mechanism so that stored secrets are unreadable without the application's protection key, recoverable secrets remain usable, and the 2FA flow is stable across instances.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Citizen 2FA secrets are unreadable at rest (Priority: P1)

A citizen enrols two-factor authentication and later signs in with a 2FA code. The TOTP secret backing that flow is stored so that a person or process with database access alone cannot recover it, while the citizen's day-to-day enrolment and verification continue to work unchanged.

**Why this priority**: This is the CRITICAL finding. The current storage is plaintext-equivalent; a single database read compromises every user's second factor, nullifying 2FA entirely.

**Independent Test**: Enrol a TOTP configuration, then inspect the stored value directly in the database — confirm it cannot be decoded back to the secret without the application's protection key. Separately, verify a valid code still authenticates and an invalid one is rejected.

**Acceptance Scenarios**:

1. **Given** a citizen completes 2FA enrolment, **When** the stored TOTP record is read directly from the database, **Then** the secret cannot be recovered without the application protection key.
2. **Given** an enrolled citizen, **When** they submit a valid current TOTP code, **Then** verification succeeds; **When** they submit an invalid code, **Then** verification fails.
3. **Given** a stored secret has been tampered with in the database, **When** verification is attempted, **Then** the attempt fails safely (treated as an invalid code, not a server error).

---

### User Story 2 - OIDC client secrets are both protected and usable (Priority: P2)

An organisation administrator configures an external identity provider by entering its client secret. The secret is stored protected, and when the platform later performs the OIDC token exchange it recovers and uses the *real* client secret to authenticate to the provider.

**Why this priority**: The current hash-based storage is irreversible, so the token-exchange path is functionally broken (it sends a hash, not the secret) in addition to the security problem. Fixing storage fixes both.

**Independent Test**: Save an IdP configuration with a known client secret, then drive the token-exchange path and confirm the value presented to the provider equals the original client secret; confirm the stored column is not the plaintext secret.

**Acceptance Scenarios**:

1. **Given** an admin saves an IdP configuration with a client secret, **When** the stored value is read directly from the database, **Then** it is not the plaintext secret and cannot be recovered without the protection key.
2. **Given** a saved IdP configuration, **When** the platform performs the OIDC token exchange, **Then** it authenticates using the original client secret value (not a hash or placeholder).

---

### User Story 3 - 2FA works across instances and restarts (Priority: P3)

A user begins the 2FA step on one running instance of the platform and completes it on another (or after a restart). The intermediate 2FA token issued by one instance is accepted by any other instance.

**Why this priority**: Multi-instance deployments and restarts currently break the 2FA intermediate token because its signing key is per-process and random. Lower priority than the at-rest exposure, but a real correctness defect for any non-single-instance deployment.

**Independent Test**: Issue a 2FA intermediate token from one instance and validate it from a second instance configured identically; confirm acceptance. Repeat across a simulated restart.

**Acceptance Scenarios**:

1. **Given** two identically configured instances, **When** a 2FA intermediate token is issued by instance A within its validity window, **Then** instance B accepts it.
2. **Given** a single instance, **When** it restarts within a token's validity window, **Then** a token issued before the restart is still accepted afterward.

---

### Edge Cases

- **No protection key available in a hardened environment**: in Production or Staging, if no protection key can be resolved, the service MUST refuse to start rather than fall back to an unprotected or default-key mode.
- **Misconfigured override key**: an explicitly provided protection key that is the wrong size is rejected at startup (fail-closed), not silently truncated or padded.
- **Tampered or corrupt stored secret**: decryption failure is surfaced as an authentication/configuration failure, never an unhandled server error.
- **Fresh deployment with cleared data**: because this ships as a pre-release clean break with the database cleared, there are no legacy-format records to read; the system has no obligation to decode the old formats.
- **Key rotation**: each stored secret records which key protected it, so a future key change can be reconciled without ambiguity.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Stored TOTP secrets MUST NOT be recoverable by a party with Tenant database read access alone; recovery MUST require the application's protection key.
- **FR-002**: Stored OIDC client secrets MUST NOT be recoverable from the database alone, AND MUST remain recoverable by the platform at runtime so the OIDC token exchange authenticates with the original secret.
- **FR-003**: TOTP enrolment and verification MUST continue to function for end users with no change to their experience.
- **FR-004**: The 2FA intermediate token MUST validate across all instances of a deployment and across restarts (its signing key MUST be stable and shared, not per-process).
- **FR-005**: All three legacy mechanisms (reversible-Base64 TOTP storage, one-way-hash OIDC client-secret storage, per-process random login-token key) MUST be removed — no reversible-without-key or weak storage of these secrets may remain.
- **FR-006**: In Production and Staging, the service MUST fail to start if no protection key can be resolved (fail-closed; no default or unprotected fallback).
- **FR-007**: The default configuration MUST require no new mandatory configuration to operate — the protection key is derived from existing managed key material by default.
- **FR-008**: Operators MUST be able to optionally supply a dedicated protection key (for independent rotation) that takes precedence over the default when present.
- **FR-009**: Each protected secret MUST record an identifier for the key that protected it, so key rotation can be reconciled.
- **FR-010**: A decryption or integrity failure on a stored secret MUST be handled safely (surfaced as an invalid-code or configuration error), never as an unhandled server error.
- **FR-011**: This change ships as a pre-release clean break: no data migration and no decoding of the prior formats; the database is cleared on rollout, and schema/column changes are folded into the existing initial database schema rather than introducing a new incremental migration.
- **FR-012**: The protection mechanism MUST be structured so a future hardware/cloud key-management implementation and convergence with the Wallet service's equivalent mechanism can be added without changing the consuming call sites; a note recording this intended convergence MUST be left in the code.

### Key Entities

- **Protected secret**: a stored secret (a TOTP secret, or an OIDC client secret) held as protected data plus the identifier of the key that protected it. TOTP secrets are recoverable for verification; OIDC client secrets are recoverable for the token exchange.
- **Protection key**: the application-held key that protects secrets at rest. Derived from existing managed key material by default; optionally an operator-supplied dedicated key. Never stored in the database.
- **2FA intermediate-token signing key**: the stable, deployment-wide key used to sign/verify the short-lived 2FA step token. Derived from the same managed key material; distinct in purpose from the at-rest protection key.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Given only a dump of the Tenant database (without application keys), **zero** TOTP secrets and **zero** OIDC client secrets can be recovered.
- **SC-002**: 100% of OIDC token exchanges that rely on a stored client secret present the *original* secret to the identity provider (the previously-broken path now succeeds).
- **SC-003**: A 2FA intermediate token issued by one instance is accepted by another identically-configured instance (and after a restart) 100% of the time within its validity window.
- **SC-004**: In a Production/Staging configuration with no resolvable protection key, the service refuses to start — there is no runtime state in which protected secrets are stored under an unprotected or default scheme.
- **SC-005**: Rolling out the feature to an environment that already configures the platform's existing signing key requires **no additional mandatory configuration**.
- **SC-006**: After rollout, no code path remains that stores any of the three secrets/keys in a reversible-without-key, hashed-but-needed-reversible, or per-process-random form (verified by an automated repository check).

## Assumptions & Dependencies

- **Authoritative design**: `docs/superpowers/specs/2026-06-03-tenant-secret-protection-design.md` governs the technical approach (mechanism, key derivation, algorithm, file-level changes). This spec defines the required outcomes; the plan derives the implementation from that design.
- **Pre-release clean break**: the deployment clears the Tenant database on rollout. There is no production data to migrate and no requirement to read legacy-format secrets.
- **Existing key material**: the platform already provisions and (in hardened environments) requires a JWT signing key; the default protection key and the 2FA-token signing key derive from it. No new mandatory secret is introduced.
- **Out of scope**: a hardware/cloud key-management (HSM/KMS) implementation; converging the Tenant and Wallet protection mechanisms (only the seam and a convergence note are in scope now); the separate OIDC token *issuer-validation* concern; backup-code hashing (unchanged).
