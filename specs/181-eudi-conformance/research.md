# Research: EUDI Conformance — Protocol Alignment & External Trust Rail

**Feature**: `181-eudi-conformance` · **Date**: 2026-07-10
**Method**: three parallel codebase-mapping dives (presentation dialect, F135 trust rail, X.509/CA
surfaces) + standards research (OpenID4VP 1.0 final / HAIP 1.0 final / SD-JWT VC final / ETSI TS 119 612).
All file:line citations verified against master at branch time.

---

## R1 — Where the shared DCQL model lives

**Decision**: New folder `src/Common/Sorcha.Verifier.Engine/Dcql/` containing the typed DCQL
request/response model, the builder, and the parser — one file pair, build and parse side by side so they
cannot drift (FR-008).

**Rationale**: `Sorcha.Verifier.Engine` is net10.0, WASM-safe (BouncyCastle-managed crypto only, no
P/Invoke — csproj comment says so explicitly), and is **already referenced by 4 of the 5 consumers**:
`Sorcha.Wallet.Pwa`, `Sorcha.UI.Components.User`, `Sorcha.Verifier`, `Sorcha.Blueprint.Service`. Only
`Sorcha.Haip.Service` needs a new ProjectReference. A brand-new common project would add solution noise
for ~6 types; `Sorcha.CitizenWallet.Abstractions` was considered but is scoped to wallet-device contracts,
and `Sorcha.Blueprint.Models` is not referenced by the PWA.

**Alternatives considered**: new `Sorcha.OpenId4Vp` common project (rejected: project proliferation for a
small surface; revisit if the OpenID4VCI leg later needs shared models); `Sorcha.CitizenWallet.Abstractions`
(rejected: wrong subject — DCQL is verifier/wallet wire dialect, not citizen-wallet device contracts).

**Note**: the architecture review (2026-06-02 §5.1) flags Verifier.Engine as one half of the dual VC-stack
problem. Placing the *dialect* model here does not deepen that split — the dialect is format-neutral wire
vocabulary consumed by both stacks, and centralising it removes four hand-rolled builder/parser sites.

## R2 — DCQL wire shape (OpenID4VP 1.0 final)

**Decision**: Implement this subset of DCQL:

```jsonc
"dcql_query": {
  "credentials": [                        // 1..n CredentialQuery
    {
      "id": "identity",                   // ^[a-zA-Z0-9_-]+$, unique in request
      "format": "dc+sd-jwt",              // or "mso_mdoc"
      "meta": {
        "vct_values": ["https://sorcha.dev/vc/assured-identity/v1"]   // dc+sd-jwt
        // "doctype_value": "org.iso.18013.5.1.mDL"                   // mso_mdoc
      },
      "claims": [
        { "path": ["givenName"] },
        { "path": ["address", "street"] }
      ]
      // claims[].values (value matching) — OUT OF SCOPE v1 (spec Assumptions)
    }
  ],
  "credential_sets": [                    // optional; alternatives
    { "options": [["pid"], ["assured_identity"]], "required": true, "purpose": "Prove your identity" }
  ]
}
```

Response: `vp_token` is a JSON **object** keyed by credential-query id, each value an **array** of
presentation strings (SD-JWT compact form / base64url mdoc DeviceResponse). `presentation_submission` is
absent. Optional-claim semantics: DCQL final models optionality via `claim_sets`; for v1 we map the
existing required/optional split onto `claims` (required) + `claim_sets` (one set with all claims, one
with required-only) — the builder/parser pair owns this mapping so callers keep the simple
required/optional API they have today.

**Rationale**: matches the final spec's normative shape; the mdoc verify path already parses the
object-keyed `vp_token` (`VerifierEndpoints.TryExtractMdocDeviceResponse`, `VerifierEndpoints.cs:318-355`),
so SD-JWT joins an existing envelope rather than inventing one.

**Alternatives considered**: full `claim_sets`/`values`/`trusted_authorities` support (deferred — nothing
in our flows needs it; `trusted_authorities` overlaps the F135 TrustPolicy which is our authoritative
mechanism).

