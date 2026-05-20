# Research: EUDI Credential Format & Unified Trust

**Feature**: 135-eudi-credential-format-trust | **Date**: 2026-05-20

All `/speckit.clarify` questions were resolved before planning; no `NEEDS CLARIFICATION` markers remain. This document records the decisions, the rationale, and the codebase facts they rest on.

---

## R1 — Where the unified trust evaluator lives (portability)

**Decision**: `ITrustEvaluator` + `ITrustResolverRegistry` live in `Sorcha.Blueprint.Engine/Credentials/`. Network-bound trust sources (CRL fetch, trust-list load, DID resolution, status-list fetch) are injected behind interfaces with in-memory/no-op variants.

**Rationale**: The engine credential library is **WASM-friendly and HttpClient-free** by design (the verifier runs in Blazor WASM for holder-side pre-flight). The skill `verifiable-credentials` is explicit: *"Do not put HttpClient calls inside CredentialVerifier. Revocation lookups go through IRevocationChecker; the WASM-friendly in-memory implementation is what makes offline verification bundles (feature 079) possible."* The evaluator must follow the same rule so both the engine path **and** the HAIP service path (`HaipPresentationVerifier`) consume one implementation (FR-007).

**Alternatives considered**:
- *Evaluator in HAIP service* — rejected: the engine path could not consume it without an HTTP hop, defeating WASM/offline use.
- *Two evaluators with a shared spec* — rejected: that is the exact divergence this feature exists to remove.

**Evidence**: `CredentialVerifier.cs:159` hardcodes `SignatureValid=false` and defers to "the service layer"; `HaipPresentationVerifier.cs` does the real work. `IRevocationChecker` is the established injectable-source pattern.

---

## R2 — Replacing AcceptedIssuers and _trustedRoots with a trust policy

**Decision**: Remove `CredentialRequirement.AcceptedIssuers` and `HaipPresentationVerifier._trustedRoots`/`AddTrustedRoot` outright (prerelease clean break). Trust is a `TrustPolicy` of `TrustSourceRef`s + an `anyOf`/`allOf` combinator + `MinAssuranceLevel`. Default when none declared (FR-026): legacy issuer identifiers → `did-allowlist` source; otherwise register/DID source at `Low`.

**Rationale**: The flat `AcceptedIssuers.Contains(iss)` check (`CredentialVerifier.cs:108`) is a string match with no signature or root concept; `_trustedRoots` is seeded only from static config (`HaipPresentationVerifier.cs:56`, HAIP `Program.cs:77-102`). Both are replaced by the policy so every path evaluates trust identically.

**Evidence**: `CredentialRequirement.cs` (`AcceptedIssuers`), `HaipPresentationVerifier.cs:32,56,319-320`.

---

## R3 — Trust sources behind a registry

**Decision**: `ITrustResolverRegistry` mirrors `IDidResolverRegistry`. Four `ITrustSourceResolver` kinds:

| Source kind | Built on (reused) | Vouches by |
|---|---|---|
| `register` | `IDidResolverRegistry` + `assertionMethod` gate + `IssuerEquivalenceMatcher` | DID resolves; key in `assertionMethod`; alsoKnownAs equivalence |
| `x509-tenant` | `ITrustProvider` root + `X509Chain` (`CustomRootTrust`) + CRL | leaf chains to tenant root, unrevoked |
| `trustlist` | **NEW** `ITrustListProvider` → `CustomTrustStore` | leaf chains to a trust-list root |
| `did-allowlist` | explicit DIDs + `ResolveWithAlsoKnownAsAsync` | issuer in allowlist (or equivalent) |

**Rationale**: Each maps onto an existing building block. `IssuerEquivalenceMatcher.IsAcceptedAsync(...)` already exists (Feature 120) but is **only wired into wallet-side `CredentialMatcher.cs:140` and `PresentationRequestService`** — never the engine verifier. The `register` source brings it into the unified path. `ITrustProvider` (spec 096) exposes `GetTrustAnchorAsync`/`GetOrgCertChainAsync`/`GetOrPublishCrlAsync`. `HaipPresentationVerifier.ValidateX5cChain` already shows the `X509Chain` + `CustomRootTrust` + CRL pattern to lift into the `x509-tenant`/`trustlist` resolvers.

**Evidence**: `IDidResolverRegistry.cs:15,36`; `IssuerEquivalenceMatcher.cs:32-36`; `ITrustProvider.cs:11-71`; `HaipPresentationVerifier.cs:298-333`.

---

## R4 — External trust list (LOTL): provider seam, operator snapshot shipped *(clarified)*

**Decision**: `ITrustListProvider` seam in `Sorcha.ServiceClients.Http/Trust`. Ship `OperatorSnapshotTrustListProvider` that loads an operator-supplied, versioned snapshot (file/config) with a snapshot id + freshness timestamp into `X509Chain.CustomTrustStore`. A live EU LOTL XML fetch/parse provider is a future implementation behind the same seam.

