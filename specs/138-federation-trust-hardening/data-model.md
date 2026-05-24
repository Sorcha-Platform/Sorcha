# Data Model: Federation Trust Hardening (Feature 138)

**Date**: 2026-05-24

Entities are grouped by user story. "New" = net-new type/field; "Change" = modification to an existing type. Implementation types named where known from research; treat as the authoritative target.

---

## US1 — Revocation authenticity

### Status List Verification Context *(new — transient, not persisted)*
Carries what the verifier needs to authenticate a fetched status list.
| Field | Type | Notes |
|-------|------|-------|
| ExpectedIssuerDid | string | `did:sorcha:org:{orgId:N}` derived from the consuming credential; the `iss` claim MUST equal this |
| Kid | string? | from JWT header when present; resolver falls back to first VM matching `alg` |
| ResolvedKey | JWK (JsonElement) | from `IIssuerKeyResolver`; null ⇒ fail closed |
| ExpiresAt | timestamp | from list `exp`; expired ⇒ reject (no +24h default) |
| VerificationOutcome | enum | `Verified` \| `SignatureFailed` \| `IssuerMismatch` \| `Unresolved` \| `Expired` \| `FetchFailed` |

**Change — `CachedList`** (`StatusListCache`): only a *verified* list may be cached. `VerificationOutcome != Verified` ⇒ never cached, caller told "unverifiable".

**Change — status-list JWT header** (`CitizenStatusListPublisher`): add `kid` identifying the signing verification method.

**State transition (fetch)**: `Requested → Fetched → SignatureVerified → IssuerPinned → FreshnessChecked → Trusted`. Any failure transitions to `Rejected(reason)` (fail closed). Previously: fetch failure → `ServedStale` (removed).

---

## US2 — Authenticated peers

### Node Identity *(new — persisted in `PeerDbContext`)*
| Field | Type | Notes |
|-------|------|-------|
| NodeId | string (PK) | public-key thumbprint; self-certifying |
| PublicKey | bytes | ED25519 public key, exported in messages |
| EncryptedPrivateKey | bytes | private key sealed via Key Protection Provider (AES-256-GCM) |
| Algorithm | enum | `ED25519` (initial) |
| CreatedAt | timestamp | generated on first startup |

### Change — `PeerNode`
| Field | Type | Notes |
|-------|------|-------|
| PeerId | string | now bound to / equal to the node public-key thumbprint (was arbitrary string) |
| PublicKey | bytes | **new** — captured at authenticated registration; used to verify later messages |
| LastHeartbeatSequenceNumber | long | **new** — monotonicity anchor for replay rejection |
| LastHeartbeatTimestamp | timestamp | **new** — freshness anchor |

### Change — proto messages (`peer_communication.proto`, `peer_heartbeat.proto`)
| Message | Added fields | Notes |
|---------|--------------|-------|
| RegisterPeerRequest | `bytes public_key`, `bytes signature`, `int64 timestamp`, challenge nonce | signature over `(PeerId‖Address‖Port‖Timestamp‖challenge)` |
| RegisterAdvertisement | `bytes signature` | signature over `(register_id‖latest_version‖latest_docket_version)` by node key |
| Heartbeat | (validate existing `sequence_number`/`timestamp`) | + signature over heartbeat body |

**Registration state transition**: `ChallengeIssued → SignedResponseReceived → SignatureVerified → Registered`. Failure ⇒ `Refused`. Re-registration under an existing `NodeId` with a valid signature ⇒ `Registered` (idempotent restart). Replay (stale seq/timestamp) ⇒ `Rejected`.

### Rate-limit state *(new — interceptor)*
Per-source counters keyed `{sourceNodeId|ip}:{method}` feeding `RESOURCE_EXHAUSTED`. Fed by existing `RateLimitSettings`.

---

## US3 — Sealed-roster vote authority

### Change — effective roster source
`ValidatorRoster` / `RegisterControlRecord.Validators` (already sealed) becomes the **authoritative** source for vote authority. The `ValidatorRegistry` (Redis/Mongo) becomes a **derived, non-authoritative cache**; on divergence the sealed record wins.

