# Data Model: EUDI Credential Format & Unified Trust

**Feature**: 135-eudi-credential-format-trust | **Date**: 2026-05-20

Conventions follow the existing credential models: **mutable classes** with `set;` properties (JSON round-trip + DataAnnotations), `System.Text.Json`, never records (per `verifiable-credentials` skill). New enums are `string`-serialised. Wire JSON uses camelCase.

---

## 1. Configuration models (Blueprint.Models.Credentials)

### CredentialFormat (NEW enum)

```
SdJwtVc   → wire "sd-jwt-vc"   (default)
MsoMdoc   → wire "mso_mdoc"
```

### AssuranceLevel (NEW enum, ordered)

```
Low (0)  <  Substantial (1)  <  High (2)        // default Low
```

### TrustAnchor (NEW enum)

```
Register     → wire "register"      (DID-verifiable, no x5c)   (default)
X509Tenant   → wire "x509-tenant"   (tenant CA leaf chain attached)
X509Lotl     → wire "x509-lotl"     (external trust-list root)
```

### TrustSourceKind (NEW enum)

```
Register | X509Tenant | TrustList | DidAllowlist
```

### TrustCombinator (NEW enum)

```
AnyOf (default) | AllOf
```

### TrustSourceRef (NEW)

| Field | Type | Notes |
|---|---|---|
| `Kind` | `TrustSourceKind` | required |
| `ConfersAssurance` | `AssuranceLevel?` | operator-configured level this source confers; null → Low |
| `AllowedIssuers` | `IReadOnlyList<string>?` | for `DidAllowlist` — DID URIs (alsoKnownAs-equivalent) |
| `TrustListId` | `string?` | for `TrustList` — which snapshot to consult |
| `Options` | `Dictionary<string,string>?` | source-specific tuning (e.g. CRL mode) |

### TrustPolicy (NEW) — replaces `CredentialRequirement.AcceptedIssuers`

| Field | Type | Notes |
|---|---|---|
| `Sources` | `IReadOnlyList<TrustSourceRef>` | required, ≥1 when explicitly set |
| `Combinator` | `TrustCombinator` | `AnyOf` default |
| `MinAssuranceLevel` | `AssuranceLevel` | `Low` default |

**Default policy (FR-026)**: when `TrustPolicy` is absent on a requirement, the evaluator synthesises one: any legacy issuer identifiers → a `DidAllowlist` source; otherwise a single `Register` source at `Low`.

### CredentialRequirement (CHANGED)

| Field | Change |
|---|---|
| ~~`AcceptedIssuers`~~ | **REMOVED** (clean break) |
| `Format` | **ADDED** `CredentialFormat` — which format to accept (default `SdJwtVc`) |
| `TrustPolicy` | **ADDED** `TrustPolicy?` — trust expectation (null → default policy) |
| `Type`, `RequiredClaims`, `RevocationCheckPolicy`, `Description`, `PresentationSource` | unchanged |

### CredentialIssuanceConfig (CHANGED)

| Field | Change |
|---|---|
| `Format` | **ADDED** `CredentialFormat` — encoding to mint (default `SdJwtVc`) |
| `TrustAnchor` | **ADDED** `TrustAnchor` — anchor to issue under (default `Register`) |
| `CredentialType`, `ClaimMappings`, `RecipientParticipantId`, `ExpiryDuration`, `RegisterId`, `Disclosable`, `UsagePolicy`, `MaxPresentations`, `DisplayConfig`, `TargetAudience` | unchanged |

**Validation**: `Format=MsoMdoc` requires `CredentialType`/claim mappings resolvable to an mdoc `docType` + namespaces (FR-004). `TrustAnchor∈{X509Tenant,X509Lotl}` requires a resolvable cert chain at mint time, else fail closed (FR-020/022).

---

## 2. Trust evaluation models (Blueprint.Engine.Credentials)

### TrustDecision (NEW)

