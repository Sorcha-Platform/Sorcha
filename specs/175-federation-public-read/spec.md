# Feature Specification: Cross-installation federation — anonymous public-register read + node-identity peer auth

**Feature Branch**: `175-federation-public-read`

**Created**: 2026-07-01

**Status**: Draft

**Input**: User description: "Cross-installation federation: anonymous public-register read + node-identity peer auth."

> Design source: `docs/superpowers/specs/2026-07-01-federation-anonymous-public-read-design.md` (read first).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A node bootstraps trust from another installation's public register (Priority: P1)

A node in installation A (e.g. `Phaethon`, a local stack) needs the **public** System Register from
installation B (e.g. `sorcha` on n1) to bootstrap its trust root. It pulls and replicates that public
register **without holding any credential in B**, verifies the register's cryptography, and — once
replicated — its own sealing pipeline can proceed.

**Why this priority**: This is the whole feature and the unblock for real federation. Today a node in
a different installation is refused (cross-installation token rejection), so it never syncs the public
register and nothing downstream can seal.

**Independent Test**: From installation A, point it at installation B's public register endpoint with
**no A-installation credential**; confirm the register is read/replicated, its crypto verifies, and A
now holds a valid copy.

**Acceptance Scenarios**:

1. **Given** a node in installation A and a **public** register published by installation B, **When** A reads/replicates that register **anonymously** (no B credential, no A-installation service token presented to B), **Then** the register is returned and A can verify it.
2. **Given** a replicated public register, **When** A validates it, **Then** A checks the genesis attestations + crypto policy + docket/validator signatures and **rejects** on any mismatch (fail-closed).
3. **Given** installations A and B remain distinct authorities, **When** A pulls B's public register, **Then** no shared identity or cross-installation token is required or created.

---

### User Story 2 - Peer federation across installations authenticates by node identity (Priority: P1)

Two nodes in different installations complete the peer handshake/gossip/sync using **node identity**
(the node's own key), not an installation-scoped service token — so the federation link that was
fast-refused now succeeds.

**Why this priority**: Story 1's read cannot even begin until the peer link is established; today the
handshake presents a `{installation}:service` JWT and is rejected across installations.

**Independent Test**: Bring up node A pointed at node B (different installation) as a seed; confirm the
peer link reports healthy/alive and does not present an installation-scoped service token to B.

**Acceptance Scenarios**:

1. **Given** node A seeded with node B in a different installation, **When** A performs the peer handshake, **Then** it authenticates with node identity and B accepts the peer (link healthy).
2. **Given** the peer link is up, **When** A requests B's public registers, **Then** the exchange proceeds without an installation token.

---

### User Story 3 - Private data and writes stay protected (Priority: P1)

Opening public read must not open anything else. Private/non-advertised registers, writes, and
governance remain gated exactly as before.

**Why this priority**: The feature is only acceptable if it does not widen access to private data or
allow cross-installation writes — a security must-not-regress.

**Independent Test**: From installation A (no B credential), attempt to (a) read a **private** register
on B, and (b) write to any register on B; both are refused.

**Acceptance Scenarios**:

1. **Given** a **private** (non-advertised) register on B, **When** A reads it anonymously, **Then** access is refused (401/403).
2. **Given** A is not a participant on a B register, **When** A attempts a write, **Then** it is refused.
3. **Given** intra-installation service calls, **When** they run, **Then** installation-scoped service-token auth and F136 audience rejection are unchanged.

---

### Edge Cases

- **Forged/tampered register** served by a hostile peer → crypto verification fails → rejected, not persisted.
- **Anonymous read flood** → rate-limited (open ≠ unlimited).
- **A register flips public→private** → subsequent anonymous reads are refused.
- **Peer reachable at TCP but auth-refused** → surfaced as an auth failure, distinct from unreachable.
- **Replication path that does not verify** → treated as a defect to close; unverified replicated data is never trusted.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow **anonymous** read and replication of a register **when that register is public** (advertised), with no requirement that the caller hold a credential in the register's installation.
- **FR-002**: A node that reads/replicates a register from another installation MUST **cryptographically verify** it (genesis attestations, crypto policy, docket/validator signatures, register identity) **before trusting or persisting** it, and MUST fail closed on any mismatch.
- **FR-003**: Peer federation (handshake / gossip / sync) between nodes MUST authenticate using **node identity**, not an installation-scoped service token.
- **FR-004**: The system MUST continue to **refuse anonymous access to private / non-advertised registers** and MUST continue to require the target register's governance/participant authority for **writes** (no cross-installation write via this feature).
- **FR-005**: F136 cross-installation **authorization** rejection for authenticated calls MUST remain unchanged; the anonymous public-read path MUST NOT be implemented by accepting foreign-installation tokens.
- **FR-006**: The anonymous public-read/replicate path MUST be **rate-limited** using the existing centralised limiting.
- **FR-007**: The public/anonymous decision MUST be gated strictly on the register's public/advertise state, evaluated per request (so a public→private change takes effect immediately).

### Key Entities

- **Public register**: an advertised register (e.g. the System Register) intended to be discoverable and replicated by any node; carries its own genesis control record + crypto policy + sealed dockets.
- **Node identity**: a node's own installation-neutral key used to authenticate peer federation, distinct from any installation-scoped service credential.
- **Installation**: an authority domain (namespaced issuer/audiences per F136); installations remain separate.
- **Replication/verification result**: the outcome of verifying a pulled register's cryptography (accept/reject).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A node in installation A can read/replicate a public register from installation B with **zero** B credentials and **zero** A-installation tokens presented to B.
- **SC-002**: 100% of replicated registers are cryptographically verified before persistence; a tampered register is **rejected 100%** of the time.
- **SC-003**: A cross-installation peer link that was previously refused now reports **healthy/alive** and exchanges public registers.
- **SC-004**: Anonymous attempts to read a **private** register or to **write** any register on B are refused **100%** of the time.
- **SC-005**: The end-to-end unblock is demonstrable: a node in a separate installation pulls the public SSR, verifies it, and its local sealing resumes — with the two installations remaining distinct.
- **SC-006**: No regression to intra-installation service auth or F136 authenticated-authorization rejection.

## Assumptions

- **The design note is authoritative** for principle and seams: `docs/superpowers/specs/2026-07-01-federation-anonymous-public-read-design.md`.
- **Registers already carry verifiable cryptography** (genesis attestations, crypto policy, docket/validator signatures) sufficient for a puller to establish trust without the caller's token.
- **A node-level identity/key exists or can be introduced** in the peer service distinct from its installation-scoped service credential (open question O1 to confirm in planning).
- **The public gate keys on the existing `Advertise`/public register state** (open question O2 to confirm).
- **Trust remains DAD-modelled**: reading public data is open; altering (writing) requires the target register's governance — unchanged by this feature.
