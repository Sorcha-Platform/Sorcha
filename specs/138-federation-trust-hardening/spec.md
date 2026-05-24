# Feature Specification: Federation Trust Hardening

**Feature Branch**: `138-federation-trust-hardening`
**Created**: 2026-05-24
**Status**: Draft
**Input**: Red-team threat model (2026-05-24) of a hardened v1, framed around "anyone can run a node."

## Context & Guiding Principle

Sorcha is a decentralised register platform where **anyone can stand up a node**. That single fact inverts the trust model: a node operator may be hostile, may run modified software, and every message a node receives over the network is potentially attacker-controlled.

This feature hardens the **permissioned-federation** model — the network is open to participation, but consensus validation is gated by a roster the operator controls, and the validator set is known and bounded. The deeper economic problems of *fully permissionless* validation (Sybil-resistant admission, bonded collateral, slashing) are explicitly a **separate, backlogged feature** (see `PERM-1..PERM-5` in `.specify/tasks/deferred-tasks.md`).

**Unifying acceptance criterion for every story below:** *Every input that crosses a node boundary must be verifiable against a cryptographic signature anchored in sealed register state — never trusted because of who sent it or how it arrived.* A red-team reviewer should be able to point at any cross-node trust decision and find the signature that backs it.

The red-team analysis found that Sorcha's cryptographic core is already strong (chain integrity, DID resolution, issuer-signature verification are all sealed-state-anchored and fail-closed). The weaknesses are concentrated at the network edges, where some trust decisions currently reduce to "a peer told me so." This feature closes those gaps.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Revocation cannot be forged by a hostile node (Priority: P1)

A credential verifier must be able to trust that a credential's revocation status is authentic, even when the revocation status list is served by an untrusted node.

Today, a verifier fetches the per-organisation revocation status list over the network and checks the revocation bit **without verifying that the list is authentic**, and when the fetch fails it silently trusts a previously cached copy (fail-open). An attacker who runs a node can serve a forged status list that flips a revoked credential back to "valid" — no authentication required — and a presented stolen or revoked credential then verifies successfully.

**Why this priority**: Cheapest possible attack (unauthenticated, any node), highest impact (defeats revocation entirely — the cornerstone of any credential system), and the smallest blast radius to fix (the change is confined to the verification side). Best value-per-effort in the feature.

**Independent Test**: Stand up a node that serves a tampered status list (revoked bit cleared) and a correctly-revoked credential. Attempt to verify the credential. It MUST be rejected. Separately, block the status-list fetch entirely and confirm verification fails closed (credential treated as revoked/unknown), not open.

**Acceptance Scenarios**:

1. **Given** a revocation status list whose signature does not match the issuing organisation's key resolved from sealed state, **When** a verifier checks a credential against it, **Then** the list is rejected and the credential is treated as having unknown/revoked status.
2. **Given** a status list whose issuer claim does not match the expected organisation's identifier, **When** a verifier consumes it, **Then** the list is rejected even if the signature itself is internally valid.
3. **Given** the status list cannot be fetched (network failure, node offline, malicious drop), **When** a verifier needs revocation status, **Then** the verifier fails closed (treats the credential as not-verifiable) rather than trusting stale cached data.
4. **Given** a correctly signed, fresh status list from the genuine issuer marking a credential as revoked, **When** a verifier checks that credential, **Then** verification fails because the credential is revoked.

---

### User Story 2 - A node's identity and its claims are cryptographically provable (Priority: P1)

A node joining or participating in the peer network must prove control of a cryptographic identity, and every claim it broadcasts (which registers it holds, that it is alive) must be signed so other nodes can reject forgeries and replays.

Today, peer registration has no admission control, peer-to-peer traffic can run unencrypted, peer authentication is silently disabled when no signing key is configured, network advertisements carry no signature and no proof that the advertising node actually holds the register it claims, and the anti-replay sequence numbers that already travel on heartbeat messages are never validated. An attacker can register unlimited free identities, advertise ownership of any register, and replay captured messages indefinitely.

