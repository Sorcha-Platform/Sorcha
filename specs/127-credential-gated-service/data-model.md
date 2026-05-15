# Phase 1 — Data Model (F111-reconciled)

**Feature**: F127 / credential-gated second council service (Blue Badge)
**Date**: 2026-05-15 (reconciled with F111)
**Reconciliation note**: This data model adopts Feature 111's existing entities as the substrate. F127 adds only what F111 doesn't already ship: the new `IPresentationConsumer` for the Sorcha wallet, the `ClaimsFetchToken`, the blueprint shape, and the disclosed-claims view that the council page sees.

## Reused from F111 (no F127 change)

These are F111's entities, already in the codebase. Listed here for completeness so the F127 task list can reference them without re-defining them.

- **`PendingPresentation`** (record, `Storage/Presentations/`) — Redis-backed pending-attempt state keyed by `presentationRequestId` (Guid). TTL = blueprint's validity window (default 600 s; per-blueprint overridable). Carries: `InstanceId`, `ActionId`, `RegisterId`, `BlueprintId`, `SubmitterWallet`, `ConsumerName`, `DraftPayloadJson`, `CredentialRequirementDigestHex`, `DelegationToken?`, `RecordAbandonment`, `OutcomeDetailLevel`, `ValidityWindowSeconds`, `CreatedAt`, `InitiatedTransactionId?`.
- **`PresentationInitiationResult`** (record, `Services/Interfaces/`) — returned by `InitiateAsync`. Carries: `PresentationRequestId` (Guid), `AuthorizationRequestUri`, `RequestUri?`, `Nonce?`, `ExpiresAt`, `InitiatedTransactionId`. **F127 extends this record** with a new property: `ClaimsFetchToken` (single-use string, returned only on consumers that opt into claims-fetch).
- **`PresentationOutcome`** (record, `Sorcha.PresentationLifecycle.Abstractions`) — returned by `IPresentationConsumer.VerifyAsync`. Carries: `Kind` (Success / Decline), `VerifiedClaims?` (filtered to required claims, minimal disclosure), `Reason?` (`PresentationDeclineReason`), `VerifierDiagnostics?`, `PresentationSubmissionHash?`.
- **`PresentationInitiationContext`** (record, `Sorcha.PresentationLifecycle.Abstractions`) — passed to `IPresentationConsumer.VerifyAsync`. Reconstructed by the lifecycle service from the pending store. Carries the citizen and action context the consumer needs.
- **`CredentialRequirement`** (model, `Sorcha.Blueprint.Models.Credentials`) — declared on a blueprint action. Carries: `PresentationSource` (consumer name, e.g. `"haip"` or new `"sorcha-wallet"`), `CredentialType`, `IssuerAllowlist`, `RequiredClaims`. **F127 reuses this verbatim**; no new blueprint schema.
- **Register transactions**: `presentation-initiated`, `presentation-outcome`, `presentation-abandoned` — written by `IPresentationLifecycleService`. F127 reuses all three; no new transaction types.

## New entities F127 adds

### `SorchaWalletPresentationConsumer`

A new `IPresentationConsumer` registered in `Sorcha.Blueprint.Service`. Mirrors `HaipPresentationConsumer`.

| Member | Type | Notes |
|---|---|---|
| `ConsumerName` | `string` | `"sorcha-wallet"` |
| `VerifyAsync` | `(context, payload, ct) → Task<PresentationOutcome>` | Deserialises `payload` (JsonElement) into a `SorchaWalletVerificationPayload` (signed VP compact-JWS), invokes `Sorcha.Verifier.Engine`, returns `PresentationOutcome.Success` with `VerifiedClaims` filtered to `context.RequiredClaims`, or `PresentationOutcome.Decline` with the appropriate reason code (`expired-credential` / `revoked` / `wrong-issuer` / `signature-invalid` / `claims-missing`). |
| `BuildInitiationAsync` (NEW interface method) | `(context, ct) → Task<ConsumerInitiationDescriptor>` | Returns the OID4VP request URI + nonce + tap-link the citizen's wallet receives. F111 reads this when `InitiateAsync` dispatches to a non-HAIP consumer. |

