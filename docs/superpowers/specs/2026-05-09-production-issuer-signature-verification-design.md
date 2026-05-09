# Production Issuer Signature Verification — Design Document

**Date:** 2026-05-09
**Author:** Stuart Fraser, with Claude Opus 4.7 (1M context)
**Status:** Design complete — ready for spec
**Target feature:** 120 (next available after 119-presentation-seal-ordering)
**Companion docs:**
- `Validator2/2026-05-09-programmable-validation-thesis.md` (shared memory)
- `Validator2/2026-05-09-did-resolution-and-issuer-sig-companion.md` (shared memory)

---

## Executive summary

Sorcha currently accepts any credential as authentic at presentation time. The verifier's `IIssuerKeyResolver` is `OptOutIssuerKeyResolver` (returns null) by default; only a demo-only `JwkRegistryIssuerKeyResolver` exists alongside it for the `/verify/demo/mint` flow. There is no production path that resolves an issuer's DID to a public key and verifies the credential's JWS signature against it.

This design closes that gap by:

1. **Standardising on the W3C-shaped DID resolver stack** (`IDidResolver` / `IDidResolverRegistry`), retiring the legacy `IDIDResolver` interface.
2. **Publishing every Sorcha-hosted org as both `did:sorcha:org:{addr}` (primary) and `did:web:{platform-domain}:orgs:{orgId}` (federation)**, linked via `alsoKnownAs`.
3. **Cross-resolving `alsoKnownAs`** at the registry layer with key-material verification — preventing impersonation via compromised `did:web` documents.
4. **Shipping a `DidResolverBackedIssuerKeyResolver`** that the verifier (and any other point-of-use: wallet sync surface, register inbox projector) consumes.
5. **Reserving forward-compat slots** on the genesis control record (`RegisterPolicy.requireIssuerSignature`, `RegisterPolicy.permittedIssuers`) so Future B (chain-authoritative issuer-sig in `VAL_CRED_*` validator rules) lands without schema migration.
6. **Defaulting to enforce-on at ship**, leveraging Sorcha's pre-production status — the walkthrough suite passing with enforcement on is the ship gate, in lieu of a multi-week warn-only soak window.

This is **Future A** in the sequencing companion: verifier-side enforcement. Future B (validator authoritative on issuer-sig at seal time) remains explicitly deferred. The work shipped here is forward-compatible: the resolver code lifts into the validator unchanged when Future B is triggered.

---

## Problem statement

### Current state

| Component | Status |
|---|---|
| `OptOutIssuerKeyResolver` | Default. Returns null. Verifier accepts on holder→device chain alone with `RequireIssuerSignature: false`. |
| `JwkRegistryIssuerKeyResolver` | Demo-only. In-memory map populated by `DemoMintEndpoint` per-mint. Used by the `/verify/demo/mint` test flow. |
| `IDidResolver` (W3C-shaped) | Live. `SorchaDidResolver`, `KeyDidResolver`, `WebDidResolver` registered via `IDidResolverRegistry`. Consumed by HAIP and Wallet for VP verification but **not by the issuer-key resolver**. |
| `IDIDResolver` (legacy) | Single consumer (`Sorcha.Register.Service/Program.cs:205`). Returns flat `{PublicKey, Algorithm}` shape. Predates W3C work. Migration debt. |
| Org DID documents | Not published. `did:sorcha:org:*` resolves dynamically via `IWalletServiceClient`; `did:web:*` for orgs does not exist at all. |
| `alsoKnownAs` | Not emitted by `SorchaDidResolver`. Not cross-resolved anywhere. |
| `RequireIssuerSignature: true` | Tripwire — every presentation fails because no resolver returns a key. |

### Gap

The verifier is one OptOut flag-flip away from accepting forged credentials. Until a `DidResolverBackedIssuerKeyResolver` exists, that flip is impossible without breaking everything. Until org DID documents are published with surfaced issuance keys, even a working resolver has nothing to resolve to.

