# Phase 1 Data Model: Validator Exemption Authority

**Feature**: 196 · **Date**: 2026-08-28

**No persisted schema changes. No EF migration. No change to any serialized ledger shape.**

Everything below is an in-memory decision model computed during validation and discarded. This is
deliberate: the whole point of the design is that authority is derivable from material already
signed, so nothing new needs to be recorded, replicated or migrated.

---

## Entities

### ExemptionKind (enumeration)

The three administrative kinds that may be granted waivers. Named as an enumeration rather than
compared as strings so a value cannot be introduced without being classified.

| Value | Waived rules | Compensating authority (after this feature) |
|---|---|---|
| `Genesis` | all six | signing key matches the node's trusted genesis anchor fingerprint |
| `BlueprintPublish` | all six | signer entitled to publish on this register (source pending research R2) |
| `Control` | all six | signer on the register's governance roster |

**Invariant**: every value must have a non-null authority rule. A value with no rule is the defect
this feature removes, so it must be impossible to add one silently — enforced by a reflection test
over the enumeration, in the manner already used for derivation contexts and executable-definition
coverage.

---

### ExemptionClaim (transient)

What a transaction *asserts*. Untrusted input.

| Field | Source | Trust |
|---|---|---|
| `ClaimedKind` | transaction-type label, or the blueprint-identifier route | **untrusted** — submitter-set, no signature covers it |
| `Route` | which of the routes produced the claim | untrusted; recorded for FR-013 |
| `EffectiveKind` | claimed kind after the existing legacy-era guard is applied | untrusted, but correctly disambiguated (research R6) |

A claim never grants anything on its own. It only selects which authority rule must be satisfied.

---

### ExemptionAuthority (transient)

What the *signer* proves. Derived from signed material.

| Field | Derived from | Trust |
|---|---|---|
| `SignerPublicKey` | the verified signature | **trusted** — signature verification already proves possession |
| `SignerAddress` | the verified signature | trusted |
| `AnchorFingerprintMatches` | node trust anchor vs signer key fingerprint | trusted |
| `IsRegisterPublisher` | register-scoped publisher authority (research R2) | trusted |
| `IsRosterMember` | governance roster for the register | trusted |
| `Resolvable` | whether the authority source could be consulted at all | — |

**Invariant**: `Resolvable == false` never yields a grant (FR-007, fail-closed). This is the single
most consequential rule in the model and the one flagged for confirmation.

---

### ExemptionDecision (transient)

The single computed outcome. **One producer**, consumed everywhere the exemptions are read.

| Field | Meaning |
|---|---|
| `Granted` | whether the waivers apply |
| `Kind` | the kind granted, when granted |
| `Reason` | why granted or refused — the input to FR-013 |

**Invariant (the heart of the feature)**: `Granted` is true only where an `ExemptionClaim` is matched
by a satisfied `ExemptionAuthority`. A claim alone never grants; an authority without a matching
claim grants nothing either (a genuine genesis signer submitting an ordinary action is an ordinary
action).

**Why one decision object**: today the grant is computed independently in more than one place, and is
correct only where those places agree. A single value cannot half-apply. This mirrors the pattern the
codebase already enforces for derivation contexts, cross-boundary validation codes, service addresses
and publication identifiers — a value that must be consistent gets exactly one producer.

---

### RefusedClaim (observability record, FR-013)

Emitted when a claim is made and the authority is not satisfied. Distinct from an ordinary validation
failure, because it is the signature of an attempted bypass rather than a malformed transaction.

| Field | Notes |
|---|---|
| `TransactionId`, `RegisterId` | correlation |
| `ClaimedKind`, `Route` | what was claimed and how |
| `RefusalReason` | unsatisfied vs unresolvable — these are operationally different |
| `SignerAddress` | who claimed it |

Carried as a structured log event plus a counter dimensioned by kind, route and reason, on the
existing validator meter. Precedent: the existing uncorroborated-lifecycle-metadata warning, which
logs exactly this situation for the lifecycle predicates.

---

## Relocated, not redefined

`INodeTrustAnchor` moves from the Register Service into `src/Core/Sorcha.Register.Core/Provenance/`.
Its shape is unchanged and already sufficient — `IsKnown`, `NetworkId`,
`GenesisPublicKeyFingerprint`, `GenesisPayloadHash`. The concrete loader stays in the Register
Service; the Validator binds the same abstraction over the same configured/embedded genesis.

**Not** a second anchor. One network root of trust, reachable from both services.
