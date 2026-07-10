# Data Model: EUDI Conformance — Protocol Alignment & External Trust Rail

**Feature**: `181-eudi-conformance` · **Date**: 2026-07-10 · **Source**: spec.md Key Entities + research.md

## 1. DCQL wire model (`Sorcha.Verifier.Engine/Dcql/`)

Immutable records, `System.Text.Json`-serialized with exact spec property names. Builder + parser live in
the same file pair (FR-008).

### DcqlQuery
| Field | Type | JSON | Rules |
|---|---|---|---|
| Credentials | `IReadOnlyList<DcqlCredentialQuery>` | `credentials` | 1..n; ids unique |
| CredentialSets | `IReadOnlyList<DcqlCredentialSetQuery>?` | `credential_sets` | optional; every option id must exist in `credentials` |

### DcqlCredentialQuery
| Field | Type | JSON | Rules |
|---|---|---|---|
| Id | `string` | `id` | `^[a-zA-Z0-9_-]+$`, unique per request |
| Format | `string` | `format` | `dc+sd-jwt` \| `mso_mdoc` (v1) |
| Meta | `DcqlCredentialMeta` | `meta` | required in v1 |
| Claims | `IReadOnlyList<DcqlClaimQuery>?` | `claims` | null ⇒ wallet discloses per its own policy |
| ClaimSets | `IReadOnlyList<IReadOnlyList<string>>?` | `claim_sets` | v1: emitted only by the builder's required/optional mapping (research R2) |

### DcqlCredentialMeta
| Field | Type | JSON | Rules |
|---|---|---|---|
| VctValues | `IReadOnlyList<string>?` | `vct_values` | required when format `dc+sd-jwt` |
| DoctypeValue | `string?` | `doctype_value` | required when format `mso_mdoc` |

### DcqlClaimQuery
| Field | Type | JSON | Rules |
|---|---|---|---|
| Id | `string?` | `id` | required only when referenced from `claim_sets` |
| Path | `IReadOnlyList<string>` | `path` | non-empty; string segments only in v1 (no array indices) |

### DcqlCredentialSetQuery
| Field | Type | JSON | Rules |
|---|---|---|---|
| Options | `IReadOnlyList<IReadOnlyList<string>>` | `options` | each inner list = ids that together satisfy the set |
| Required | `bool` | `required` | default true |
| Purpose | `string?` | `purpose` | consent-surface display |

### DcqlVpToken (response envelope)
| Field | Type | JSON | Rules |
|---|---|---|---|
| Presentations | `IReadOnlyDictionary<string, IReadOnlyList<string>>` | (root object) | key = credential-query id; value = presentation strings (SD-JWT compact / base64url mdoc DeviceResponse); unknown key ⇒ verification failure `DCQL_UNKNOWN_QUERY_ID` |

### Parsed request (PWA-side, replaces `ParsedPresentationRequest` single-vct shape)
`ParsedPresentationRequest` (in `Sorcha.UI.Components.User/Models/User/Presentation/PresentationModels.cs`)
becomes: `ClientId` (prefixed form), `VerifierHost` (derived), `ResponseUri`, `Nonce`, `ResponseMode`,
`Query: DcqlQuery`, `VerifierAuthentication: VerifierAuthState` (see §5). `CredentialMatch` becomes
`DcqlQueryMatch { QueryId, Candidates: IReadOnlyList<CredentialMatch> }` with a request-level
`DcqlMatchResult { Satisfiable: bool, PerQuery, UnsatisfiedRequiredQueryIds, SetChoices }`.

**State transitions (wallet)**: Parsed → Matched (per-query) → Consented (per-query claim approval,
alternative chosen) → Submitted. No partial submission when any required query/set unsatisfied (FR-006d).

## 2. Trusted-list snapshot (Tenant Service, EF `public` schema)