### Why now

Sorcha's near-term focus is real-world adoption (per the programmable-validation thesis). Production credential flows — Feature 106 register-native credentials, Feature 097 OID4VCI, Feature 107 Assured Identity — all assume issuer signature is meaningfully verified. They cannot ship to production with the OptOut default.

---

## Goals and non-goals

### Goals

- **Production-shippable issuer signature verification** for all SD-JWT VC presentations, regardless of whether the credential was issued via OID4VCI, register-native, or demo-mint.
- **Single canonical DID resolver stack** (W3C-shaped). Legacy `IDIDResolver` retired.
- **Federation interop**: Sorcha-issued credentials verifiable by standards-compliant external wallets and verifiers, via `did:web`.
- **Forward-compat** for Future B (chain-authoritative `VAL_CRED_*`). Schema slots reserved; resolver code structured for lift-and-shift into the validator.
- **Pre-production ship posture**: enforce-on at ship. Walkthrough suite green is the gate.

### Non-goals

- **Validator-side issuer-sig at seal time** — Future B. Deferred until adoption pressure justifies the determinism cost.
- **BYO-domain `did:web`** — orgs publishing their own `did.json` on a domain they control. Deferred phase; Sorcha-hosted form is the v1 default.
- **Auto-rotation schedules** — manual rotation via governance op only in v1.
- **Additional DID methods** (`did:ethr`, `did:ion`, etc.) — additive; the registry pattern means they drop in without touching this work.
- **Signed `alsoKnownAs` equivalence assertions** — cryptographically cleaner than cross-resolution but requires a Sorcha-specific extension. Cross-resolution wins on standards interop.
- **Per-credential-type `requireIssuerSignature` policy** — per-action allowlist via `acceptedIssuers` already provides finer-grained control.
- **Validator gaining `IDidResolverRegistry` access** — Future B concern.

---

## Locked product decisions (the brainstorm output)

The six decisions taken in the 2026-05-09 brainstorm session, captured here so the spec can treat them as starting axioms.

### D1 — Org DID hosting

**Sorcha-hosted, path-based.** Default form `did:web:{platform-domain}:orgs:{orgId}` resolves to `https://{platform-domain}/orgs/{orgId}/did.json`. Static JSON behind the gateway, CDN-cacheable. Document regenerated on key events (key derived, rotated, revoked).

BYO-domain (`did:web:acme.com`) deferred. When upgrading, the old A-form DID stays resolvable indefinitely; both documents declare each other via `alsoKnownAs`.

### D2 — Issuance key lifecycle

**Lazy derivation** at first credential-issuance attempt (slot 1, `KeyUsage.VCIssuance`, BIP44 path under Feature 083). Orgs that never issue credentials never carry an issuance key.

**Manual rotation only in v1.** Governance op rotates; old key remains in the DID document as a `VerificationMethod` until all credentials it signed have expired.

**Compromise revocation = governance op with admin quorum.** Same pattern as Feature 086 `RotateValidatorKey`. Proto-rule code `VAL_CRED_GOV_001` reserved.

### D3 — `kid` convention

**Hybrid: dual-publish + tolerant-verify.**

- DID document publishes **two `VerificationMethod` entries per active key** with identical key material — one versioned (`#vc-issuance-{n}`) and one thumbprint-keyed (`#{rfc7638-thumbprint}`).
- Issuer-side `kid` defaults to **versioned** per platform configuration. Per-org override slot reserved on the `Organization` model (`KidStyle` enum: `Versioned` | `Thumbprint`), not exposed in v1 UI.
- Verifier matches kid via **exact string match first**, then **thumbprint fallback** — handles credentials from external issuers whose DID docs only carry one form.

Doc cost: ~200 bytes per active VM. Trivial.

### D4 — `alsoKnownAs` trust model

**Cross-resolve and verify key material.** New method on `IDidResolverRegistry`:

```csharp
Task<DidDocument?> ResolveWithAlsoKnownAsAsync(string did, CancellationToken ct = default);
```

Resolves the primary DID, walks `alsoKnownAs`, resolves each linked DID independently, compares `VerificationMethod` key material across the two documents, returns the merged document only if the same public key appears in both. Reject (return null) on mismatch.

Cached at the registry layer. TTLs:
- `did:web` — 1h default, configurable
- `did:sorcha:*` — on-event-invalidate via `transaction:confirmed` Redis stream subscription
- `did:key` — infinite (offline, no refresh possible)

This packaging means the verifier asks the registry once and gets a trustworthy result. Future B (validator-side) inherits the cross-resolution behaviour for free.

### D5 — `RequireIssuerSignature` rollout

**Per-register slot reserved + global flag honoured + default-on at ship.**

- Schema addition to genesis control record: `RegisterPolicy.requireIssuerSignature: bool?` (reserved, not read in v1).
- v1 reads a single platform config: `IssuerSignature:Required`, default `true`.
- **No warn-only soak window.** Pre-production status means there are no legacy credentials to nurse. Walkthrough suite green with enforce-on is the ship gate.
- **Three-way failure-mode logging** from day one:
  1. `iss` DID does not resolve
  2. DID resolves but no `VerificationMethod` matches the JWS `kid`
  3. `VerificationMethod` matches but signature does not verify

Spec records that the default-on-no-soak posture is conditional on pre-production status; the warn-only-then-flip pattern remains the right move for any future feature that materially changes verification behaviour after first external participant.

### D6 — `did:web` trust bootstrap

**Open trust + per-action allowlist.** Already implemented:

- `CredentialRequirement.AcceptedIssuers : IEnumerable<string>?` exists at `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialRequirement.cs:28`.
- Enforced at three points: `CredentialMatcher.cs:51-52`, `PresentationRequestService.cs:364-365`, `PresentationLifecycleService.cs:133`.
- Publish-time warning `OPEN_CREDENTIAL_ISSUER` flags empty allowlists.

New schema slot reserved for Future B: `RegisterPolicy.permittedIssuers: string[]?` (register-wide allowlist, not read in v1).

**One new bit of matching logic:** when `alsoKnownAs` cross-resolution succeeds, the matcher accepts either of the linked DIDs. If `acceptedIssuers: ["did:sorcha:org:ws1q..."]` and the credential's `iss=did:web:acme.com` resolves to a doc with `alsoKnownAs: ["did:sorcha:org:ws1q..."]`, the match succeeds.

---

## Architecture

### Component diagram

```
┌────────────────────────────────────────────────────────────────────┐
│ Sorcha.Citizen.Verifier  /  Sorcha.Wallet.Service  /  HAIP         │
│   VerifiablePresentationValidator                                  │
│   PresentationRequestService                                       │
│   InboundCredentialDetector                                        │
│       │                                                             │
│       ▼                                                             │
│   IIssuerKeyResolver  ◄── DidResolverBackedIssuerKeyResolver  NEW  │
│                            │                                        │
│                            ▼                                        │
│                       IDidResolverRegistry                          │
│                          .ResolveAsync                              │
│                          .ResolveWithAlsoKnownAsAsync       NEW    │
│                            │                                        │
│              ┌─────────────┼─────────────┐                          │
│              ▼             ▼             ▼                          │
│       SorchaDidResolver  WebDidResolver  KeyDidResolver             │
│       (enhanced)         (existing)      (existing)                 │
│              │                                                      │
│              ▼                                                      │
│       IWalletServiceClient                                          │
│              │                                                      │
│              ▼                                                      │
│       Wallet Service                                                │
└────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────┐
│ Sorcha.Tenant.Service                                               │
│   IOrgDidDocumentService  ◄── NEW                                   │
│     - Generates and stores did:sorcha:org and did:web docs          │
│     - Regenerates on key events                                     │
│     - Serves did.json at /orgs/{orgId}/did.json                     │
└────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────┐
│ Sorcha.Wallet.Service                                               │
│   IIssuanceKeyService  ◄── NEW                                      │
│     - Lazy slot-1 derivation on first issuance                      │
│     - Rotation governance op handler                                │
│     - Revocation governance op handler                              │
└────────────────────────────────────────────────────────────────────┘
```