**Why this priority**: This is the open front door to the network. Until peer identity and advertisements are authenticated, no higher-layer guarantee about cross-node data can be trusted, because the transport itself is forgeable.

**Independent Test**: From a node that does not control a given identity key, attempt to (a) register under an arbitrary peer identifier, (b) advertise ownership of a register it does not hold, and (c) replay a previously captured valid heartbeat. All three MUST be rejected. Confirm that unencrypted peer transport is refused outside development environments.

**Acceptance Scenarios**:

1. **Given** a node attempting to join, **When** it cannot produce a valid signature proving control of the identity it claims, **Then** its registration is refused.
2. **Given** a register-ownership advertisement, **When** it is not signed by the advertising node's proven identity, **Then** it is rejected and not propagated.
3. **Given** a captured, previously-valid signed message, **When** it is replayed (stale sequence number or stale timestamp), **Then** the receiving node rejects it.
4. **Given** a peer connection in a production or staging environment, **When** the transport is not mutually authenticated and encrypted, **Then** the connection is refused.
5. **Given** a flood of registration attempts from one source, **When** they exceed a configured rate, **Then** excess attempts are throttled.

---

### User Story 3 - Validator voting authority derives only from sealed on-chain roster (Priority: P1)

The right to cast a consensus vote must derive **solely** from a validator-roster record sealed in the register's on-chain governance state, and detected misbehaviour must be punished automatically and deterministically by every honest node — with no human operator in the loop.

Today, validator admission defaults to a mode where any node can self-register and become an active validator immediately; the live roster is held in operational stores that lag the on-chain record; and although the system *detects* double-voting and chain violations, the *consequence* (removing a misbehaving validator) is a manual operator action. A withholding validator (one that accepts work but never seals it) has no automatic penalty at all.

**Why this priority**: A self-admitting or unpunished validator set undermines every transaction the network seals. This story also deliberately builds the two primitives — an authoritative on-chain roster and automatic, deterministic equivocation handling — that the backlogged permissionless feature will later extend (swapping roster-admission for stake-admission, roster-ejection for collateral-slashing).

**Independent Test**: Submit a consensus vote signed by a key that is not present in the sealed on-chain roster; every honest node MUST reject it deterministically. Cause a validator to equivocate (sign two conflicting states); confirm it is ejected automatically with no operator action. Cause a validator to go silent after accepting work; confirm automatic ejection after the liveness timeout.

**Acceptance Scenarios**:

1. **Given** a consensus vote, **When** the signing key is not in the roster sealed in on-chain governance state, **Then** the vote is rejected by every honest node and contributes nothing to quorum.
2. **Given** a new register, **When** it is created without an explicit admission policy, **Then** validator admission defaults to requiring explicit roster approval (not open self-registration).
3. **Given** a validator that signs two conflicting states for the same slot, **When** the conflict is observed, **Then** that validator is ejected from the effective roster automatically and deterministically, identically on every honest node, without operator intervention.
4. **Given** a validator that accepts a transaction for sealing but does not seal within the configured liveness window, **When** the timeout elapses, **Then** a sealed liveness-timeout record is produced and the validator is ejected automatically.
5. **Given** the operational (cached) roster diverges from the on-chain sealed roster, **When** voting authority is evaluated, **Then** the sealed on-chain roster is authoritative and the cache is never trusted over it.

---

### User Story 4 - Recovered blueprints are verified before being trusted (Priority: P2)

When a node reconstructs a blueprint from another node's data during recovery or synchronisation, it must verify the blueprint's provenance against sealed register state before storing or executing it.

Today, event-driven blueprint recovery stores a blueprint fetched from another node's response with no signature or digest verification, carried over an untrusted messaging channel. A node that is tricked into recovering a malicious blueprint could store and act on attacker-supplied workflow logic.

**Why this priority**: Closes a provenance gap introduced by recent cross-node recovery work. Lower urgency than US1–US3 because exploitation requires the victim node to be on a recovery/sync path and there is partial mitigation today (blueprints are keyed by identifier and the register carries the canonical version), but "partial mitigation" is exactly what a red team converts into a full compromise.