### TrustedListSnapshot
| Field | Type | Rules |
|---|---|---|
| Id | `Guid` PK | |
| TrustListId | `string` | operator-chosen stable identity, matches `TrustSourceRef.trustListId`; indexed |
| SequenceNumber | `long` | from `TSLSequenceNumber`; per-TrustListId monotonic — import of ≤ current sequence rejected `TRUSTLIST_SEQUENCE_REGRESSION` (edge case: concurrent import) |
| SchemeTerritory | `string?` | e.g. `EU`, `IE` |
| SchemeOperatorName | `string?` | display |
| ListIssueDateTime | `DateTimeOffset` | from list |
| NextUpdate | `DateTimeOffset?` | from list; null ⇒ treated as stale after config default (90 d) |
| SignerCertSubject / SignerCertThumbprint | `string` | XMLDSig signer identity surfaced at import (research R5) |
| ImportedAt / ImportedByPlatformUserId | `DateTimeOffset` / `Guid` | provenance |
| SourceUrl | `string?` | when imported by URL fetch-once |
| RawDocumentSha256 | `string` | audit tie to the imported bytes |
| Status | enum `Active \| Superseded` | newest Active per TrustListId is authoritative (FR-013) |
| Anchors | owned collection → `TrustedListAnchor` | |
| ExtractionSummary | `string` (json) | extracted vs skipped service entries (FR-012) |

### TrustedListAnchor
| Field | Type | Rules |
|---|---|---|
| Id | `Guid` PK / SnapshotId FK | cascade delete |
| CertificateDer | `byte[]` | the CA cert |
| SubjectDn / Thumbprint | `string` | display + dedupe |
| ServiceTypeIdentifier | `string` | originating `TSPService` type URI |
| ServiceStatus | `string` | originating status URI |
| NotBefore / NotAfter | `DateTimeOffset` | from cert |

**Freshness state (computed, never stored)**: `Fresh` (now < NextUpdate) \| `Stale` (past NextUpdate;
warn-mode evaluates + flags evidence, strict mode `Trust:TrustListStrictFreshness=true` fails closed —
FR-016). Read seam unchanged: `ITrustListProvider.GetSnapshotAsync` → `TrustListSnapshot` (wire DTO keeps
its current shape + gains `SequenceNumber`, `NextUpdate`, `FreshnessState`); `TrustAnchorSet.AnchorSetId`
carries `{trustListId}#{sequenceNumber}` into `TrustEvidence.TrustListId` (FR-015).

## 3. Certificate persistence (Tenant Service, EF `public` schema — research R8)

### TenantRootCa (persists today's in-memory `_roots` + `_rootPrivateKeys`)
| Field | Type | Rules |
|---|---|---|
| TenantId | `string` PK | |
| CertificateDer | `byte[]` | self-signed root |
| PrivateKeyCiphertext / Nonce | `byte[]` | AES-256-GCM at rest (constitution II); KMS = existing TODO |
| Algorithm | `string` | `ES256` only (v1) |
| CreatedAt / NotAfter | `DateTimeOffset` | |
| CrlNumber | `int` | monotonic (moves off `_crlCounters`) |

### OrgCertificateRecord (persists `_orgCerts` + adds imported provenance)
| Field | Type | Rules |
|---|---|---|
| Id | `Guid` PK | |
| TenantId + OrgWalletAddress | `string` | composite index; org identity |
| Provenance | enum `Internal \| Imported` | D5/D4 |
| Status | enum `Active \| Superseded \| Revoked \| KeyMismatch` | one Active per (org, provenance); re-issue supersedes (FR-023d); KeyMismatch set when org key rotation invalidates an imported cert (edge case) |
| CertificateDer | `byte[]` | leaf |
| ChainDer | `byte[][]` (jsonb) | ordered leaf-exclusive chain; Internal ⇒ [root] |
| BoundPublicKeySpki | `byte[]` | the P-256 key certified (research R9) |
| BoundKeySource | enum `Primary \| HaipCoKey` | which org key it binds |
| SerialNumber / SubjectDn / SanUri-or-Dns | `string` | display/audit |
| NotBefore / NotAfter | `DateTimeOffset` | expiry surfaced in admin UI (US4 AS-4) |
| CreatedAt / CreatedByPlatformUserId | provenance | auto-enrol records system principal |
| RevokedAt? / RevocationReason? | | Internal certs only (CRL path exists); Imported revocation is the external CA's concern |