## R3 — `dc+sd-jwt` migration mechanics

**Decision**: flip the constant at `SdJwtService.cs:199-203` (`header["typ"] = "dc+sd-jwt"`); accept
**both** `dc+sd-jwt` and `vc+sd-jwt` wherever typ is checked on the verify side (grep found **no runtime
typ checks today** — only test assertions — so acceptance is mostly about *not adding* a strict check that
breaks stored credentials); all DCQL `format` keys and OpenID4VCI issuer-metadata format identifiers move
to `dc+sd-jwt`. Update the six test files that assert `vc+sd-jwt`.

**Impacted sites** (from the dialect dive): `SdJwtService.cs:203` (write);
`VerifierEndpoints.cs:163` (PD format key — replaced by DCQL anyway); HAIP issuer metadata
(`MetadataEndpointTests` asserts `credentials_supported` format); test fixtures
(`DeviceDelegationIssuerTests:109,142`, `PresentationEngineTests:226`, `TestVpFactory:79,103,185,209`,
`CredentialEndpointTests:20,28,39`).

## R4 — Presentation dialect surfaces (what actually changes where)

| Surface | Today | Target |
|---|---|---|
| `Sorcha.Haip.Service/Endpoints/VerifierEndpoints.cs:141-179` | builds inline PE dict in signed request object | builds `dcql_query` via shared builder; request object keeps route + signing (RFC 9101 `oauth-authz-req+jwt` already correct) |
| `VerifierEndpoints.cs:190-277` (`HandleDirectPost`) | mdoc = object-keyed, SD-JWT = bare compact string; `presentation_submission` stored raw | one object-keyed `vp_token` for both formats; per-query dispatch; `presentation_submission` param dropped; legacy-shape input → typed 400 `LEGACY_DIALECT` |
| `SorchaWalletPresentationConsumer.cs:210-215` (Blueprint, F127) | minimal `openid4vp://` URI (client_id + nonce + request_id; **no embedded PD**) | emits `request_uri` form pointing at the F111 request-object endpoint carrying the DCQL body (converges with the desk-verifier form) |
| Desk verifier / F164 unified surface (`QrPresentationService.cs:71`) | already `openid4vp://authorize?request_uri=…` | unchanged (already the right transport) |
| `Sorcha.Wallet.Pwa/Services/Presentation/PresentationEngine.cs:33-73,195-245` | parses **inline** `presentation_definition` query param only; single `input_descriptors[0]` | fetches `request_uri`, validates signed request object (US6), parses `dcql_query` via shared parser; `Match()` generalises to query sets + alternatives |
| `Sorcha.Agent/Commands/HaipPresentCommand.cs:137-145` | hand-builds `presentation_submission` | drops submission; posts object-keyed `vp_token` |
| PWA consent UI (`Present.razor`, `ConsentSheet.razor`, `CredentialPickerDialog.razor`, `PresentationModels.cs`) | single-credential model (`ParsedPresentationRequest` = one vct) | `ParsedPresentationRequest` becomes a query set; ConsentSheet renders per-query sections; picker handles per-query alternatives |
| Stale worktree `.worktrees/116-…/Sorcha.Citizen.Verifier/PresentationRequestBuilder.cs` | orphaned PE builder | not in scope (worktree, not master); noted for the housekeeping sweep |

**Discrepancy found & resolved**: the PWA's `PresentationEngine` v1 comment says it parses "the unsigned
query-parameter form the reference verifier emits", but the main-tree reference verifier now emits the
`request_uri` form. The inline form only survives for tests and any F127 gate variant. The migration
retires the inline form entirely (spec FR-026), which also resolves this drift.

## R5 — ETSI TS 119 612 trusted-list parsing & signature verification depth