**Rationale (clarified A6/FR-017)**: No LOTL loader exists today; `_trustedRoots` is static config only. The snapshot model keeps verification offline-pinnable (the snapshot id + freshness land in `TrustEvidence`) and avoids adding an eIDAS trust-list XML parser + list-signature validation + network dependency in this feature. Lowest risk, clean extension point.

**Alternatives considered**: scheduled live LOTL fetch (deferred — heavier, network-bound); static config with no seam (rejected — no path to live LOTL, weak freshness model).

---

## R5 — Assurance level normalisation *(clarified)*

**Decision**: `AssuranceLevel { Low, Substantial, High }`. The level is the **assurance conferred by the vouching source** (operator-configurable per source), with an **upward-only override** from an explicit credential assurance claim where the source supports it. Absent any signal → `Low`. Policy `MinAssuranceLevel` rejects anything below (FR-012).

**Rationale (clarified A4)**: Most current credentials carry no LoA claim; deriving purely from a claim would make assurance unusable. Source-tier mapping reflects how strongly the source actually vouches; the upward-only override prevents a credential from self-asserting a higher level than its source supports. Fail-safe (defaults to lowest).

---

## R6 — Unify the two status-list checkers

**Decision**: New `IStatusListChecker` abstraction. `BitstringStatusListChecker` (W3C, currently `IRevocationChecker`) and `IetfTokenStatusListChecker` (IETF, currently standalone) both implement/adapt to it. Every verification path checks revocation through the one seam, fail-closed by default. mdoc revocation rides the IETF checker (R9).

**Rationale**: Today they share **no interface** — `BitstringStatusListChecker` implements `IRevocationChecker`; `IetfTokenStatusListChecker.CheckBitAsync(...)→StatusListBit` is standalone. Unifying removes the duplicated W3C-vs-IETF branching in `HaipPresentationVerifier.CheckStatusAsync` and gives the engine path real revocation parity. `RevocationCheckPolicy.FailClosed` is already the default (Feature 093) — preserve it.

**Evidence**: `BitstringStatusListChecker.cs:13,23,42`; `IetfTokenStatusListChecker.cs:25,44`; `HaipPresentationVerifier.cs:351-400`.

---

## R7 — mso_mdoc on BCL only

**Decision**: Implement `mso_mdoc` with `System.Formats.Cbor` + `System.Security.Cryptography.Cose` (pin Cose **10.0.8**). No third-party CBOR/COSE library. Add a small `CoseX5Chain` helper for the x5chain header (label 33) since BCL has no named constant.

**Rationale (verified by probe)**: Both types resolve on `net10.0` and round-trip; `CoseSign1Message` supports an arbitrary unprotected header via `new CoseHeaderLabel(33)` and `CoseHeaderValue.FromEncodedValue(...)`, surviving sign→decode→verify. `System.Security.Cryptography.Cose` 10.0.0 carries advisory NU1903 — pin 10.0.8; `System.Formats.Cbor` 10.0.0 is clean. Neither is referenced in `Directory.Packages.props` yet → two new central package entries.

**PQC posture (FR-006/SC-009)**: mdoc is additive and ES256/P-256-only at the *format* layer. It does not touch Sorcha-native signing or the `Multicodec` PQC fallback. Where a P-256 device/issuer key cannot be multibase-encoded, fall back to `publicKeyJwk` — exactly the `Multicodec.ToMultibasePublicKey(...) → null → JWK` rule the `verifiable-credentials` skill already mandates.

---

## R8 — mdoc CBOR/COSE structures (authoritative reference)

Tag-24 (`#6.24(bstr .cbor X)`) wrapping is load-bearing in **three** places — digests/signatures are computed over the *tagged outer bytes*; never re-serialise the inner map.