| Field | Type | Notes |
|---|---|---|
| `IsTrusted` | `bool` | accept/reject |
| `EstablishedAssurance` | `AssuranceLevel` | the level actually established |
| `DecidingSources` | `IReadOnlyList<TrustSourceKind>` | which source(s) vouched |
| `SignatureValid` | `bool` | **always truthfully set** (closes the `=false` defect) |
| `FailureReason` | `TrustFailureReason?` | when rejected |
| `Evidence` | `TrustEvidence` | populated on accept (and on reject for audit) |

### TrustFailureReason (NEW enum)

```
UntrustedIssuer | SignatureInvalid | Revoked | RevocationUnavailable
| SourceUnavailable | InsufficientAssurance | ChainInvalid | HolderBindingInvalid
| IntegrityFailure | FormatUnsupported
```

### TrustEvidence (NEW) — pinnable, carried on spec-079 receipts (FR-014/015)

| Field | Type | Notes |
|---|---|---|
| `VouchingSource` | `TrustSourceKind` | which source established trust |
| `IssuerId` | `string` | resolved issuer (DID or cert subject) |
| `RegisterHeight` | `long?` | for `Register` source — height consulted |
| `CrlVersion` | `string?` | for X.509 sources — CRL version/thisUpdate |
| `TrustListId` | `string?` | for `TrustList` source — snapshot id |
| `TrustListFreshness` | `DateTimeOffset?` | snapshot freshness timestamp |
| `AssuranceLevel` | `AssuranceLevel` | established level |
| `EvaluatedAt` | `DateTimeOffset` | decision timestamp |
| `PolicyDigest` | `string` | hash of the policy evaluated (so a re-eval uses the same policy) |

**Pinnability invariant**: a verifier given only `{credential, TrustEvidence, pinned source material}` and no network MUST reproduce the same `IsTrusted` (or report `cannot-re-evaluate-offline`), never a different accept (FR-015, SC-005).

### Service contracts (NEW interfaces)

- `ITrustEvaluator.EvaluateAsync(issuerContext, TrustPolicy, CancellationToken) → TrustDecision`
- `ITrustResolverRegistry.Register(ITrustSourceResolver)` / `Resolve(TrustSourceKind)` — mirrors `IDidResolverRegistry`
- `ITrustSourceResolver.VouchAsync(issuerContext, TrustSourceRef, ct) → TrustSourceVouch` (per kind)
- `IStatusListChecker.CheckAsync(statusRef, ct) → StatusListBit` — unifies W3C bitstring + IETF token status
- `ICredentialFormatHandler` — `Format`, `VerifyAsync(presentation, requirement, ITrustEvaluator, ct)`, `IssueAsync(config, claims, signer, ct)`, `BuildPresentationAsync(...)`

---

## 3. mdoc wire models (Sorcha.Cryptography.Mdoc)

> Tag-24 (`#6.24(bstr .cbor X)`) wrapping is load-bearing in three places. Digests/signatures are over the **tagged outer bytes** — preserve verbatim, never re-encode the inner map.

### IssuerSignedItem / IssuerSigned

| Type | Fields |
|---|---|
| `IssuerSignedItem` | `DigestId:uint`, `Random:byte[]` (≥16), `ElementIdentifier:string`, `ElementValue:object` |
| `IssuerSignedItemBytes` | tag-24 CBOR of `IssuerSignedItem` (hash input for `valueDigests`) |
| `IssuerSigned` | `NameSpaces: Dictionary<string, IReadOnlyList<IssuerSignedItemBytes>>`, `IssuerAuth: CoseSign1` |

### MobileSecurityObject (MSO)

| Field | Type |
|---|---|
| `Version` | `string` ("1.0") |
| `DigestAlgorithm` | `string` ("SHA-256" \| "SHA-384" \| "SHA-512") |
| `ValueDigests` | `Dictionary<string, Dictionary<uint, byte[]>>` (namespace → digestID → digest) |
| `DeviceKeyInfo.DeviceKey` | `CoseKey` (holder binding key, e.g. EC2/P-256) |
| `DocType` | `string` (e.g. `eu.europa.ec.eudi.pid.1`, `org.iso.18013.5.1.mDL`) |
| `ValidityInfo` | `{ Signed, ValidFrom, ValidUntil, ExpectedUpdate? : DateTimeOffset }` |
| `Status` | `MsoStatus?` → `{ StatusList: { Uri:string, Idx:uint } }` (IETF token status, R9) |