**Independent Test**: Feed a recovery path a blueprint whose content does not match the digest sealed in the register. It MUST be rejected and not stored. Feed a correctly-sealed blueprint and confirm it is accepted.

**Acceptance Scenarios**:

1. **Given** a blueprint offered to a recovery/sync path, **When** its content digest does not match the digest sealed in the register, **Then** it is rejected and not stored.
2. **Given** a blueprint with no verifiable provenance (no sealed digest or signature available), **When** recovery runs, **Then** it is not silently stored on the strength of the transport alone.
3. **Given** a correctly-sealed blueprint matching its on-chain digest, **When** recovery runs, **Then** it is accepted and stored.

---

### User Story 5 - Captured presentation proofs cannot be replayed within the session window (Priority: P2)

A holder's key-binding proof presented during a credential verification must be bound to a short, independently-checked expiry and re-validated against current revocation status at the moment of verification — so a captured proof cannot be replayed even before the verification session closes.

Today, a key-binding proof is checked against a session nonce, an audience, and the session lifetime, but the proof itself carries no independently-enforced expiry. Within the (several-minute) session window, a captured proof can be replayed — even if the holder's device credential was revoked mid-session.

**Why this priority**: Tightens an existing presentation lifecycle. The exposure window is bounded (single session, requires capturing a live proof), so it ranks below the structural gaps in US1–US3, but it is a genuine replay vector against high-value presentations.

**Independent Test**: Capture a valid key-binding proof, then replay it after its short expiry has elapsed but while the session is still open — it MUST be rejected. Separately, revoke the device credential mid-session and replay a still-fresh proof — verification MUST fail because revocation is re-checked at verify time.

**Acceptance Scenarios**:

1. **Given** a key-binding proof whose own expiry has passed, **When** it is presented within an otherwise-open session, **Then** it is rejected.
2. **Given** a key-binding proof with no expiry, **When** it is presented, **Then** it is rejected for failing the mandatory freshness requirement.
3. **Given** a device credential revoked after a session opened but before verification completes, **When** a presentation is verified, **Then** revocation status is re-checked at verify time and verification fails.

---

### User Story 6 - Open-participant credential delivery keys cannot be hijacked (Priority: P3)

For a late-bound ("open") participant who has not yet published a participant record, the credential-delivery key carried in a submission must be bound to a verifiable prior artefact, so an attacker cannot claim an open participant slot with a key they control.

Today, key precedence correctly protects *published* participants (a published key cannot be overridden). But for an *unpublished* open participant, the carried delivery key is accepted without verification and travels in plaintext inside the (signed) submission. An attacker who can submit into an open slot before the genuine participant binds could insert their own delivery key and intercept the credential.

**Why this priority**: Lowest urgency — affects only the open-participant late-binding edge, and the surrounding submission is already signed. Ranked P3 but **confirmed in v1 scope (2026-05-24)**: it is closed alongside the other "verify against sealed state" stories while those surfaces are being touched, rather than left as a known cheap-ish interception on open slots.

**Independent Test**: Submit into an open participant slot with a carried delivery key not bound to any prior invitation or pre-registration. It MUST be rejected. Submit with a key bound to a valid invitation/pre-registration and confirm acceptance.

**Acceptance Scenarios**:

1. **Given** a submission into an open participant slot, **When** the carried delivery key is not bound to a verifiable prior artefact (invitation token or sealed pre-registration), **Then** the key is rejected.
2. **Given** a carried delivery key bound to a valid prior artefact, **When** the submission is processed, **Then** the key is accepted for credential delivery.

---

### Edge Cases

- **Clock skew** (US1, US5): expiry and freshness checks must tolerate a bounded, configured clock-skew window so honest nodes with minor drift are not falsely rejected, while still closing the replay window.
- **Status-list publisher key rotation** (US1): when the issuing organisation rotates its status-list signing key, verifiers must resolve the current key from sealed state, not a stale pin.
- **Roster change mid-consensus** (US3): a vote cast just before a validator's sealed ejection must be evaluated against a deterministic, well-defined roster snapshot so all honest nodes reach the same conclusion about whether the vote counted.
- **Quorum impossibility after ejections** (US3): automatic ejection must not be able to silently drop the roster below a workable quorum without surfacing the condition (ties to existing deadlock-detection concerns, `GOV-5`).
- **Legitimate node re-registration** (US2): a genuine node restarting and re-registering under its existing identity key must succeed; only identity-forgery and replay are refused.
- **Recovery of a blueprint not yet sealed** (US4): if the canonical digest is not yet available from the register, recovery must wait or refuse rather than trust the transport.

