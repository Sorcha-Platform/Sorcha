# Data Model — Open Verifier PWA

Phase 1. Entities are mostly view/result shapes (the feature adds little persistence). New/changed types
are grouped by where they live.

## Engine — enriched verification result

### `ValidationLayerResult` (NEW — `Sorcha.Verifier.Engine/Models`)

Per-layer outcome surfaced for the trail.

| Field | Type | Notes |
|---|---|---|
| `Layer` | `ValidationLayer` (enum) | `LivePresentation`, `IssuerSignature`, `Revocation`, `RegisterAnchor` |
| `Status` | `LayerStatus` (enum) | `Pass`, `Fail`, `Unverified` (FR-013 distinguishes Fail vs Unverified) |
| `Headline` | `string` | short human label, e.g. "Not revoked" |
| `Detail` | `IReadOnlyDictionary<string,string>` | raw key→value lines shown when expanded (protocol, nonce, iss, kid, status-list uri/idx, docket, …) |

### `VerificationOutcome` (CHANGED — same file)

Add: `IReadOnlyList<ValidationLayerResult> Layers { get; init; }` (defaults `[]`).
Existing fields unchanged (`Accepted`, `DisclosedClaims`, `Errors`, `CompletedAt`, `IssuerSignature`).

- `VerifiablePresentationValidator.ValidateAsync` populates `Layers` for LivePresentation, IssuerSignature,
  Revocation (Selective-disclosure detail is derived in the UI from `DisclosedClaims` + the session's
  requested claims — no engine field needed; withheld = requested-but-undisclosed ∪ known-issued-not-requested).
- The **RegisterAnchor** layer is appended by the verifier app after the anchor read (engine has no anchor).

## Verifier app — view + client types

### `QuestionPreset` (NEW — `Sorcha.Verifier/Services`)

| Field | Type | Notes |
|---|---|---|
| `Key` | `string` | `age-over-18`, `confirm-identity`, `custom` |
| `Label` | `string` | "Age over 18?" |
| `RequiredVct` | `string` | e.g. `AssuredIdentityCredential` vct |
| `RequiredClaims` | `IReadOnlyList<string>` | `["age_over_18","portrait"]` for age-over-18 |
| `OptionalClaims` | `IReadOnlyList<string>` | usually empty |

`custom` keeps the existing free-form fields.

### `RegisterAnchorResult` (NEW — `Sorcha.Verifier/Services`)

Returned by `IRegisterAnchorClient.CheckAsync(registerId, credentialId, ct)`.

| Field | Type | Notes |
|---|---|---|
| `Anchored` | `bool` | true if issuance tx found AND inclusion proof verifies |
| `Status` | `LayerStatus` | `Pass` / `Fail` / `Unverified` (e.g. not found → Unverified) |
| `TxId` | `string?` | issuance transaction id |
| `DocketNumber` | `ulong?` | sealing docket |
| `SealedAt` | `DateTimeOffset?` | |
| `RegisterId` | `string` | echoed |
| `BundleJson` | `string?` | the exportable verification bundle (FR-011) |

### `VerdictViewModel` (NEW — `Sorcha.Verifier/Services`)

Assembled for `Outcome.razor`:
- `OverallPass` (bool — `Accepted` AND no `Fail` layers; anchor `Unverified` does **not** veto, FR-013),
- `Headline` (e.g. "Over 18"), `IssuerDisplayName`, `IssuerDid`, `PortraitBase64?`,
- `DisclosedClaims`, `WithheldClaims`,
- `Layers: IReadOnlyList<ValidationLayerResult>` (the four trail rows).

## Register Service — new public read

### `CredentialAnchorResponse` (NEW DTO — `Sorcha.Register.Service`)

Response of `GET /api/registers/{registerId}/credentials/{credentialId}/anchor` (anonymous).

| Field | Type | Notes |
|---|---|---|
| `RegisterId` | `string` | |
| `CredentialId` | `string` | echoed |
| `TxId` | `string` | issuance transaction id |
| `DocketNumber` | `ulong` | |
| `SealedAt` | `DateTimeOffset` | |
| `Status` | `string` | transaction lifecycle status (Active/Revoked/Superseded) |
| `InclusionProof` | `MerkleInclusionProof` | existing F079 type |

404 when no issuance tx matches `(registerId, credentialId)` — distinct from a verification failure.

### Repository (CHANGED — `IReadOnlyRegisterRepository`)

Add `Task<TransactionModel?> GetCredentialIssuanceTransactionAsync(string registerId, string credentialId, CancellationToken ct)`
— queries `GetTransactionsAsync(registerId)` for `MetaData.TransactionType == credential-issuance` (or
`TrackingData["type"]=="credential-issuance"`) AND `TrackingData["credentialId"] == credentialId`.
Implement on EF + Mongo + in-memory.

## Credential (issuance) — AssuredIdentity additions

`credentialIssuanceConfig`:
- add claim mapping `{ "claimName": "age_over_18", "sourceField": "/ageCheck/over18" }` and add
  `age_over_18` to `disclosable[]`.
- add claim mapping `{ "claimName": "registerAnchor", "sourceField": "/issuanceContext/registerId" }`
  (disclosable) — the verifier reads `registerAnchor` (registerId) + the credential's own `jti` to call
  the anchor endpoint. `ActionExecutionService` injects `/issuanceContext/registerId` into merged data
  before `BuildClaimsFromMappings`.

State transitions: none new. The presentation session lifecycle (pending → accepted/rejected) is unchanged.