### New components

| Component | Project | Purpose |
|---|---|---|
| `DidResolverBackedIssuerKeyResolver` | `Sorcha.Citizen.Verifier` (initial) — likely promoted to a shared abstraction project later for wallet inbox / register projector reuse | Resolves credential `iss` to a public key via `IDidResolverRegistry.ResolveWithAlsoKnownAsAsync`, matches `kid` via exact-then-thumbprint, returns `IssuerPublicKey` |
| `IDidResolverRegistry.ResolveWithAlsoKnownAsAsync` | `Sorcha.ServiceClients.Http.Did` | Cross-resolves `alsoKnownAs`, verifies key-material match, returns merged DID document or null |
| `IOrgDidDocumentService` / `OrgDidDocumentService` | `Sorcha.Tenant.Service` | Builds and stores DID documents for orgs; regenerates on key events; exposes the public `did.json` endpoint |
| `IIssuanceKeyService` / `IssuanceKeyService` | `Sorcha.Wallet.Service` | Lazy slot-1 derivation, rotation, revocation; publishes key changes to `IOrgDidDocumentService` |
| `OrgDidDocumentEndpoints` | `Sorcha.Tenant.Service` | `GET /orgs/{orgId}/did.json` — public, anonymous, CDN-cacheable |

### Enhanced components

| Component | Change |
|---|---|
| `SorchaDidResolver` | Surface issuance key as second `VerificationMethod` in `assertionMethod`; emit `alsoKnownAs` linking to the org's `did:web` form; emit dual VMs (versioned + thumbprint) per active key. |
| `WebDidResolver` | No code changes — already ships with SSRF protection, document-id roundtrip check, HTTPS-only, 5s timeout. Configuration: ensure `DidResolver:AllowPrivateAddresses` is appropriately set in test vs prod. |
| `DidResolverRegistry` | Add `ResolveWithAlsoKnownAsAsync` method. Add per-issuer cache keyed by canonical DID. Add Redis-stream subscription for `did:sorcha:*` invalidation. |
| `VerifiablePresentationValidator` (verifier) | Wire `DidResolverBackedIssuerKeyResolver` as the production `IIssuerKeyResolver`. `JwkRegistryIssuerKeyResolver` retained for tests + demo-mint. |
| `CredentialMatcher` (Wallet) | Add `alsoKnownAs`-equivalent issuer matching: when `acceptedIssuers` contains a DID and the candidate credential's `iss` resolves with `alsoKnownAs` linking to it, treat as match. |
| `RegisterControlRecord` model | Add `RegisterPolicy.requireIssuerSignature: bool?` and `RegisterPolicy.permittedIssuers: string[]?`. JSON-null-ignored. |
| `Organization` model | Add `KidStyle: KidStyle` enum (default `Versioned`). Not exposed in v1 admin UI. |

### Retired components

| Component | Action |
|---|---|
| `IDIDResolver` (`Sorcha.Register.Core.Services`) | Migrate single consumer in `Sorcha.Register.Service/Program.cs:205` to `IDidResolverRegistry`. Delete `IDIDResolver`, `DIDResolver`, `DIDResolutionResult`. Update specs/031 + specs/039 references. |

---

## Data flow

### Issuance flow (server-mediated, slot 1 lazy)