## Requirements *(mandatory)*

### Functional Requirements

**US1 — Revocation authenticity**
- **FR-001**: The system MUST verify the cryptographic signature of a revocation status list against the issuing organisation's key resolved from sealed register state before trusting any revocation bit it contains.
- **FR-002**: The system MUST reject a status list whose issuer claim does not match the expected organisation identifier, even if the signature is internally valid.
- **FR-003**: The system MUST fail closed when a status list cannot be fetched or verified — treating affected credentials as not-verifiable rather than serving stale cached status.
- **FR-004**: The system MUST enforce a freshness bound on accepted status lists, rejecting expired lists.

**US2 — Authenticated peers**
- **FR-005**: A node MUST prove control of its claimed cryptographic identity (e.g., by signing a challenge) before its registration is accepted.
- **FR-006**: The system MUST reject network advertisements and liveness messages that are not signed by the originating node's proven identity.
- **FR-007**: The system MUST validate anti-replay data already carried on peer messages — rejecting messages with non-advancing sequence numbers or stale timestamps.
- **FR-008**: The system MUST refuse unauthenticated or unencrypted peer transport outside development environments (fail-closed, not silently disabled when unconfigured).
- **FR-009**: The system MUST rate-limit peer registration attempts per source.

**US3 — Sealed-roster voting authority**
- **FR-010**: The system MUST derive consensus voting authority solely from the validator roster sealed in on-chain governance state; votes from keys not in that roster MUST be rejected deterministically by every honest node.
- **FR-011**: The system MUST default new registers to an admission policy requiring explicit roster approval, not open self-registration.
- **FR-012**: The system MUST eject a validator that equivocates (signs conflicting states for the same slot) automatically and deterministically, identically on every honest node, with no operator action.
- **FR-013**: The system MUST produce a sealed liveness-timeout record and eject a validator that accepts work but fails to seal within a configured liveness window.
- **FR-014**: The system MUST treat the on-chain sealed roster as authoritative over any operational/cached copy when evaluating voting authority.

**US4 — Verified recovery**
- **FR-015**: The system MUST verify a recovered or synchronised blueprint's content against a provenance anchor (digest or signature) sealed in the register before storing or executing it.
- **FR-016**: The system MUST refuse to store a blueprint that has no verifiable provenance, rather than trusting the delivery channel.

**US5 — Replay-resistant presentations**
- **FR-017**: The system MUST require an independently-enforced expiry on each holder key-binding proof and reject proofs lacking one.
- **FR-018**: The system MUST validate the key-binding proof's expiry against wall-clock time (within the configured skew tolerance) at verification, independent of the overall session lifetime.
- **FR-019**: The system MUST re-check credential revocation status at the moment of verification, not only at session open.

**US6 — Bound open-participant keys**
- **FR-020**: The system MUST bind a carried credential-delivery key for an unpublished open participant to a verifiable prior artefact (invitation token or sealed pre-registration), and reject unbound carried keys for open slots.

**Cross-cutting**
- **FR-021**: Every cross-node trust decision introduced or modified by this feature MUST be traceable to a signature anchored in sealed state; the verification path MUST be exercised by an automated test that proves the forged/unsigned variant is rejected.
- **FR-022**: Security-relevant rejections (forged list, unsigned advertisement, out-of-roster vote, replayed proof, failed recovery verification) MUST be observable via metrics/telemetry for monitoring and alerting.

### Key Entities

