# Feature Specification: Authorization-gap closure

**Feature Branch**: `147-authorization-gap-closure`
**Created**: 2026-06-03
**Status**: Draft
**Input**: Sub-project 2 of the security-hardening initiative. Authoritative source: `docs/superpowers/specs/2026-06-03-authorization-gap-closure-design.md` and `docs/reviews/2026-06-02-architecture-review.md` §2 (H1, H2, LOW), §7, §8.

## Overview

Four sensitive operations currently accept callers they should refuse, because the authorization decision is not enforced at the operation itself — it relies on a comment, on the perimeter (which only checks "is authenticated"), or on a shared rule that a later override silently weakened. This feature makes each operation enforce the correct trust tier and/or role in its own right, and — where a rule could be omitted per-operation — moves the rule into the shared definition so it cannot be forgotten.

The trust tiers referenced throughout are the platform's existing identity tiers: **consumer** (citizen / wallet holder), **platform** (org admin / designer / operator), and **service** (service-to-service / infrastructure). "Administrator" / "SystemAdmin" are roles carried by platform-tier human tokens.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Validator signing keys cannot be seated by unauthorized callers (Priority: P1)

The system-wallet create and recover operations exist so the platform's own infrastructure (and a one-time genesis ceremony operator) can seat the validator key that signs the distributed ledger's dockets. The recover operation imports a supplied seed phrase. Today these operations are reachable by any authenticated token through the perimeter, and by *anyone* on the internal network. An attacker who reaches them can install an attacker-controlled validator signing key and forge ledger entries.

**Why this priority**: A seated attacker key compromises the integrity guarantee at the heart of the platform (the ledger). It is the single highest-impact gap in the set.

**Independent Test**: Attempt create and recover with (a) no credentials, (b) a consumer-tier citizen token, (c) a service-tier token, (d) a platform admin token. Confirm only the legitimate callers succeed and everyone else is refused — without changing any legitimate caller's existing flow (infrastructure for create, admin genesis CLI for recover).

**Acceptance Scenarios**:

1. **Given** no credentials, **When** create or recover is called, **Then** the request is refused as unauthenticated.
2. **Given** a consumer-tier citizen token, **When** create or recover is called, **Then** the request is refused as unauthorized.
3. **Given** a service-tier token, **When** create is called, **Then** it succeeds (the infrastructure caller is unchanged).
4. **Given** a platform-tier token holding an administrator role, **When** recover is called, **Then** it succeeds (the genesis-ceremony operator is unchanged).
5. **Given** a service-tier token, **When** recover is called, **Then** it succeeds (future automation is supported).
6. **Given** a system wallet already exists for the validator, **When** recover is called with a different seed, **Then** it is refused with a conflict (the existing protective guard is preserved).

---

### User Story 2 - Blueprint and schema authoring is closed to non-platform callers (Priority: P1)

Blueprint, schema, credential-definition, and status-list authoring operations are an organization-administration activity. Today the gate they share admits any token carrying an organization identifier — and citizen/consumer tokens carry one — so a citizen can reach authoring. Authoring must require a platform-tier caller (or a legitimate service-tier caller); it must exclude consumer-tier callers.

**Why this priority**: Authoring surfaces let a caller change what workflows validate and what credentials are issued. Consumer access to them is a privilege-escalation gap across multiple endpoints at once.

**Independent Test**: Call each authoring operation with a consumer-tier token (expect refusal), a platform-tier org member (expect success), and a service-tier token (expect success). Confirm the fix is applied centrally, so every current and future authoring operation sharing the gate is covered without per-operation edits.

**Acceptance Scenarios**:

1. **Given** a consumer-tier token that carries an organization identifier, **When** any blueprint/schema/credential/status-list authoring operation is called, **Then** it is refused as unauthorized.
2. **Given** a platform-tier token carrying an organization identifier, **When** the same operation is called, **Then** authorization succeeds.
3. **Given** a service-tier token, **When** the same operation is called, **Then** authorization succeeds.
4. **Given** the authoring operations already composing a platform-tier requirement, **When** the central gate is changed, **Then** their behavior is unchanged (no regression, no new access).

