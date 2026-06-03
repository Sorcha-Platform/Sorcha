# Feature Specification: Verification-correctness

**Feature Branch**: `148-verification-correctness`
**Created**: 2026-06-03
**Status**: Draft
**Input**: Sub-project 3 of the security-hardening initiative. Authoritative source: `docs/superpowers/specs/2026-06-03-verification-correctness-design.md` and `docs/reviews/2026-06-02-architecture-review.md` §2 (H3, M3), §5.1, §7, §8.

## Overview

Three flows currently accept something without fully verifying its signature — or, where full verification is genuinely impossible (an offline citizen device), present the result as if they had. This feature makes each flow either verify the signature for real, or — where it cannot — be **honest** about what was and wasn't verified, and makes a latent no-op verification **fail loud** so it cannot be silently switched on.

The actors are: a **citizen** doing an offline "doorstep" check of someone's credential on their own device; the **platform** completing a social-login (OIDC) sign-in; and an **operator** who might one day enable wallet recovery.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A citizen is told the truth about what was verified (Priority: P1)

When a citizen verifies a presented credential on their own device while offline, the device can check the holder→device chain and revocation status, but it cannot reach the network to cryptographically confirm the credential's **issuer** signature. Today the device shows a plain "valid" result regardless — concealing that the issuer was never checked. The citizen must instead see an honest result that distinguishes a fully-verified credential from one where the issuer could not be confirmed.

**Why this priority**: A person makes a trust decision (e.g. at their door) based on this result. A false "fully valid" is the most directly harmful of the three findings, even though it is the citizen's own lower-stakes tool rather than the authoritative server gate.

**Independent Test**: Run the device verifier against a presentation whose issuer key cannot be resolved and confirm the outcome reports "issuer not verified" (not plain success); run it where the issuer key resolves and the signature checks, and confirm it reports "issuer verified". Confirm the authoritative server-side verifiers (which require the issuer signature) are unchanged.

**Acceptance Scenarios**:

1. **Given** a presentation whose issuer signature cannot be verified (no resolvable key) on a device that does not require the issuer signature, **When** verification runs, **Then** the outcome is surfaced as holder-verified-but-issuer-**not**-verified (reduced assurance), never as plain "valid".
2. **Given** a presentation whose issuer key resolves and whose issuer signature is valid, **When** verification runs, **Then** the outcome reports the issuer as **verified**.
3. **Given** a verifier configured to require the issuer signature (the authoritative server gate), **When** the issuer key cannot be resolved, **Then** verification **fails** — behaviour unchanged by this feature.
4. **Given** the holder→device chain or revocation status is invalid, **When** verification runs, **Then** it fails regardless of issuer-signature status — behaviour unchanged.

---

### User Story 2 - Social-login identity tokens are cryptographically verified (Priority: P1)

When a user signs in through an external identity provider, the platform receives an identity token and trusts its claims (who the user is, their email) to create or match an account. Today the token's claims (issuer, audience, expiry, replay-nonce) are checked, but its **signature** is not — the platform trusts that the token came from the configured provider because it was fetched over a secure channel. The platform must additionally verify the token's signature against the provider's published keys before trusting its claims.

**Why this priority**: This is on the live authentication path. While the current behaviour is defensible for the specific flow in use (the token is fetched directly from the provider over a secure channel), verifying the signature removes a standing footgun and is defense-in-depth.

**Independent Test**: Complete a sign-in with a token correctly signed by the provider's key and confirm it succeeds; present a token with a tampered or wrong signature (or when the provider's keys can't be fetched) and confirm sign-in is refused.

**Acceptance Scenarios**:

1. **Given** an identity token signed by the provider's current signing key, **When** the platform validates it, **Then** the signature check passes and sign-in proceeds (existing issuer/audience/expiry/nonce checks still apply).
2. **Given** an identity token with an invalid or absent signature, **When** the platform validates it, **Then** sign-in is refused.
3. **Given** the provider's signing keys cannot be fetched, **When** the platform validates a token, **Then** sign-in is refused (fail-closed) rather than trusting the token unverified.
4. **Given** a token failing any existing check (issuer / audience / expiry / nonce), **When** validated, **Then** it is still refused — those checks are retained.

---

### User Story 3 - Disabled recovery cannot be switched on with broken verification (Priority: P2)

Wallet recovery (via passkey or via an organisation) is currently disabled by a feature flag. The code paths behind that flag re-key a wallet after only confirming a record exists — they do **not** perform the cryptographic proof (a passkey assertion, or an organisation signature) that should authorise recovery. If an operator enabled the flag today, recovery would proceed without real proof. The disabled paths must fail loudly rather than silently "recover", until the real verification is built.

**Why this priority**: Latent, not live (the flag is off), so lower than the active paths — but a dangerous footgun: enabling a flag should never silently bypass authentication.

**Independent Test**: With the recovery feature enabled, invoke each recovery path and confirm it refuses with an explicit "not supported / not implemented" failure rather than completing a re-key.

**Acceptance Scenarios**:

1. **Given** the recovery feature is enabled, **When** the passkey-recovery path reaches the point where it would unwrap the recovery key, **Then** it fails loudly (explicit not-supported error) instead of re-keying the wallet.
2. **Given** the recovery feature is enabled, **When** the organisation-recovery path reaches the same point, **Then** it fails loudly likewise.
3. **Given** the recovery feature is disabled (current default), **When** any recovery endpoint is called, **Then** it remains refused by the feature gate as today.

---

### Edge Cases

- Device verifier, issuer key resolves but the signature is **invalid** (not merely unresolvable): verification fails (this is the verified-rejection path, distinct from the unresolved-key reduced-assurance path).
- Identity provider rotates its signing keys between two sign-ins: the platform tolerates rotation (re-fetches keys) rather than failing valid tokens signed with the new key.
- Identity token references a key id the provider's key set does not contain: refused (no matching key → cannot verify → fail-closed).
- A provider with no published key location configured: token cannot be verified → sign-in refused (treated as misconfiguration, fail-closed).

## Requirements *(mandatory)*

### Functional Requirements

**Device-verifier honesty (US1)**

- **FR-001**: The verification outcome MUST carry an explicit issuer-signature status distinguishing "issuer verified" from "issuer not verified".
- **FR-002**: When the device does not require the issuer signature and the issuer key cannot be resolved, the outcome MUST report "issuer not verified" while still reflecting holder-chain and revocation results — it MUST NOT present as plain unqualified success.
- **FR-003**: When the issuer key resolves and the issuer signature is valid, the outcome MUST report "issuer verified".
- **FR-004**: The citizen-facing device verification UI MUST surface the issuer-signature status so the person sees whether the issuer was confirmed.
- **FR-005**: Verifiers that require the issuer signature (authoritative server gates) MUST be unaffected — they continue to reject when the issuer cannot be verified.
- **FR-006**: The offline reduced-assurance behaviour MUST be documented as a deliberate, scoped exception.

**Social-login token verification (US2)**

- **FR-007**: The platform MUST verify the identity token's signature against the provider's published signing keys before trusting its claims.
- **FR-008**: The platform MUST obtain the provider's signing keys from the provider's published key location (or its discovery document), and MUST tolerate key rotation.
- **FR-009**: If the signature cannot be verified — invalid signature, no matching key, or keys unobtainable — the platform MUST refuse the sign-in (fail-closed).
- **FR-010**: All existing identity-token checks (issuer, audience, expiry, replay-nonce) MUST be retained.

**Recovery fail-loud (US3)**

- **FR-011**: Each disabled recovery path (passkey, organisation) MUST fail with an explicit not-supported error at the point it would otherwise authorise recovery without the required cryptographic proof.
- **FR-012**: The feature gate that disables recovery MUST remain; this requirement is defense-in-depth for the case where the gate is opened before the real verification exists.

### Key Entities *(include if feature involves data)*

- **Verification outcome**: The result of verifying a presented credential; now includes an issuer-signature status alongside the existing overall validity, holder-chain, and revocation results.
- **Identity token**: The signed token issued by an external identity provider during social login; carries the user's subject, email, and standard claims.
- **Provider signing keys**: The external identity provider's published public keys used to verify identity-token signatures; subject to rotation.
- **Recovery proof**: The cryptographic proof (passkey assertion or organisation signature) that should authorise a wallet recovery; not yet implemented.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of device verifications where the issuer signature is not confirmed are presented to the citizen as reduced-assurance (issuer not verified), never as plain "valid".
- **SC-002**: 100% of device verifications where the issuer signature is confirmed report "issuer verified", and authoritative server-side verifiers show no behaviour change.
- **SC-003**: A social-login sign-in with a validly-signed identity token succeeds; one with an invalid/absent signature, or when provider keys are unobtainable, is refused — verified by automated tests.
- **SC-004**: With recovery enabled, both recovery paths refuse with an explicit not-supported error rather than completing a re-key — verified by automated tests.
- **SC-005**: All existing social-login checks (issuer/audience/expiry/nonce) remain enforced after the change.
- **SC-006**: Automated tests covering every acceptance scenario pass in the affected services' test suites.

## Assumptions

- The social-login flow in use is the authorization-code flow, with the identity token fetched server-side from the provider's token endpoint over a secure channel; signature verification is added as defense-in-depth, not because the current channel is insecure.
- The device (PWA) verifier intentionally runs without requiring the issuer signature so offline doorstep checks remain usable; the correctness fix is honesty about the result, not forcing a network round-trip.
- The authoritative server-side verifiers already require and check the issuer signature; this feature does not change their posture.
- Wallet recovery remains disabled by its feature flag; full passkey/organisation recovery verification is a separate future feature.

## Out of Scope

- **Online issuer verification on the device** — resolving and checking issuer signatures on the PWA when it has connectivity (a consumer-accessible DID resolution path). Backlogged.
- **Consolidating the two credential-verification stacks** onto one unified trust evaluator (review §5.1) — a separate future sub-project.
- **Full wallet-recovery verification** — building the real passkey assertion and organisation-signature proofs, alongside the wallet-recovery feature itself.