### DeviceResponse / DeviceSigned / DeviceAuth

| Type | Fields |
|---|---|
| `DeviceResponse` | `Version:string`, `Documents: IReadOnlyList<Document>`, `Status:uint` |
| `Document` | `DocType:string`, `IssuerSigned`, `DeviceSigned` |
| `DeviceSigned` | `NameSpacesBytes: byte[]` (tag-24, usually empty map), `DeviceAuth` |
| `DeviceAuth` | exactly one of `DeviceSignature: CoseSign1` (detached) or `DeviceMac: CoseMac0` (detached) |

### SessionTranscript & DeviceAuthentication (OpenID4VP 1.x, R9)

| Type | Shape |
|---|---|
| `SessionTranscript` | `[ null, null, Handover ]` |
| `OpenID4VPHandover` | `[ "OpenID4VPHandover", SHA-256(OpenID4VPHandoverInfoBytes) ]` |
| `OpenID4VPHandoverInfo` | `[ clientId:string, nonce:string, jwkThumbprint:byte[]\|null, responseUri:string ]` |
| `OpenID4VPDCAPIHandover` | `[ "OpenID4VPDCAPIHandover", SHA-256([ origin, nonce, jwkThumbprint ]) ]` (recognised, secondary) |
| `DeviceAuthentication` | tag-24 of `[ "DeviceAuthentication", SessionTranscript, docType, DeviceNameSpacesBytes ]` — the detached payload that `DeviceAuth` signs/MACs |

### CoseX5Chain helper (NEW)

Encodes/decodes COSE header **label 33** in the **unprotected** bucket: single cert → `bstr`; multiple → array of `bstr` leaf-first (RFC 9360). No named BCL constant; uses `new CoseHeaderLabel(33)` + `CoseHeaderValue.FromEncodedValue(...)`.

---

## 4. mdoc ↔ blueprint claim mapping (FR-004)

| Blueprint concept | mdoc concept |
|---|---|
| `CredentialIssuanceConfig.CredentialType` | mdoc `docType` (e.g. PID `eu.europa.ec.eudi.pid.1`, mDL `org.iso.18013.5.1`) |
| `ClaimMapping.claimName` | `(namespace, elementIdentifier)` pair — mdoc namespaces are flat (2-element path) |
| `Disclosable` set | which `IssuerSignedItem`s the holder may withhold (all issuer-signed items are individually disclosable in mdoc) |
| SD-JWT `_sd` digests | MSO `valueDigests` |
| SD-JWT disclosure (salt+claim) | `IssuerSignedItem` (`random` + element) |
| KB-JWT | `DeviceAuth` over `DeviceAuthentication`/`SessionTranscript` |
| `iss` / issuer DID | MSO issuer (x5chain leaf subject, or DID) |
| status list claim | MSO `status.status_list` |

---

## 5. Trust-list snapshot (Tenant/ServiceClients)

| Type | Fields |
|---|---|
| `TrustListSnapshot` | `Id:string`, `Roots: IReadOnlyList<byte[]>` (DER), `Source:string`, `CreatedAt:DateTimeOffset`, `Freshness:DateTimeOffset` |
| `ITrustListProvider` | `GetSnapshotAsync(trustListId, ct) → TrustListSnapshot?` |

Loaded into `X509Chain.ChainPolicy.CustomTrustStore` for `trustlist` / `X509Lotl` evaluation; `Id` + `Freshness` recorded in `TrustEvidence`.

---

## 6. Removed shapes (clean break)

- `CredentialRequirement.AcceptedIssuers` — replaced by `TrustPolicy`.
- `HaipPresentationVerifier._trustedRoots` + `AddTrustedRoot(...)` — replaced by the `x509-tenant`/`trustlist` trust sources and `ITrustListProvider`.
- `CredentialVerifier`'s hardcoded `SignatureValid=false` deferral — replaced by real verification via `ICredentialFormatHandler` + `ITrustEvaluator`.