---

### User Story 3 - A citizen's pending-application notice is reachable only by that citizen (Priority: P3)

The pending-application notice is a citizen-facing wallet surface. Today its gate accepts any authenticated token, so a platform-tier token could read or set a citizen's notice. It must require a consumer-tier caller, matching every sibling citizen surface.

**Why this priority**: Lower blast radius than US1/US2 (a notice label, scoped per-user), but it is a clear consumer⊥platform isolation gap and a one-line correction in the same theme.

**Independent Test**: Call the pending-application read/set/clear operations with a platform-tier token (expect refusal) and a consumer-tier token (expect success).

**Acceptance Scenarios**:

1. **Given** a platform-tier token, **When** the pending-application notice is read or set, **Then** the request is refused as unauthorized.
2. **Given** a consumer-tier token, **When** the citizen reads or sets their own notice, **Then** it succeeds and remains scoped to that citizen.

---

### User Story 4 - Platform-administration operations require system-admin-org membership (Priority: P3)

The platform-administration operations (managing all organizations, platform settings) are intended for members of the system administration organization holding the SystemAdmin role. Today, in the tenant service, the shared system-admin rule is re-declared as role-only, dropping the organization constraint — so a SystemAdmin in *any* organization clears these operations. The organization constraint must be restored.

**Why this priority**: Lower likelihood (requires an existing SystemAdmin role in another org) but a real privilege-scope gap on platform-wide operations.

**Independent Test**: Evaluate the system-admin gate for a SystemAdmin in a non-system organization (expect refusal) and a SystemAdmin in the system administration organization (expect success).

**Acceptance Scenarios**:

1. **Given** a token with the SystemAdmin role but whose organization is not the system administration organization, **When** a platform-administration operation is called, **Then** it is refused as unauthorized.
2. **Given** a token with the SystemAdmin role in the system administration organization, **When** the same operation is called, **Then** it succeeds.

---

### Edge Cases