**Decision**: Parse the `TrustServiceStatusList` XML server-side in **Tenant Service** (which already
hosts `TrustEndpoints`). Extract: `SchemeInformation` → `TSLSequenceNumber`, `ListIssueDateTime`,
`NextUpdate`, `SchemeTerritory`, scheme operator name; `TrustServiceProviderList` → per
`TSPService`: `ServiceTypeIdentifier`, `ServiceStatus`, `ServiceDigitalIdentity/DigitalId/X509Certificate`
(DER). **Anchor inclusion filter**: service type ∈ { `…/Svctype/CA/QC`, `…/Svctype/CA/PKC` } AND status ∈
{ `…/Svcstatus/granted`, `…/Svcstatus/recognisedatnationallevel` } (config-extensible list). Everything
else is skipped and itemised in the import summary (FR-012).

**Signature verification depth (v1)**: verify the enveloped **XMLDSig core signature** using
`System.Security.Cryptography.Xml.SignedXml` with the signing certificate from `KeyInfo`; reject on
core-validation failure. Surface the **signer certificate identity** (subject, issuer, thumbprint) in the
import summary so the operator confirms out-of-band that it is the expected scheme operator. Full XAdES
qualifying-property validation and LOTL→TL pivot-chain validation are **explicitly deferred** (documented
in the import UI and ops docs) — the operator-attested import model (spec D3) makes the operator the
authenticity root for v1.

**Rationale**: `SignedXml` core validation is well-supported on the server and catches tampering; full
XAdES is a large surface with poor BCL support and is only load-bearing in the *live-fetch* model (the
deferred follow-up). This matches D3's trust model: the operator vouches for where the document came from;
the platform proves it wasn't modified and extracts deterministically.

**Alternatives considered**: full XAdES-B/T validation (rejected v1: effort ≫ value under operator-attested
import); no signature check at all (rejected: cheap tamper protection discarded).

## R6 — Trusted-list snapshot persistence & distribution

**Decision**: Replace the in-memory `OperatorSnapshotTrustListProvider`
(`Sorcha.ServiceClients.Http/Trust/TrustListProvider.cs:46-66`) as the authoritative store with an
EF-backed store in **Tenant** (`public` schema): `TrustedListSnapshot` (+ anchors serialized as a child
collection). The existing seams stay: `ITrustListProvider.GetSnapshotAsync(trustListId)` is the read seam
the `TrustListSourceResolver` chain already consumes via `ITenantTrustAnchorProvider`/`AnchorId()`
(`Sources/TrustListSourceResolver.cs:17-25` subclassing `X509TenantTrustSourceResolver`, whose evidence
already records `TrustListId` + `TrustListFreshness` — `TrustEvidence.cs:28,31`).