- **IssuerSigned** = `{ nameSpaces: { ns => [ IssuerSignedItemBytes+ ] }, issuerAuth: COSE_Sign1 }`. `IssuerSignedItemBytes = #6.24(bstr .cbor { digestID:uint, random:bstr≥16, elementIdentifier:tstr, elementValue:any })`.
- **MSO** (the `issuerAuth` payload, tag-24 wrapped) = `{ version:"1.0", digestAlgorithm:"SHA-256|384|512", valueDigests:{ ns => { digestID => bstr } }, deviceKeyInfo:{ deviceKey: COSE_Key }, docType:tstr, validityInfo:{ signed, validFrom, validUntil, ?expectedUpdate }, ?status }`.
- **DeviceResponse** = `{ version:"1.0", documents:[ { docType, issuerSigned, deviceSigned } ], status:uint }`. **DeviceSigned** = `{ nameSpaces: #6.24(bstr .cbor {}), deviceAuth: { deviceSignature:COSE_Sign1 } // { deviceMac:COSE_Mac0 } }` with **detached payload**.
- **DeviceAuthentication** (what is signed/MAC'd) = `#6.24(bstr .cbor [ "DeviceAuthentication", SessionTranscript, docType, DeviceNameSpacesBytes ])`.
- **x5chain** (RFC 9360): COSE label **33**, **unprotected** header, value = single `bstr` (one DER cert) or array of `bstr` leaf-first.

**Source caveat**: ISO clause numbers come from public cross-references / reference implementations, not the paywalled ISO PDF — confirm against a licensed ISO copy before finalising wire-level code.

---

## R9 — SessionTranscript for OpenID4VP & mdoc revocation *(clarified A5)*

**Decision**: Target **OpenID4VP 1.x hash-based handover**, not the legacy ISO 18013-7 Annex B `mdocGeneratedNonce` handover. `SessionTranscript = [ null, null, Handover ]`; `Handover = [ "OpenID4VPHandover", SHA-256(OpenID4VPHandoverInfoBytes) ]` where `OpenID4VPHandoverInfo = [ clientId, nonce, jwkThumbprint|null, responseUri ]`. (DC-API variant `OpenID4VPDCAPIHandover` over `[ origin, nonce, jwkThumbprint ]` is recognised but not the primary target.)

**mdoc revocation**: the MSO `status.status_list = { uri, idx }` (same shape as the CWT `status` claim and the SD-JWT IETF status list). Resolved through the unified `IStatusListChecker` (R6), fail-closed. This reuses `IetfTokenStatusListChecker`'s fetch/decompress/read path directly.

**Privacy note**: the `(uri, idx)` tuple is per-credential unique → traceable; do not log it with subject data (FR-024).

---

## R10 — x5c attach on issuance (the open question, resolved)

**Decision**: Mirror the Wallet Service's existing `IssueCredentialChainResolver.ResolveChainAsync(provider, tenantId, issuerWallet, …)` (fail-soft) in HAIP. Register `IOrgCertChainProvider` in HAIP `Program.cs`; add an `x5cChain` parameter to **both** `HaipCredentialMinter.MintCredentialAsync` (currently hardcodes `x5cChain:null`, `HaipCredentialMinter.cs:79`) and `MintCredentialWithExternalSignerAsync` (currently drops the param); resolve + thread the chain at `CredentialEndpoints.IssueCredential` (~`CredentialEndpoints.cs:337-385`). Under a `register` trust anchor: no chain (DID-verifiable). Under an X.509 anchor: chain required — if it cannot be resolved, **issuance fails closed** (a credential that should carry a chain must not ship without one — A3 tightens the Wallet Service's fail-soft-to-null behaviour for the X.509-anchor case).

**Rationale**: `ISdJwtService.CreateTokenAsync` already accepts `x5cChain` on all three overloads — the wire is present; only the minter and HAIP DI need the addition. `OrgCertChain.AsJwsChain()` returns leaf-first `IReadOnlyList<byte[]>` ready for both the SD-JWT `x5c` JWS header and the mdoc COSE `x5chain` (label 33).

**Evidence**: `IOrgCertChainProvider.cs`; `IssueCredentialChainResolver.cs:20-55`; `ISdJwtService.cs:46,73,94`; `HaipCredentialMinter.cs:79,119-129`; `CredentialEndpoints.cs:361,375`.

---

## R11 — Format/anchor as config, coexisting with existing routing

**Decision**: Add `CredentialFormat Format` to `CredentialRequirement` and `CredentialIssuanceConfig`, and `TrustAnchor` to `CredentialIssuanceConfig`. These coexist with the existing `CredentialRequirement.PresentationSource` (SorchaInternal|HaipExternalWallet|SorchaWallet) and `CredentialIssuanceConfig.TargetAudience` (SorchaInternal|HaipExternalWallet|SorchaLocalWallet) discriminators — *audience* says where the credential goes; *format* says how it is encoded; *trustAnchor* says what it is trusted under. No conflict (FR-025).

**Rationale**: The discriminators answer orthogonal questions; the format handler is selected by `Format`, the delivery path by `TargetAudience`/`PresentationSource`. Mismatched combinations (e.g. X.509 anchor with no provisioned org cert) fail closed with a configuration error (FR-022).

---

## Decisions summary

| # | Decision |
|---|---|
| R1 | Trust evaluator in `Blueprint.Engine`, WASM-friendly, network sources injected |
| R2 | Delete `AcceptedIssuers` + `_trustedRoots`; replace with `TrustPolicy` |
| R3 | `ITrustResolverRegistry` with register / x509-tenant / trustlist / did-allowlist sources |
| R4 | `ITrustListProvider` seam; ship operator-snapshot provider; live LOTL deferred |
| R5 | Source-tier assurance + upward-only claim override; default Low |
| R6 | Unify W3C + IETF status checkers behind `IStatusListChecker`, fail-closed |
| R7 | mdoc on BCL CBOR+COSE (pin Cose 10.0.8); x5chain helper; no PQC regression |
| R8 | mdoc CBOR/COSE structures (tag-24 wrapping load-bearing) |
| R9 | OpenID4VP 1.x hash-based SessionTranscript; mdoc revocation via IETF status list |
| R10 | Mirror `IssueCredentialChainResolver` in HAIP; X.509 anchor fails closed without chain |
| R11 | `Format` + `TrustAnchor` config coexists with existing audience/source discriminators |