### CsrRecord (lightweight audit of FR-018)
| Field | Type | Rules |
|---|---|---|
| Id | `Guid` PK; TenantId + OrgWalletAddress | |
| CsrPem | `string` | returned to admin; stored for audit |
| BoundPublicKeySpki | `byte[]` | must match at cert import time (`CERT_KEY_MISMATCH` otherwise) |
| CreatedAt / CreatedByPlatformUserId | | |

**Org eligibility (computed)**: `Eligible(P256Key)` \| `NotEligible(CERT_KEY_NOT_ELIGIBLE)` — resolved
via Wallet Service (primary ES256, else HAIP co-key derivation); never stored (mirrors F108
derived-relationship convention).

## 4. Verifier certificate (config, not DB)

`Haip:VerifierCertificate` (PFX path or base64) + `Haip:VerifierCertificatePassword?` +
`Haip:PublicHost`. Dev fallback: tenant-root-issued cert with SAN dNSName = `Haip:PublicHost`
(research R12). Validation at startup: SAN dNSName must equal `Haip:PublicHost`, else fail-fast in
Production/Staging (mirrors `SorchaIssuer` fail-closed posture).

## 5. VerifierAuthState (engine model, carried to PWA consent surface)

enum-ish record per FR-027: `TrustedListVerified(anchorSetId)` \| `AuthenticUntrusted` \|
`Unverifiable(reason)`. Produced by `RequestObjectValidator` (research R13); rendered by `ConsentSheet`.

## 6. New typed error codes

| Code | Where |
|---|---|
| `LEGACY_DIALECT` | 400 on PE-shaped request/response bodies (FR-007) |
| `DCQL_UNKNOWN_QUERY_ID` | verification failure (FR-003) |
| `TRUSTLIST_SIGNATURE_INVALID` / `TRUSTLIST_MALFORMED` / `TRUSTLIST_SEQUENCE_REGRESSION` | import (FR-011/FR-013) |
| `TRUSTLIST_UNAVAILABLE` | trust evaluation with no snapshot (FR-014) |
| `TRUSTLIST_STALE` | strict-mode fail-closed (FR-016) |
| `CERT_KEY_NOT_ELIGIBLE` | non-P-256-resolvable org (FR-024) |
| `CERT_KEY_MISMATCH` / `CERT_CHAIN_INVALID` / `CERT_EXPIRED` / `CERT_UNSUITABLE` | import validation (FR-019) |
| `CERT_EXTERNAL_ANCHOR_UNAVAILABLE` | issuance fail-closed (FR-020) |
| `REQUEST_OBJECT_INVALID` / `REQUEST_HOST_MISMATCH` | wallet-side request refusal (FR-026) |

## 7. Metrics (FR-028)

| Instrument | Meter | Tags |
|---|---|---|
| `sorcha_trustlist_snapshot_info` (gauge) / `sorcha_trustlist_stale_evaluation_total` (counter) | `Sorcha.Trust` | `trust_list_id`, `sequence` |
| `sorcha_trust_decision_total` | existing `Sorcha.Trust` | gains real `source=trustlist` traffic (no schema change) |
| `sorcha_org_cert_issuance_total` | `Sorcha.Trust` | `provenance`, `outcome`, `reason` |
| `sorcha_dialect_rejection_total` | `Sorcha.Haip` (new meter or existing) | `surface` |
| `sorcha_request_auth_total` | `Sorcha.Trust` | `state ∈ {trusted, authentic_untrusted, unverifiable}` |