1. Org admin or user triggers credential issuance for the first time.
2. `IIssuanceKeyService.GetOrDeriveAsync(orgId)` checks for existing slot-1 key. If absent, derives via `IKeyManagementService.DeriveKeyAtPathAsync` (Feature 083 slot 1, BIP44 path, context `sorcha:vc-issuance`).
3. Issuance key material persisted (encrypted, custodial mode).
4. `IOrgDidDocumentService.RegenerateAsync(orgId, KeyEventReason.IssuanceKeyDerived)` rebuilds and stores both DID documents (`did:sorcha:org:*` and `did:web:platform:orgs:{orgId}`).
5. Credential JWS signed with the issuance key. Header `kid = did:sorcha:org:{addr}#vc-issuance-1` (or thumbprint per org's `KidStyle`).
6. Credential issued. `iss` claim = `did:sorcha:org:{addr}` (canonical primary form).

### Verification flow (presentation time)

1. Verifier receives SD-JWT VC presentation. Parses JWS header → `iss`, `kid`.
2. Verifier calls `IIssuerKeyResolver.ResolveAsync(iss)`.
3. `DidResolverBackedIssuerKeyResolver` calls `IDidResolverRegistry.ResolveWithAlsoKnownAsAsync(iss)`.
4. Registry resolves `iss`. If `alsoKnownAs` is non-empty, resolves each linked DID. Verifies key-material match across documents. Returns merged document or null.
5. `DidResolverBackedIssuerKeyResolver` matches `kid` against `verificationMethod[].id` (exact match first, thumbprint fallback). Returns the matching `JsonWebKey`.
6. Verifier verifies the JWS signature against the returned key.
7. Verifier matches the credential against per-action `acceptedIssuers`. Match succeeds if `iss` ∈ `acceptedIssuers` OR any DID in the resolved document's `alsoKnownAs` ∈ `acceptedIssuers`.

### Cross-resolution example

Credential header: `iss=did:web:platform:orgs:abc-123`, `kid=did:web:platform:orgs:abc-123#vc-issuance-1`.

```
1. ResolveWithAlsoKnownAsAsync("did:web:platform:orgs:abc-123")
2. WebDidResolver fetches https://platform/orgs/abc-123/did.json
   Returns: {
     id: "did:web:platform:orgs:abc-123",
     alsoKnownAs: ["did:sorcha:org:ws1qABC..."],
     verificationMethod: [{ id: "...#vc-issuance-1", publicKeyMultibase: "z6MkXYZ..." }, ...]
   }
3. Registry walks alsoKnownAs. Resolves "did:sorcha:org:ws1qABC..."
4. SorchaDidResolver returns: {
     id: "did:sorcha:org:ws1qABC...",
     alsoKnownAs: ["did:web:platform:orgs:abc-123"],
     verificationMethod: [{ id: "...#vc-issuance-1", publicKeyMultibase: "z6MkXYZ..." }, ...]
   }
5. Registry verifies same key material in both. Match.
6. Returns merged document.
```

If step 5 mismatches (e.g. compromised `did:web` document with attacker's key), registry returns null. Verifier rejects.

---

## Genesis schema additions

### `RegisterControlRecord.RegisterPolicy`

```jsonc
"registerPolicy": {
  // ... existing fields ...
  "requireIssuerSignature": true,                  // optional; null => use platform default
  "permittedIssuers": ["did:sorcha:org:ws1q..."]   // optional; null/empty => any resolvable issuer
}
```

Both fields JSON-null-ignored. v1 does not read them. Future B reads both at validator seal time.

### `Organization` model additions

```csharp
public class Organization
{
    // ... existing fields ...

    public KidStyle DefaultKidStyle { get; set; } = KidStyle.Versioned;
    // Not exposed in v1 admin UI. Slot reserved for standards-purist orgs.
}

public enum KidStyle { Versioned = 0, Thumbprint = 1 }
```

Default `Versioned` means platform-wide default applies. Slot reserved per D3.

---

## Phasing

| Phase | Scope | Cost estimate | Independent? |
|---|---|---|---|
| **0 — Cleanup** | Retire legacy `IDIDResolver`. Migrate `Sorcha.Register.Service/Program.cs:205` consumer to `IDidResolverRegistry`. Delete `IDIDResolver`, `DIDResolver`, `DIDResolutionResult`. Update spec references. | 1-2h | Yes — small standalone PR, can ship before anything else |
| **1 — DID document publishing** | `IOrgDidDocumentService` + `OrgDidDocumentEndpoints`. Generate both forms with `alsoKnownAs`. Dual-VM publishing template. Regeneration triggers on key events. Test: regenerated doc resolves cleanly via both `WebDidResolver` and `SorchaDidResolver`. | 2-3 days | Depends on Phase 0 |
| **2 — Issuance key lifecycle** | `IIssuanceKeyService` with lazy slot-1 derivation, rotation handler, revocation handler (governance op `VAL_CRED_GOV_001`). Wire `IOrgDidDocumentService.RegenerateAsync` calls on key events. | 2-3 days | Depends on Phase 1 |
| **3 — Resolver enhancements** | `SorchaDidResolver` enhancement: dual-VM publishing, `alsoKnownAs` emission, issuance-key VM surfaced. `IDidResolverRegistry.ResolveWithAlsoKnownAsAsync` with cross-resolution + caching + Redis-stream invalidation. Thumbprint-fallback matching helper. | 3-4 days | Depends on Phases 1+2 |
| **4 — Issuer key resolver + verifier wiring** | `DidResolverBackedIssuerKeyResolver` in `Sorcha.Citizen.Verifier`. Replace `OptOutIssuerKeyResolver` as production default. `JwkRegistryIssuerKeyResolver` retained for tests. Three-way failure-mode logging. Update `RequireIssuerSignature` default to `true`. | 2-3 days | Depends on Phase 3 |
| **5 — Genesis schema slots + matching logic** | `RegisterControlRecord.RegisterPolicy.requireIssuerSignature` + `.permittedIssuers` slots reserved. `Organization.DefaultKidStyle` slot reserved. `CredentialMatcher` accepts `alsoKnownAs`-equivalent issuer match. | 1-2 days | Depends on Phase 4 |
| **6 — Walkthroughs + integration** | Update walkthroughs: AssuredIdentity, TradeFinance, ConstructionPermit, SelfBuildHouse — confirm enforce-on green. Demo-mint flow updated to either use real DID resolution or stay as documented test escape via `JwkRegistryIssuerKeyResolver`. Document the AssuredIdentity action's optional `acceptedIssuers` pin as a hardening recommendation. | 1-2 days | Depends on Phase 5; gates ship |

Total: ~2-3 weeks for an engineer with codebase familiarity. Phase 0 can ship anytime independently; the rest is a sequential chain.

---

## Test strategy

### Pre-production ship gate

The walkthrough suite passing end-to-end with `RequireIssuerSignature: true` is the ship gate. Specifically:

- **AssuredIdentity walkthrough** — exercises `did:sorcha:org:*` issuance and `HaipExternalWallet` presentation. Closes the loop on Feature 107.
- **TradeFinance walkthrough** — confirms register-native credential flow (Feature 106) verifies under enforce-on.
- **At least one walkthrough exercising `did:web:*` resolution path** — likely a new variant of an existing walkthrough using the BYO-domain test fixture, or a dedicated federation walkthrough. Required to prove the `WebDidResolver` is on a hot path, not a parked code path.
- **AssuredIdentity 10/10 verification** (per session resume note for Feature 119) re-runs cleanly after this work merges.

### Unit + integration tests

| Surface | Coverage |
|---|---|
| `SorchaDidResolver` enhancement | Dual-VM publishing, `alsoKnownAs` emission, issuance-key VM surfacing, kid format validation |
| `WebDidResolver` (existing) | SSRF protection regression tests, document-id roundtrip, HTTPS-only enforcement |
| `DidResolverRegistry.ResolveWithAlsoKnownAsAsync` | Happy path (matching keys), mismatch (compromised document), missing alsoKnownAs (passthrough), TTL expiry, Redis-stream invalidation |
| `DidResolverBackedIssuerKeyResolver` | Exact-match kid, thumbprint-fallback kid, no match (return null), three failure modes correctly distinguished in logs |
| `CredentialMatcher` | `alsoKnownAs`-equivalent issuer matching, regression test for direct-DID match |
| `IOrgDidDocumentService` | Document regeneration on key events, JSON canonical form, public key embedding for both kid styles |
| `IIssuanceKeyService` | Lazy derivation, rotation flow, revocation flow, idempotency on first-issuance retries |

### Failure-mode logging instrumentation

Three counters on `Sorcha.Verifier.IssuerSignature` meter:

- `sorcha_verifier_issuer_did_unresolved_total` — `iss` does not resolve
- `sorcha_verifier_issuer_kid_unmatched_total` — DID resolves but no VM matches
- `sorcha_verifier_issuer_signature_failed_total` — VM matches but signature mismatch

Plus span `verifier.issuer-resolve` parented to `verifier.presentation` with tags `outcome ∈ {success, did-unresolved, kid-unmatched, signature-failed}`.

### Cache observability

Counters on `Sorcha.Did.Resolver` meter:

- `sorcha_did_resolver_cache_hit_total{method, kind}` — kind ∈ {primary, alsoKnownAs}
- `sorcha_did_resolver_cache_miss_total{method, kind}`
- `sorcha_did_resolver_cross_resolve_mismatch_total` — alsoKnownAs key material mismatch

---

## Forward-compat for Future B

The work shipped here lifts cleanly into validator-side issuer-sig at seal time when Future B is triggered.

| Element | Future B inheritance |
|---|---|
| `DidResolverBackedIssuerKeyResolver` | Same class, lifted into validator process. Same input/output contract. |
| `IDidResolverRegistry.ResolveWithAlsoKnownAsAsync` | Same method called from validator's seal path. |
| `RegisterPolicy.requireIssuerSignature` slot | Validator reads at seal time. Per-register policy lights up. |
| `RegisterPolicy.permittedIssuers` slot | Validator enforces issuer allowlist at seal time as `VAL_CRED_002`. |
| `VAL_CRED_GOV_001` (revoke issuance key) | Already a governance op in Phase 2; Future B adds the validator-side enforcement check. |
| Cross-resolution caching | Validator inherits the same cache semantics. Determinism question (replay) addressed in Future B's spec — likely by caching resolved keys alongside the seal. |

The validator does not gain network access or new external dependencies in v1. Future B's design conversation can start from "the resolver and verifier already work; the question is whether to call them at seal time" — not "we need to build resolution from scratch."

---

## Open questions deferred to coding time

The questions that don't block spec writing but want answers when the work starts:

| # | Question | Default if not addressed |
|---|---|---|
| 1 | Resolver cache TTLs (`did:web` 1h vs 6h vs 24h?) | 1h, configurable |
| 2 | DID document version metadata (`versionId`, `nextUpdate`) | Defer until rotation lands; v1 single-key has no version story |
| 3 | Status list signing key (slot 109, `sorcha:citizen-status-signing`) as separate `VerificationMethod` | Add as additive verification relationship in DID doc; not a v1 blocker |
| 4 | HAIP OID4VCI integration: ensure `credential_issuer` metadata declares same DID as `iss` | Verify in Phase 6 walkthrough validation |
| 5 | Demo-mint flow — keep `JwkRegistryIssuerKeyResolver` or wire `DidResolverBackedIssuerKeyResolver` with localhost test fixture? | Keep registry; document as test-only escape |
| 6 | Where does the `Organization.DefaultKidStyle` slot live structurally — main entity or settings sub-entity? | Settings sub-entity; cleaner for future per-org-config additions |
| 7 | `did.json` cache headers — what's the right `Cache-Control: max-age`? | 6 hours, with explicit invalidation on key events via cache-purge |
| 8 | Sorcha-hosted `did.json` URL stability across BYO-domain upgrade — keep old URL alive forever, or define a sunset? | Keep alive forever for verification of historical credentials |

---

## Pointers to current code

| Concept | Path |
|---|---|
| W3C resolver stack | `src/Common/Sorcha.ServiceClients.Http/Did/` |
| `IDidResolverRegistry` | `src/Common/Sorcha.ServiceClients.Http/Did/IDidResolverRegistry.cs` + `DidResolverRegistry.cs` |
| `SorchaDidResolver` | `src/Common/Sorcha.ServiceClients.Http/Did/SorchaDidResolver.cs` |
| `WebDidResolver` | `src/Common/Sorcha.ServiceClients.Http/Did/WebDidResolver.cs` |
| `KeyDidResolver` | `src/Common/Sorcha.ServiceClients.Http/Did/KeyDidResolver.cs` |
| Resolver DI registration | `src/Common/Sorcha.ServiceClients.Http/Extensions/HttpServiceCollectionExtensions.cs:101-120` |
| Legacy `IDIDResolver` to retire | `src/Core/Sorcha.Register.Core/Services/IDIDResolver.cs` + `DIDResolver.cs` |
| Legacy consumer | `src/Services/Sorcha.Register.Service/Program.cs:205` |
| `IIssuerKeyResolver` (verifier) | `src/Apps/Sorcha.Citizen.Verifier/Services/IIssuerKeyResolver.cs` |
| `DemoMintEndpoint` | `src/Apps/Sorcha.Citizen.Verifier/Endpoints/DemoMintEndpoint.cs` |
| `VerifiablePresentationValidator` | `src/Apps/Sorcha.Citizen.Verifier/Services/VerifiablePresentationValidator.cs` |
| `CredentialRequirement.AcceptedIssuers` | `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialRequirement.cs:28` |
| `CredentialMatcher` | `src/Services/Sorcha.Wallet.Service/Credentials/CredentialMatcher.cs:51-52` |
| `PresentationRequestService` allowlist | `src/Services/Sorcha.Wallet.Service/Services/PresentationRequestService.cs:364-365` |
| `RegisterControlRecord` (target for new slots) | `src/Common/Sorcha.Register.Models/RegisterControlRecord.cs` |
| `Organization` model (target for `DefaultKidStyle`) | `src/Services/Sorcha.Tenant.Service/Models/Organization.cs` |
| Feature 083 derivation paths | `src/Common/Sorcha.Cryptography/SorchaDerivationPaths.cs` |
| Feature 086 `RotateValidatorKey` (precedent for `VAL_CRED_GOV_001`) | `src/Services/Sorcha.Validator.Service/Services/RightsEnforcementService.cs` |
| `transaction:confirmed` Redis stream (cache invalidation) | `src/Common/Sorcha.Events/IEventSubscriber.cs` |
| AssuredIdentity walkthrough | `walkthroughs/AssuredIdentity/blueprints/driving-licence.json:103-115` |

---

## Provenance

This design is the output of the 2026-05-09 brainstorm session that immediately followed the programmable-validation thesis. The session walked six locked product decisions one at a time (D1-D6 above). The user's framing throughout: "we should be standards based" — meaning W3C-shaped DID resolution, federation-friendly `did:web` interop, and `alsoKnownAs` as the primary DID-equivalence mechanism rather than Sorcha-specific extensions.

The companion documents in shared memory (`Validator2/2026-05-09-programmable-validation-thesis.md`, `Validator2/2026-05-09-did-resolution-and-issuer-sig-companion.md`) capture the broader architectural framing — particularly the Future A vs Future B distinction and the alignment with the eventual `VAL_CRED_*` validator-side family.

Decisions D5 and D6 reserved schema slots on the genesis control record without reading them in v1. This is the deliberate "reserve-now-read-later" pattern: the schema migration cost is paid once, when the slots are added; the read-side cost is deferred to whenever Future B (or per-register-policy demand) lights them up.