### Change — `ValidatorRosterEntry`
| Field | Type | Notes |
|-------|------|-------|
| Status | enum | `Active` \| `Pending` \| `Ejected` (ejection now a first-class sealed state, was cache-only `Revoked`) |
| EjectionRef | string? | **new** — transaction id of the sealing ejection control-tx |

### Change — `RegisterPolicy.PolicyValidatorConfig`
| Field | Type | Change |
|-------|------|--------|
| RegistrationMode | enum | `CreateDefault()` default flips `Public → Consent` |

### Validator Ejection Record *(new — sealed control transaction `control.validator.eject`)*
| Field | Type | Notes |
|-------|------|-------|
| ValidatorId | string | subject |
| Reason | enum | `Equivocation` \| `LivenessTimeout` |
| Evidence | object | for equivocation: the two conflicting signed votes (slot, two docket hashes, two signatures) |
| ObservedAt | timestamp | |
| Deterministic | invariant | any honest node with the same evidence produces an identical record ⇒ convergent ejection |

### Liveness-Timeout Proof *(new — sealed control transaction `control.validator.liveness-violation`)*
| Field | Type | Notes |
|-------|------|-------|
| ValidatorId | string | subject |
| AcceptedTxRef | string | the work it accepted but did not seal |
| Deadline | timestamp | accept-time + `DocketTimeoutSeconds` |
| ObservedAt | timestamp | > Deadline + skew |

**Vote authority transition**: `VoteReceived → KeyInSealedRoster? → SignatureValid? → NotDoubleVote? → Counted`. Any "no" ⇒ `Rejected` (zero quorum weight), identically on every honest node. Detected equivocation ⇒ emit `control.validator.eject`; on seal, entry `Active → Ejected`.

---

## US4 — Blueprint provenance

### Change — `PublishedBlueprintEntry` (`IRegisterServiceClient`)
| Field | Type | Notes |
|-------|------|-------|
| ContentHash | string | **new** — SHA-256 over canonical blueprint JSON, sealed at `control.blueprint.publish` |

**Recovery transition**: `Fetched → CanonicalHashRecomputed → MatchesSealedHash? → Stored`. Mismatch or missing sealed hash ⇒ `Rejected` (not stored).

---

## US5 — Presentation replay

### Change — KB-JWT validation (no persisted entity)
KB-JWT MUST carry `exp`. Verifier checks `exp` against `_clock.GetUtcNow()` within `ClockSkewSeconds`. Missing `exp` ⇒ reject (FR-017). Ordering: KB-JWT `exp` checked **before** delegation/status validation.

### Change — `VerifierSession`
No schema change; revocation continues to be checked fresh at verify time (not cached on session).

---

## US6 — Open-participant key binding

### Carried-Key Binding *(new — validation rule, leverages existing invitation)*
| Field | Type | Notes |
|-------|------|-------|
| CarriedKey | JWK / public key | from submission form field (existing) |
| BindingArtifactRef | string | invitation id or sealed pre-registration id |
| Commitment | string | derived from `RegisterInvitationRecord.Nonce` + carried key; must match |

**Resolution transition (open + unpublished participant)**: `CarriedKeyPresent → BindingArtifactResolved? → CommitmentMatches? → Accepted`. Any "no" ⇒ `Rejected`. Published-participant path (`ResolvePublicKeyAsync` wins) unchanged.

---

## Cross-cutting invariants

- **Fail-closed**: every transition above terminates in `Rejected` when verification cannot complete; no state leads to "trust anyway".
- **Sealed-state anchoring**: every `Verified`/`Counted`/`Stored`/`Accepted` terminal state traces to a signature checked against a key or digest sealed in register state (FR-021).
- **Determinism (US3)**: ejection and vote-authority outcomes are pure functions of sealed state + observed evidence, identical across honest nodes (SC-004, SC-005).