- **Revocation Status List**: A signed, time-bounded statement by an organisation of which of its credentials are revoked. Authenticity is anchored in the organisation's sealed-state key; freshness is bounded by an expiry.
- **Node Identity**: A cryptographic key pair that uniquely identifies a network node. A node's claims (registration, advertisements, liveness) are valid only when signed by the identity it proves control of.
- **Register Advertisement**: A signed claim by a node that it holds a given register, used for discovery and sync routing.
- **Sealed Validator Roster**: The authoritative, on-chain governance record of which validator keys may vote, including admission policy and ejection state. The single source of voting authority.
- **Liveness-Timeout Record**: A sealed, deterministic artefact attesting that a validator failed to seal accepted work within the configured window — the trigger for automatic ejection.
- **Key-Binding Proof**: A holder/device-signed proof presented during verification, now carrying an independently-enforced short expiry.
- **Carried Delivery Key**: A credential-delivery key supplied in a submission for a recipient; for open participants it must be bound to a verifiable prior artefact.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A forged, unsigned, or wrong-issuer revocation status list is rejected 100% of the time; a genuinely revoked credential never verifies as valid regardless of which node serves the status list.
- **SC-002**: When revocation status is unavailable, the verifier fails closed in 100% of cases (zero fail-open occurrences).
- **SC-003**: A node cannot register under an identity it does not control, cannot advertise ownership of a register it does not hold, and cannot replay a captured message — all three rejected 100% of the time in adversarial testing.
- **SC-004**: A consensus vote from a key absent from the sealed roster contributes zero weight to quorum on every honest node, with identical outcomes across nodes (deterministic).
- **SC-005**: A detected equivocating or withholding validator is ejected automatically with zero required operator actions, and the ejection is reproduced identically on every honest node.
- **SC-006**: A blueprint whose content does not match its sealed provenance anchor is never stored or executed.
- **SC-007**: A captured key-binding proof replayed after its expiry, or after the underlying credential's mid-session revocation, fails verification 100% of the time.
- **SC-008**: A red-team reviewer can trace every cross-node trust decision touched by this feature to a specific signature-verification step backed by an automated negative test; there are zero "trust the sender/transport" paths remaining in the affected surfaces.

## Assumptions

- **Pre-release, no backward-compatibility burden**: The platform is pre-release (production readiness ~30%). Changing the default validator-admission policy and tightening verification is acceptable without a migration path for existing deployments. Existing dev/test registers may be re-created.
- **Sealed-state trust anchors already exist and are sound**: The red-team analysis confirmed DID resolution, issuer-signature verification, and chain integrity are already sealed-state-anchored and fail-closed. This feature reuses those anchors rather than rebuilding them.
- **Known, bounded validator set**: Federation assumes the operator controls roster admission. Economic/Sybil defences for open admission are out of scope (backlogged).
- **Configurable thresholds**: Clock-skew tolerance, status-list freshness window, key-binding-proof expiry, peer-registration rate limit, and validator liveness-timeout window are all configurable, with secure defaults; their exact values are tuning details for planning, not scope decisions.
- **Demo/dev bridges remain dev-only**: Any demo-mint or unauthenticated-dev affordance must be structurally excluded from production builds/config, not merely flag-gated.

## Dependencies

- Builds on existing sealed-state trust anchors (DID resolver, issuer-signature verification, chain integrity, double-vote detection).
- US3 establishes the on-chain roster + deterministic equivocation primitives that the backlogged **permissionless validation** feature (`PERM-1..PERM-5`) will extend.
- US5 extends the existing timebound presentation lifecycle.
- US4 closes a gap in the existing cross-node blueprint recovery path.
- Related existing backlog/governance items to keep coherent: `GOV-5` (quorum deadlock detection), `GOV-6` (roster reconstruction caching), `TRUST-6` (consensus finality).

## Out of Scope (explicitly)

- **Permissionless / open-membership validation** — Sybil-resistant stake-based admission, bonded collateral, economic slashing. These are the separate backlogged feature (`PERM-1..PERM-5`). This feature assumes a roster-gated, operator-controlled validator set.
- General consensus-finality redesign (`TRUST-6`) beyond what roster authority requires.
- Re-encryption of historical payloads after key compromise (`TRUST-10`).
- Cross-register reference verification (`TRUST-7`).
