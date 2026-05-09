# Research: Production Issuer Signature Verification

**Feature**: 120-production-issuer-signature-verification
**Phase**: 0 (research, alternatives, decision rationale)
**Date**: 2026-05-09
**Status**: Complete — no NEEDS CLARIFICATION markers remain

## Scope

The product decisions D1–D6 were locked in a 2026-05-09 brainstorm session and captured in the design doc. This research artifact records, for each decision, the alternatives considered and why they were rejected — the audit trail that makes the decisions defensible to future readers (and to the agent that picks up `/speckit.tasks` next).

No open questions remain. Where the design doc deferred a question to "coding time" (e.g., specific cache TTLs, DID document version metadata), the default is documented here so the planner does not need to revisit them.

---

## R1 — Single canonical resolver interface (FR-024)

**Decision**: Standardise on `IDidResolver` (W3C-shaped, `DidDocument` return). Retire legacy `IDIDResolver` (capital DID, custom `{PublicKey, Algorithm}` return).

**Rationale**:
- W3C DID Core is the standard the rest of the credential ecosystem (issuers, wallets, verifiers, relying parties) speaks.
- `IDidResolver` already supports three methods (`SorchaDidResolver`, `WebDidResolver`, `KeyDidResolver`) wired through `IDidResolverRegistry`. The infrastructure is built; only the migration of one consumer is missing.
- `IDIDResolver` is consumed in exactly one place (`Sorcha.Register.Service/Program.cs:205`). The migration is mechanical.
- Two parallel resolvers in production code is a maintenance smell that grows the longer it persists.

**Alternatives considered**:
- *Keep both, deprecate legacy gradually*: rejected. A single consumer doesn't justify a deprecation window. The migration is small enough to do in one PR.
- *Migrate consumer logic into the legacy interface*: rejected. The legacy shape is non-standard; investing in it would deepen the wrong-direction debt.