**Distribution**: verifying services (Blueprint, HAIP, Verifier) resolve anchors over HTTP from Tenant —
extend `TrustEndpoints` with `GET /api/v1/trust/trustlists/{id}/anchors` returning the DER root set +
freshness (service-tier auth), consumed by a caching HTTP-backed `ITrustListProvider` in
`Sorcha.ServiceClients.Http` (15-min in-process cache; trust decisions tolerate that staleness window
given lists roll monthly). The existing `PUT /trustlists/{id}` raw-roots route is **subsumed** by the new
import route (clean break — it was Feature 135's placeholder).

**Storage registration**: `IStorageRegistrationLog.RegisterPersistent/RegisterInMemory` per pattern #10;
NOT on the fail-fast audited list (trust config, reloadable), mirroring the F114 presentation-store
precedent. Migration folded per the pre-release squash convention
([[prerelease-migration-squash]] memory): add to Tenant's `InitialCreate`.

## R7 — Multibase status-list decode

**Decision**: at `BitstringStatusListChecker.cs:86`, accept both encodings:
`encodedList[0] == 'u'` → `Base64Url.DecodeFromChars(encodedList.AsSpan(1))`; otherwise
`Convert.FromBase64String` (current). One-line-plus-tests. The IETF checker
(`IetfTokenStatusListChecker.cs:161`) already uses Base64Url per its spec — no change.

## R8 — Certificate persistence (prerequisite for US4/US5)

**Decision**: `InternalCaTrustProvider` (`Sorcha.Tenant.Service/Trust/InternalCaTrustProvider.cs:17-20`)
currently holds tenant roots, **plaintext CA private keys**, org certs, and CRLs in
`ConcurrentDictionary`s — nothing survives restart. Before auto-enrolment is meaningful, persist to Tenant
Postgres: `TenantRootCa` (root DER + private key **encrypted at rest** with AES-256-GCM under a
config-derived key, KMS integration flagged as the existing production TODO), `OrgCertificateRecord`
(both provenances — see data-model), `TenantCrl`. The provider keeps its interface; the dictionaries
become a write-through cache over the EF store. Registered via `IStorageRegistrationLog`.

**Rationale**: spec US5 auto-enrols every new org; in-memory certs would silently vanish on every deploy,
breaking issued-credential x5c chain resolution (`IssueCredentialChainResolver` in Wallet Service resolves
chains from this store at mint time).

## R9 — Which key gets certified (refinement of D6)

**Finding**: org wallets are created with **ED25519 primary** by default
(`OrganizationService.cs:102`), yet the X.509 rail is P-256-only. The platform already has the answer:
HAIP-facing wallets carry a **classical ES256 co-key** derived under `sorcha:haip-issuer-signing`
(`WalletAlgorithmClassification.cs:18-28`, `Wallet.cs:141`), and the enrol endpoint carries a TODO to
validate the submitted key against exactly that co-key (`TrustEndpoints.cs:228-229`).

**Decision**: the org certificate always binds the org's **P-256 signing key**: the primary key when the
wallet is ES256-primary, else the derived HAIP-issuer ES256 co-key. Eligibility = "a P-256 key is
resolvable for this org". The D6 typed exclusion (`CERT_KEY_NOT_ELIGIBLE`) then applies only to orgs where
no P-256 key can be resolved (co-key derivation unavailable/failed) — and the enrol path additionally
gains the missing key-match validation (server resolves the key itself via Wallet Service instead of
trusting a caller-supplied key). This stays within D6's boundary — **no Ed25519 certificate wrapping is
built** — while making auto-enrolment useful for the default org population. ⚠ Flagged to the platform
owner in the plan report (it narrows D6's practical exclusion set to a rare failure mode).

**Alternatives considered**: literal D6 (exclude every ED25519-primary org) — rejected: with ED25519 the
creation default, auto-enrol (US5/FR-022) would no-op for essentially all orgs, gutting SC-006.

## R10 — CSR generation with wallet-custodied keys

**Decision**: the org's private key never leaves Wallet Service, so Tenant builds the CSR with a custom
`X509SignatureGenerator` that delegates signing to
`IWalletServiceClient.SignTransactionAsync(walletAddress, digest, derivationPath: "sorcha:haip-issuer-signing", isPreHashed: true)`
(the existing seam — no new Wallet API). Implementation notes: `CertificateRequest.CreateSigningRequest(X509SignatureGenerator)`
signs the `CertificationRequestInfo`; the wallet returns a raw `r‖s` ECDSA signature which the generator
converts to the DER `ECDSA-Sig-Value` encoding CSRs require (`AsnWriter` — both formats are
well-defined; conversion is ~20 lines and unit-testable with known vectors). P-256/SHA-256 only (R9).

**Alternatives considered**: adding a dedicated `SignCsrAsync` to Wallet Service (rejected: the generic
pre-hashed sign seam suffices; fewer API surfaces); generating a *new* keypair Tenant-side for the cert
(rejected: breaks key-continuity — the cert must bind the key credentials are actually signed with).

## R11 — Imported-certificate validation & chain attach

**Decision**: import validation = (a) `leaf.PublicKey` SPKI byte-match against the org's resolved P-256
key (R9); (b) chain build leaf→root over the uploaded set only (`X509Chain` with `CustomRootTrust` seeded
from the uploaded root, `RevocationMode = NoCheck` at import; external CRL/OCSP is the CA's concern);
(c) validity window sanity (leaf + every intermediate currently valid); (d) suitability = if KeyUsage
present it must include `digitalSignature`, if EKU present it must not exclude signing (absent extensions
pass — CA profiles vary). Both chain-with-root and chain-without-root uploads normalise to a stored
ordered chain. Chain-attach: `IssueCredentialChainResolver` (Wallet Service) and
`MdocFormatHandler.IssueAsync` resolve per `TrustAnchor` setting — `x509-lotl` → imported chain (fail
closed `CERT_EXTERNAL_ANCHOR_UNAVAILABLE` if none valid, per FR-020), `x509-tenant` → tenant chain
(unchanged, FR-021).

## R12 — Verifier request-signing certificate & `x509_san_dns` client_id

**Decision**: client_id becomes the **prefixed** final-spec form `x509_san_dns:{public-host}` (the
`client_id_scheme` parameter was folded into the prefix in the final spec). The HAIP verifier's request
object JWS gains an `x5c` header carrying an operator-provisioned **verifier certificate** whose SAN
dNSName equals the installation's public host: config `Haip:VerifierCertificate` (PFX path or base64) +
`Haip:VerifierCertificatePassword?`; in dev, fall back to a tenant-root-issued cert with SAN dns =
configured host (self-contained demos keep working; wallets show it as authentic-but-untrusted). KB-JWT
`aud` = the full prefixed client_id (both mint side, `PresentationEngine.BuildVpTokenAsync:170`, and
verify side, `VerifiablePresentationValidator` aud check). `RequestObjectSigner` currently signs with an
ephemeral/issuer key — switches to the verifier certificate key. The existing `TODO(098)` at
`VerifierEndpoints.cs:100` is exactly this change.

## R13 — PWA-side signed-request verification (WASM constraints)

**Decision**: implement request-object validation inside `Sorcha.Verifier.Engine` (new
`RequestObjectValidator`) using **BouncyCastle** — already a WASM-safe dependency of the engine — for
ES256 JWS verification and X.509 parsing/SAN extraction (BCL `ECDsa`/`X509Chain` are unavailable or
unreliable on browser-wasm; `System.Formats.Asn1` is available but BouncyCastle's cert object model is
already in the dependency tree). Steps: parse JWS → extract `x5c` → verify signature with leaf key →
check leaf SAN dNSName == host part of `x509_san_dns:` client_id → chain-to-anchor check against a
`TrustAnchorSet` supplied by the host app. Anchor supply: the PWA fetches its home installation's
trusted-list anchor sets via the R6 endpoint (cached in IndexedDB alongside status lists); no anchors ⇒
verdict caps at *authentic-but-untrusted* (never blocks an otherwise-valid request — spec FR-027's
three-state model). The desk verifier and Blueprint consumers get the same validator server-side.

## R14 — CI dialect gate

**Decision**: `scripts/check-presentation-dialect.ps1` + workflow step, following the
`check-no-snackbar.ps1` / `check-trust-clean-break.ps1` ratchet precedent: fail on `presentation_definition`,
`input_descriptors`, or `presentation_submission` under `src/` outside an allowlist file
(`.presentation-dialect-allowlist` — expected to be empty at feature completion; the allowlist exists so
the gate can land before the last consumer migrates). Also greps for `"vc+sd-jwt"` **writes** (the typ
constant site) while permitting verify-side acceptance constants.

## R15 — Walkthrough / demo / test migration inventory

From the dialect dive (SC-002 regression oracle): `walkthroughs/AssuredIdentity/**` (phase-2 licence
script exercises the HAIP verifier), `demos/AIAS/rehearse.ps1`, `demos/AssuredIdentity/**`,
`demos/Membership/presentations/membership-pos.presentation.json` (static PE fixture — regenerate as
DCQL), `Sorcha.Agent HaipPresentCommand`, plus the six test files in R3 and
`PresentationEngineTests` / `VerifiablePresentationValidatorTests` / `SorchaWalletPresentationConsumerTests` /
HAIP endpoint tests. E2E: F164 shared-verdict tests and F155 open-verifier tests ride the request_uri form
and re-validate via SC-002.

---

## Resolved clarifications

All Technical Context unknowns are resolved above; no NEEDS CLARIFICATION markers remain. The single
plan-level deviation from a locked decision is **R9** (certificates bind the P-256 co-key for
ED25519-primary orgs), which preserves D6's "no Ed25519 cert support" boundary while keeping US5's
auto-enrolment meaningful — flagged for the platform owner's attention in the plan report.