### `ConsumerInitiationDescriptor`

NEW record in `Sorcha.PresentationLifecycle.Abstractions`. The return type of the new `BuildInitiationAsync` extension method on `IPresentationConsumer`.

| Field | Type | Notes |
|---|---|---|
| `AuthorizationRequestUri` | `string` | OID4VP `openid4vp://?…` URI; primary artifact the wallet receives. |
| `RequestUri` | `string?` | Optional alternative request URI shape. |
| `Nonce` | `string?` | Optional nonce echoed in the VP. |

### `SorchaWalletVerificationPayload`

NEW record in `Sorcha.Blueprint.Service.Services.Implementation` (or a sibling location). The wire shape `SorchaWalletPresentationConsumer.VerifyAsync` expects when deserialising the F111 callback's verifier payload.

| Field | Type | Notes |
|---|---|---|
| `SignedVp` | `string` | Compact-JWS verifiable presentation produced by the wallet. |
| `WalletDid` | `string?` | Optional explicit holder DID. Validated against the VP's `holder` claim. |

### `ClaimsFetchToken`

NEW. Single-use, short-TTL token that authenticates the council page on the new claims-fetch endpoint. Issued by `InitiateAsync` ONLY for consumers that opt into claims-fetch (the Sorcha-wallet path; HAIP keeps the existing register-only outcome flow).

| Field | Type | Notes |
|---|---|---|
| `Value` | `string` | High-entropy URL-safe random string (16-byte). |
| `PresentationRequestId` | `Guid` | Bound to a single F111 presentation request. |
| `ExpiresAt` | `DateTimeOffset` | Remaining validity window when minted. |

**Storage**: NEW interface `IClaimsFetchTokenStore` in `Sorcha.Blueprint.Service.Storage.Presentations`. Redis-backed. `SetAsync` at mint (writes the bound `presentationRequestId` against the token, with TTL); `GetAndRemoveAsync` at fetch (returns the bound `presentationRequestId` and atomically deletes the entry — NonceStore pattern, single-use enforced).

### `DisclosedClaimsResponse` (view-model)

NEW. Response shape for `GET /api/presentations/{requestId}/disclosed-claims?token=…`. Returns the `VerifiedClaims` from F111's `presentation-outcome.success` in plaintext to the council page for autofill.

| Field | Type | Notes |
|---|---|---|
| `PresentationRequestId` | `Guid` | Echoed back for the council-page-side state machine. |
| `Claims` | `IReadOnlyDictionary<string, JsonElement>` | Filtered to `requiredClaims`. |
| `SubjectDisplayName` | `string?` | Convenience — `"givenName familyName"` when both present. |
| `HolderDid` | `string` | The wallet's holder DID — used as the late-bind sender on the second action's submission. |

### `BlueBadgeCredential` (issued credential type — unchanged from pre-amendment)

Issued by Strathcarron Council, delivered into `SorchaLocalWallet`. Subject claims:

| Claim | Type | Notes |
|---|---|---|
| `givenName` | string | Copied from the disclosed `AssuredIdentityCredential`. |
| `familyName` | string | Copied. |
| `dateOfBirth` | string (ISO 8601 date) | Copied. |
| `homeAddress` | string | Copied. |
| `mobilityCondition` | string | Citizen-entered. |
| `previousBadgeNumber` | string (nullable) | Citizen-entered, optional. |
| `issuedAt` | DateTimeOffset | Set by the council's issuer wallet. |
| `expiresAt` | DateTimeOffset | Default 3 years. |
| `issuer` | string (DID URI) | `did:sorcha:org:strathcarron-council` |
| `credentialSubject.id` | string (DID URI) | Citizen's holder DID. |

**Storage**: lives in the Strathcarron Council credentials register (the same register F126 introduced for `AssuredIdentityCredential`). Revocation tracked via the F079 status-list mechanism.

## Blueprint shape (F111-reconciled)

The Blue Badge blueprint is a **three-action chain**:

```
verify-identity (starting, citizen actor, credentialRequirement.presentationSource="sorcha-wallet", no form schema)
    ↓ predecessor
submit-blue-badge-application (citizen actor, form schema with mobilityCondition + previousBadgeNumber, x-persona.presentation="verify-identity")
    ↓ predecessor
issue-blue-badge (licensing-officer actor, issuance of BlueBadgeCredential to SorchaLocalWallet)
```

Predecessor enforcement comes from the existing blueprint runtime; no new gating mechanism is required.

## State transitions (F111 substrate)

```
Citizen taps "Prove you're you"
    ↓ council page submits verify-identity action via Sorcha.ServiceClients.Blueprint
F111 InitiateAsync:
    writes presentation-initiated to register
    stores PendingPresentation in Redis (TTL = validity window)
    mints ClaimsFetchToken (via new IClaimsFetchTokenStore)
    dispatches to SorchaWalletPresentationConsumer.BuildInitiationAsync
    returns PresentationInitiationResult + ClaimsFetchToken to council page
    ↓ council page renders HybridQrAffordance with AuthorizationRequestUri
Wallet scans / taps → presents signed VP → POSTs to:
    /api/presentations/callbacks/sorcha-wallet/{requestId}
F111 HandleOutcomeAsync:
    dispatches to SorchaWalletPresentationConsumer.VerifyAsync
    consumer calls Sorcha.Verifier.Engine → returns PresentationOutcome
    F111 writes presentation-outcome to register (claims encrypted per disclosure rules)
    F111 publishes IBlueprintHubClient.PresentationOutcomeReady(requestId) to
        BlueprintHubGroups.PresentationNonce(requestId)
    ↓ council page learns via SignalR (or F111's existing status poll fallback)
Council page fetches disclosed claims:
    GET /api/presentations/{requestId}/disclosed-claims?token=ClaimsFetchToken
    server validates token via IClaimsFetchTokenStore.GetAndRemoveAsync (single-use)
    server reads PresentationOutcome from the register, decrypts claims per disclosure rules
    returns DisclosedClaimsResponse in plaintext
    ↓ council page transitions to submit-blue-badge-application action
Citizen fills form, submits → action 2 runs with form payload joined with disclosed claims (via x-persona.presentation autofill)
    ↓ action 2 success
issue-blue-badge action runs:
    mints BlueBadgeCredential
    delivers via SorchaLocalWallet to the citizen's wallet (existing F124 path)
```

## Validation rules (from spec FRs, F111-reconciled)

| FR | Rule | Enforcement point |
|---|---|---|
| FR-001 | Credential gate declared via existing `credentialRequirement` field. | F111 blueprint validation. |
| FR-002 | Submission of `verify-identity` fires F111's `InitiateAsync`. | Existing action-submission endpoint. |
| FR-003 | `SorchaWalletPresentationConsumer.VerifyAsync` invokes `Sorcha.Verifier.Engine`. | Blueprint Service DI. |
| FR-004 | Disclosed claims fetchable only with valid single-use `ClaimsFetchToken`. | `IClaimsFetchTokenStore.GetAndRemoveAsync`. |
| FR-005 | Three-action chain `verify-identity` → `submit-blue-badge` → `issue-blue-badge`. | Blueprint JSON authoring + runtime predecessor enforcement. |
| FR-018 | All-or-nothing consent on the wallet side. | PWA-side ConsentSheet; no per-claim toggle UI. |
| FR-019 | PWA confirms verifier + credential type before signing. | PWA-side dialog (mirrors F126 redeem-confirm). |
| FR-020 | A citizen lacking a matching credential sees a no-dead-end error state. | `CredentialGateComponent` after wallet picker returns empty. |
| FR-021 | A revoked credential's presentation is rejected. | `SorchaWalletPresentationConsumer` (via F079 status-list check inside `Sorcha.Verifier.Engine`). |
| FR-022 | Expired presentation surfaces a regenerate affordance. | `CredentialGateComponent` reading F111's status endpoint returning `expired`. |
| FR-023 | Council page learns of completion ≤ 2 s in 95% via SignalR; 3 s polling fallback; 60 s manual recovery. | New `IBlueprintHubClient.PresentationOutcomeReady` + existing F111 status-poll + `IPresentationSignal`. |