**Migration shape**: the `Sorcha.Register.Service/Program.cs:205` call site reads `IDIDResolver.ResolveAsync(did)` and uses `DIDResolutionResult.PublicKey` / `.Algorithm`. The W3C path: call `IDidResolverRegistry.ResolveAsync(did)` → for `did:sorcha:r:*:t:*` (the legacy resolver's specialty), the W3C `SorchaDidResolver` emits service endpoints only, **not** key material extracted from the control transaction payload. The legacy resolver did extract attestation public keys; the W3C resolver punts that to `IRegisterServiceClient.GetTransactionAsync`. **Therefore the migration replaces one DID-resolver call with two service calls** at the consumer site. Acceptable; the consumer is not in a hot path.

---

## R2 — Federation via `did:web` (D1)

**Decision**: Sorcha-hosted, path-based `did:web:{platform-domain}:orgs:{orgId}` → `https://{platform-domain}/orgs/{orgId}/did.json`. Static JSON regenerated on key events. BYO-domain deferred.

**Rationale**:
- Standards-compliant external wallets and verifiers can resolve `did:web` with zero Sorcha-specific code (SC-004).
- Static JSON behind the gateway is trivially CDN-cacheable; one route, no per-request work.
- Path-based form is forward-compatible with later subdomain or BYO-domain via `alsoKnownAs` — old URL stays alive forever.

**Alternatives considered**:
- *Subdomain-based (`did:web:{orgId}.{platform}`)*: rejected for v1. Smoother BYO upgrade ceremony but requires wildcard TLS, per-org subdomain provisioning, CDN config aware of subdomains. Real ops cost without meeting a real day-1 need.
- *BYO-domain mandatory*: rejected. High onboarding friction; orgs don't want to operate `/.well-known/did.json` on day 1.
- *Sorcha-hosted only, no `did:web` form at all*: rejected. Loses federation; `did:sorcha:org` requires Sorcha-specific tooling to resolve.

**Migration to BYO-domain (future)**: when an org upgrades, the old A-form DID stays resolvable indefinitely via the original endpoint. New BYO DID document declares the old form via `alsoKnownAs`; cross-resolution (R4) makes the equivalence safe.

---

## R3 — Hybrid kid scheme (D3)

**Decision**: DID document publishes **two `VerificationMethod` entries per active key** with identical key material — versioned (`#vc-issuance-{n}`) and thumbprint (`#{rfc7638-base64url}`). Issuer signs with versioned-format kid by platform default; per-org override slot reserved (not exposed in v1 UI). Verifier matches kid via exact-string-first, thumbprint-fallback.

**Rationale**:
- Versioned kids are human-readable in logs and forensic traces (`#vc-issuance-2` says "second issuance key" without inspecting the doc).
- Thumbprint kids are what standards-purist external implementations tend to publish (matches `did:key`-style conventions).
- Dual-publishing both forms in the DID document costs ~200 bytes per active VM. Trivial.
- Verifier-side tolerance (exact match → thumbprint fallback) means inbound credentials from external issuers using either form just work.
- The platform-default + per-org override pattern is the cheap forward-compat trick: standards-purist orgs that demand thumbprint-only signing can be accommodated without changing platform behaviour.

**Alternatives considered**:
- *Versioned only*: rejected. Forces external standards-purist verifiers to resolve our specific kid format; loses interop.
- *Thumbprint only*: rejected. Loses readability in logs, makes rotation trace hostile.
- *Single VM per key with computed thumbprint matching at verify time*: considered. Verifier-side complexity equivalent (still needs thumbprint computation for fallback). Doc shape is one entry instead of two — saves 200 bytes per key. Lost: external verifiers that only do exact-string matching against published VMs would fail on credentials we sign with the absent form. Rejected for interop simplicity.
- *Per-credential-type kid style*: rejected as overkill (item 3 in design doc's "open questions"). Per-platform default with per-org override is the right granularity.

---

## R4 — Cross-resolved `alsoKnownAs` with key-material verification (D4)

**Decision**: New method `IDidResolverRegistry.ResolveWithAlsoKnownAsAsync(did, ct)`. Resolves primary DID, walks `alsoKnownAs`, resolves each linked DID, compares `VerificationMethod` key material across all linked documents, returns merged document only when same public key appears in every link. Reject on mismatch or unreachable link.

**Rationale**:
- `alsoKnownAs` blindly trusted is a privilege-escalation primitive: anyone who controls the `did:web` document hosting can claim equivalence to any organisation and forge credentials in their name.
- Cross-resolution anchors the equivalence to whichever side is harder to compromise. `did:sorcha:org:*` is anchored to wallet-derived keys (custodial under Feature 083); `did:web:*` is anchored to domain control. Cross-resolving from one to the other requires both to be compromised simultaneously to forge a credential.
- Cost: one extra DID resolution per *issuer* per cache window, not per presentation. Cache absorbs the overhead in steady state.
- Packaging in the registry (rather than each consumer) means future B (validator-side at seal time) inherits the behaviour without re-implementation.

**Alternatives considered**:
- *Trust blindly*: rejected. Documented as a known privilege-escalation path; security review would not accept it.
- *Signed `alsoKnownAs` assertions*: considered. Cryptographically cleaner (each side cryptographically attests to the equivalence); does not require the second resolution at verify time. Rejected because no W3C primitive exists — would require a Sorcha-specific extension that no external verifier would honour. Cross-resolution gives equivalent security with zero standards drift.
- *DNS-anchored proofs (e.g., DNSSEC TXT records on the `did:web` domain referencing the `did:sorcha` form)*: rejected. Adds DNS infrastructure dependency to a feature that should be HTTP-only.

**Caching default**: cache key = canonical primary DID; TTL 1h for `did:web`, on-event-invalidate for `did:sorcha:*` (subscribed to `transaction:confirmed` Redis stream — the same mechanism used by Feature 119 for seal-aware ordering). `did:key` is cache-forever (key embedded in identifier; offline; no refresh possible). Cache layer lives in `DidResolverCache` (new), called from the new `ResolveWithAlsoKnownAsAsync` method.

---

## R5 — Pre-production posture: default-on at ship, no warn-only soak (D5)

**Decision**: `IssuerSignature:Required = true` by default at ship. Per-register `RegisterPolicy.requireIssuerSignature` slot reserved (not read at v1). No multi-week warn-only soak window. Walkthrough suite green with enforce-on is the ship gate.

**Rationale**:
- Sorcha is in pre-production. There are no in-flight credentials issued under the prior accept-everything default that need a deprecation window. Default-on at ship is therefore safe (Assumption #1 in spec).
- The walkthrough suite is a representative test surface (Assumption #4). Walkthroughs green with enforce-on prove all in-tree flows verify cleanly.
- The default-off-then-flip pattern exists to manage external customer impact during a behaviour change. With no external customers, it adds operational complexity without value.
- Three-way failure-mode logging (FR-003) gives developer-time triage signal during dev/staging without the warn-only mode.

**Alternatives considered**:
- *Default-off, multi-week warn-only soak, deliberate flip-to-true*: appropriate post-first-external-participant. Rejected for pre-production v1 — adds a flip-event that has to be remembered. Recorded in spec's Out of Scope as the right pattern for *future* features.
- *Per-register-only enforcement (no global)*: rejected. v1 simplification — global flag is one config; per-register reads are deferred to Future B alongside the validator-side rules. The slot is reserved (FR-020) so the migration is zero-cost.

**Forward note (per spec)**: the no-soak posture is conditional on pre-production status. When the platform onboards its first external participant who issues credentials, any subsequent feature that materially changes verification behaviour should revert to the warn-only-then-flip pattern.

---

## R6 — Open trust + per-action allowlist, slots reserved (D6)

**Decision**: v1 accepts any resolvable issuer DID. Per-action allowlist via `CredentialRequirement.AcceptedIssuers` (already implemented and enforced in three places). Per-register `RegisterPolicy.permittedIssuers` slot reserved on genesis (not read at v1). New matching logic: `alsoKnownAs`-equivalent issuer match in `CredentialMatcher`.

**Rationale**:
- Domain control = identity is the trust model the public web's TLS infrastructure uses. Good enough for v1.
- Per-action allowlist is the *operational* security gate today regardless of platform-level policy — high-stakes actions (Assured Identity verification, regulated finance flows) pin to specific accredited issuer DIDs in the blueprint. This works whether or not Sorcha curates a platform allowlist.
- The publish-time `OPEN_CREDENTIAL_ISSUER` warning already nudges blueprint authors to pin issuers for high-stakes credentials.
- Equivalence-aware matching is the one new bit of matching logic: if `acceptedIssuers: ["did:sorcha:org:ws1q..."]` and the credential's `iss=did:web:...` resolves to a doc with that DID in `alsoKnownAs`, the match succeeds.

**Alternatives considered**:
- *Platform-curated allowlist*: rejected. Sorcha curating "known-good issuers" is operational ownership Sorcha doesn't want, and per-action pinning is a finer-grained control that obviates it.
- *Per-register-only allowlist (no per-action)*: rejected. Coarser than needed; multiple actions in a register may legitimately accept different issuer sets.
- *Per-credential-type allowlist*: rejected as overkill. Per-action already covers it.

---

## R7 — Caching strategy details (Q1, Q7 from design doc)

**Decision**: Per-method TTL with on-event invalidation for platform-internal DIDs. Defaults:

| Method | Cache TTL | Invalidation trigger |
|---|---|---|
| `did:web` | 1h | TTL only |
| `did:sorcha:*` | infinite (within process lifetime) | `transaction:confirmed` Redis stream subscription, plus `IOrgDidDocumentService` direct invalidation on local key events |
| `did:key` | infinite | None (key embedded in identifier) |

`did.json` HTTP `Cache-Control` header: `public, max-age=21600` (6h) — matches the existing pattern from Feature 114's status-list endpoint. Explicit invalidation on key events propagates faster than max-age via gateway cache-purge.

**Rationale**:
- 1h `did:web` TTL balances freshness against external server load. Production deployments can tighten this; the 1h ceiling matches what the design doc deferred.
- `did:sorcha:*` events are local; subscribing to `transaction:confirmed` (the same mechanism used by Feature 119's seal coordinator) invalidates within milliseconds of the on-platform event.
- `did:key` cannot be invalidated — the identifier *is* the key.
- 6h `Cache-Control` for the public `did.json` is the conservative ceiling for static published documents; explicit purge on key change makes the lower bound near-zero.

**Alternatives considered**:
- *No caching*: rejected — every verification incurs a network round trip. Fails SC-009 (steady-state latency).
- *Cache-forever with explicit invalidation only*: rejected for `did:web` — external server's published document may change without any signal Sorcha can subscribe to.
- *Aggressive prefetch (warm cache for known issuers)*: deferred to a hardening pass. Not required for v1.

---

## R8 — DID document version metadata (Q2)

**Decision**: Defer to a later feature. v1 single-key-per-org has no version story; multi-key rotation lands metadata incrementally.

**Rationale**: W3C DID Core defines `versionId`, `versionTime`, `nextUpdate` in DID document metadata. These matter when verifiers need to resolve historical states ("what was the document at the time this credential was issued?"). v1 always-active-or-revoked semantics don't need it — every resolution returns the current state, and credentials signed by a revoked key are rejected unconditionally.

When manual rotation lands (Phase 2 already handles single-step rotation), the DID document already carries old + new VMs in the same `verificationMethod` array. Version metadata can be added incrementally to a future hardening pass.

---

## R9 — Status list signing key (slot 109) in DID document (Q3)

**Decision**: Add as additive `VerificationMethod` in the published DID document, with verification relationship `assertionMethod` (matches its purpose — signing status list JWTs that assert per-bit revocation state).

**Rationale**:
- Feature 114 already derives slot 109 (`sorcha:citizen-status-signing`) per-org for status list JWT signing. Surfacing it in the DID document means external verifiers can verify status-list signatures via the same resolver mechanism as credential signatures.
- Additive: doesn't conflict with the issuance VM (slot 1, `KeyUsage.VCIssuance`) being in the same document under its own `assertionMethod` entry.
- Cost: one extra dual-VM pair per org that issues citizen-PWA-targeted credentials. ~400 bytes.

**Implementation note**: `IOrgDidDocumentService.RegenerateAsync` reads both slot-1 and slot-109 keys via `IKeyManagementService` when present; emits both. If either is absent (org has issuance but no citizen-PWA credentials, or vice versa), only the present one appears.

---

## R10 — HAIP OID4VCI integration consistency (Q4)

**Decision**: Phase 6 walkthrough validation includes confirming that OID4VCI `credential_issuer` metadata declares the same DID as the issued credential's `iss` claim. No new code in this feature; validation only.

**Rationale**: Feature 097 (OID4VCI issuer) already exists. The OID4VCI metadata endpoint already declares the issuer's DID. The integration is "the issuer is the same entity in both surfaces" — verifiable end-to-end by issuing through OID4VCI and presenting through the verifier with enforce-on.

If the walkthrough surfaces a mismatch, a small alignment patch lands in this feature's Phase 6. If clean, no work needed.

---

## R11 — Demo-mint flow disposition (Q5)

**Decision**: `JwkRegistryIssuerKeyResolver` retained in `Sorcha.Citizen.Verifier` for tests + `DemoMintEndpoint`. Production `IIssuerKeyResolver` is `DidResolverBackedIssuerKeyResolver`. `DemoMintEndpoint` is documented as test-only; not used in production.

**Rationale**:
- The demo flow generates per-mint issuer keys on-the-fly. Wiring it through real DID resolution would require publishing per-mint DID documents, which is meaningless for a demo.
- `JwkRegistryIssuerKeyResolver` already exists as the right level of abstraction for an in-memory, test-only key store.
- Production wiring (`DidResolverBackedIssuerKeyResolver` as the registered `IIssuerKeyResolver`) and test/demo wiring (`JwkRegistryIssuerKeyResolver` registered in test composition or behind a configuration flag) coexist cleanly.

**Alternatives considered**:
- *Replace registry with a localhost-served `did:web` test fixture*: rejected. Adds test infrastructure (a test HTTP server, a certificate for HTTPS, etc.) without value beyond what the registry already provides.
- *Delete the registry, force tests to use real DID resolution*: rejected. Tests would need to publish documents before each test setup; large overhead for marginal "more realistic" testing.

---

## R12 — Where does `Organization.DefaultKidStyle` live structurally (Q6)

**Decision**: Add as a top-level property on `Organization`, not a sub-entity. Default value `KidStyle.Versioned`. Type: enum.

**Rationale**:
- The setting is per-org, single-valued, and rarely changed. A sub-entity for one field is over-design.
- Other per-org configuration on `Organization` (branding, billing fields) lives at the top level — consistent.
- If future per-org credential settings accumulate (e.g., default expiry, default disclosure scope), they may move into a settings sub-entity in a later refactor. Not a v1 concern.

---

## R13 — Sorcha-hosted `did.json` URL stability across BYO upgrade (Q8)

**Decision**: Sorcha-hosted DID documents stay reachable indefinitely. No sunset.

**Rationale**:
- Already-issued credentials whose `iss` points at the Sorcha-hosted form must remain verifiable forever (until they expire by their own `exp` claim).
- `alsoKnownAs` linkage between old (Sorcha-hosted) and new (BYO) documents lets cross-resolution validate either direction.
- Storage cost is negligible: one JSON document per org that ever upgraded, served as static content.
- The discipline is: **DIDs are forever once published**. This is a property of the W3C model, not a Sorcha-specific commitment — and breaking it would silently invalidate historical credentials, which is exactly what `alsoKnownAs` exists to prevent.

---

## Summary

All 13 research items resolved. No NEEDS CLARIFICATION markers remain. The architectural decisions in the design doc are validated against alternatives; no decision was overturned during research.

**Phase 1 prerequisites**: complete. Proceed to data-model and contracts.