- A platform-tier token holding an org id but **no** administrator role calling recover → refused (recover's human branch requires the administrator role, not merely platform tier).
- A token presenting an administrator role but a non-platform (e.g. consumer) audience calling recover → refused (both role and platform audience are required on the human branch).
- A token from a *different installation* (different audience namespace) → already refused at the perimeter; the per-operation gates do not weaken that.
- The internal-network direct-reach path (bypassing the perimeter) → closed for all four, because authorization is now enforced at the operation rather than at the perimeter.
- The genesis ceremony run before any administrator exists → not a supported path today; the recover caller already authenticates as an administrator (the platform bootstrap creates that administrator first). Documented as an assumption.

## Requirements *(mandatory)*

### Functional Requirements

**System-wallet operations (US1)**

- **FR-001**: The system-wallet create operation MUST refuse any caller that is not a service-tier caller, including unauthenticated callers and all human-tier (consumer or platform) tokens.
- **FR-002**: The system-wallet recover operation MUST refuse any caller that is neither (a) a service-tier caller nor (b) a platform-tier caller holding an administrator role; unauthenticated and consumer-tier callers MUST be refused.
- **FR-003**: The system-wallet recover operation MUST continue to refuse, with a conflict result, an attempt to recover when an active system wallet already exists for that validator (the existing protective guard is retained, not removed).
- **FR-004**: The legitimate existing callers MUST keep working unchanged: infrastructure creates via a service-tier token; the genesis-ceremony administrator recovers via a platform-tier administrator token.

**Blueprint/schema authoring (US2)**

- **FR-005**: The shared blueprint-management gate MUST admit a caller only if the caller is a service-tier caller, OR is a platform-tier caller carrying an organization identifier. Consumer-tier callers MUST be refused even when they carry an organization identifier.
- **FR-006**: The correction MUST be applied in the shared gate definition so that every operation using it — current and future — is covered without per-operation changes.
- **FR-007**: Authoring operations that already additionally require a platform-tier audience MUST retain their stricter behavior (no new access granted to them by this change).

**Pending-application notice (US3)**

- **FR-008**: The pending-application notice operations MUST require a consumer-tier caller; platform-tier and other non-consumer callers MUST be refused. Per-citizen scoping of the notice is unchanged.

**Platform-administration (US4)**

- **FR-009**: The platform-administration system-admin gate MUST require both the SystemAdmin role and membership of the system administration organization. The tenant service MUST NOT weaken this to role-only.
- **FR-010**: Before removing the weakening, every use of the system-admin gate MUST be confirmed to be a platform-administration operation intended to be system-admin-org-scoped; any use that legitimately needs different semantics MUST be given its own distinct rule rather than relying on the weakened shared one.

**Cross-cutting**

- **FR-011**: No operation in scope may depend on the perimeter for its tier/role decision; each MUST enforce its own authorization so a direct internal-network call is refused identically.
- **FR-012**: Each gate MUST derive the expected trust-tier audience from the platform's single source of truth at evaluation time (so per-installation audience namespaces are honored), rather than hard-coding an audience string.

### Key Entities *(include if feature involves data)*

- **Trust tier**: The tier a token belongs to (consumer / platform / service / enrol-session), carried in the token's audience. The unit of cross-tier isolation.
- **Role**: An authorization role carried by a platform-tier human token (e.g. Administrator, SystemAdmin).
- **System administration organization**: The well-known organization whose membership (plus SystemAdmin role) authorizes platform-wide administration.
- **System wallet**: The validator's docket-signing wallet; created by infrastructure or recovered (imported from a seed phrase) during the genesis ceremony.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of unauthorized caller classes are refused on every in-scope operation: unauthenticated and consumer-tier callers receive an authorization failure on system-wallet create/recover, blueprint/schema/credential/status-list authoring, and the pending-application notice.
- **SC-002**: 100% of legitimate caller classes continue to succeed: service-tier create, service-tier or platform-administrator recover, platform-tier or service-tier authoring, consumer-tier pending-application access — verified to be unchanged from before this feature.
- **SC-003**: The blueprint-management correction is verified to cover **all** operations sharing the gate (the full set of currently-bare authoring operations) through a single central change, demonstrated by tests that exercise the gate rather than each endpoint individually.
- **SC-004**: A SystemAdmin in a non-system organization is refused platform-administration operations, while a system-admin-org SystemAdmin is allowed.
- **SC-005**: Each in-scope operation enforces authorization independently of the perimeter — demonstrated by a check that the operations carry no "allow anonymous" exemption and require their expected rule.
- **SC-006**: Automated tests covering every acceptance scenario above pass in the affected services' test suites.

## Assumptions

- The genesis-ceremony operator authenticates as a platform-tier administrator before running recover (the platform bootstrap creates the first administrator, then the operator logs in as that administrator). Recover is therefore not required to run before any administrator exists.
- The system-wallet create operation's only caller is the infrastructure service-to-service path, which already presents a service-tier token.
- The consolidated recover client method has no current service-to-service caller; recover is exercised today only by the administrator CLI. The service-tier branch on recover is forward-looking for future automation and does not change any current flow.
- All current uses of the tenant system-admin gate are platform-administration operations intended to be system-admin-org-scoped (to be confirmed during implementation per FR-010).

## Out of Scope

- The PWA-local presentation verifier accepting unverified issuer signatures (review H3) — handled in sub-project 3 (verification-correctness).
- Issuer-trust shortcuts in the OIDC exchange and passkey-recovery paths (review M3) — sub-project 3.
- Other review LOW items not concerning in-code authorization gating: token-revocation fail-open on cache error (risk acceptance), stale dev audience config (cleanup), default service-principal secrets in base configuration (operations), the already-guarded anonymous bootstrap.
