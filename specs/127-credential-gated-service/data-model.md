# Phase 1 — Data Model

**Feature**: F127 / credential-gated second council service (Blue Badge)
**Date**: 2026-05-15

## New domain vocabulary

- **Credential gate** — a prerequisite on a blueprint starting action that demands a verifiable presentation of a named credential type, issued by a named issuer, before the action can run. Spec 4 introduces this term; subsequent specs reuse it.

## Entities

### CredentialGate (blueprint-side)

Declared on a starting action via `prerequisites.presentationRequests[]`. One blueprint can have multiple gates per starting action (e.g. "Assured Identity AND Proof of Residence"); Spec 4 demos a single-gate case.

| Field | Type | Constraints | Notes |
|---|---|---|---|
| `id` | string (kebab-case) | required, unique within action | e.g. `"assured-identity-check"`. Used to bind disclosed claims to autofill targets via `x-persona.presentation`. |
| `credentialType` | string | required | e.g. `"AssuredIdentityCredential"`. Matches the `type` claim on the VC. |
| `issuerAllowlist` | string[] (DID URIs) | required, ≥1 entry | e.g. `["did:sorcha:org:strathcarron-council"]`. Wallet picker filters to credentials whose issuer DID is in this set. |
| `requiredClaims` | string[] (JSON-pointer-style) | required, ≥1 entry | e.g. `["givenName", "familyName", "dateOfBirth", "homeAddress"]`. Server rejects presentations missing any required claim. |

**Validation**: JSON schema (see `contracts/prerequisites-presentation-requests.schema.json`) runs at blueprint publish via the existing FluentValidation pipeline.

### PresentationRequest (runtime)

Short-lived. Stashed in `IAtomicDistributedCache` (Redis) with TTL.

| Field | Type | Notes |
|---|---|---|
| `nonce` | string (16-byte URL-safe base64) | Primary key in the cache. |
| `requestUri` | string (URI) | OID4VP-shaped. Encoded as the QR / tap payload. |
| `qrUrl` | string (URI) | Council page renders this as a QR. |
| `tapUrl` | string (URI) | Same-device tap-link (opens PWA at `/wallet/present?request=…`). |
| `gateId` | string | Back-reference to the `CredentialGate.id` that minted this request. |
| `blueprintId` | Guid | Council page's blueprint context (re-fetched when validating). |
| `expiresAt` | DateTimeOffset | TTL 5 minutes by default. |

**Lifecycle**: created on `POST /api/blueprint/presentation-requests` → stashed → exists until either consumed by `POST /api/blueprint/presentation-responses` (deleted on consume) or expires.

**Storage**: Redis via `IAtomicDistributedCache`. Single-use enforcement = stash on create, `GetAndRemoveAsync` on consume — same NonceStore pattern as F126's enrol-session.

### PresentationResponse (runtime)

Produced by the wallet, posted to the platform.

| Field | Type | Notes |
|---|---|---|
| `nonce` | string | Must match an outstanding `PresentationRequest`. |
| `signedVp` | string (compact JWS) | The signed verifiable presentation. Validated server-side by `Sorcha.Verifier.Engine`. |

After server-side validation:

| Field (added) | Type | Notes |
|---|---|---|
| `disclosedClaims` | Dictionary<string, JsonElement> | Subset of VP claims that satisfy the gate's `requiredClaims`. Stashed against the nonce. |
| `holderDid` | string (DID URI) | Wallet's DID — used as the late-bind sender if the application advances. |
| `trustStatus` | enum (`Valid`, `Revoked`, `IssuerNotTrusted`, `SignatureInvalid`) | Result of trust-hardening check (F079). |

**Storage**: validated `disclosedClaims` stashed in `IAtomicDistributedCache` keyed by nonce, TTL extended to 10 minutes (gives the council page room to fetch + render).

### DisclosedClaims (view-model surfaced to the council page)

Plain data transferred to the council page after `GET /api/blueprint/presentation-responses/{nonce}` succeeds:

| Field | Type | Notes |
|---|---|---|
| `claims` | Dictionary<string, JsonElement> | The validated `disclosedClaims`. |
| `subjectDisplayName` | string | Derived from `givenName + familyName` if present. |
| `holderDid` | string | For the late-bind sender on submission. |

### BlueBadgeCredential (new credential type)

Issued by Strathcarron Council, delivered into `SorchaLocalWallet`.

| Claim | Type | Notes |
|---|---|---|
| `givenName` | string | Copied from the disclosed `AssuredIdentityCredential`. |
| `familyName` | string | Copied. |
| `dateOfBirth` | string (ISO 8601 date) | Copied. |
| `homeAddress` | string | Copied. |
| `mobilityCondition` | string | Citizen-entered on the Blue Badge form. |
| `previousBadgeNumber` | string (nullable) | Citizen-entered, optional. |
| `issuedAt` | DateTimeOffset | Set by the council's issuer wallet. |
| `expiresAt` | DateTimeOffset | Default 3 years from `issuedAt`. |
| `issuer` | string (DID URI) | `did:sorcha:org:strathcarron-council` |
| `credentialSubject.id` | string (DID URI) | Citizen's holder DID. |

**Storage**: lives in the Strathcarron Council credentials register (the same register F126 introduced for `AssuredIdentityCredential`). Revocation tracked via the F079 status-list mechanism.

## State transitions

```
PresentationRequest:
    [created]   ── via POST /api/blueprint/presentation-requests ──>   [pending]
    [pending]   ── wallet signs + posts ─────────────────────────>     [validating]
    [pending]   ── 5 min expiry ──────────────────────────────────>    [expired]
    [validating] ── Sorcha.Verifier.Engine ✓ ────────────────────>     [resolved]
    [validating] ── Sorcha.Verifier.Engine ✗ ────────────────────>     [rejected]
    [resolved]  ── council page fetches claims, blueprint advances ─>  [consumed]
```

After `[consumed]`, the council form has the disclosed claims and the citizen fills the Blue-Badge-specific fields. Submission proceeds via the existing register-native flow:

```
Application form:
    [pending submission] ── citizen submits ──> [bp action runs] ──> [BlueBadgeCredential issued] ──> [wallet receives credential]
```

## Relationships

```
Blueprint (existing)
  └─ Action (existing) — isStartingAction = true
        └─ Prerequisites
              └─ PresentationRequests[] (NEW: CredentialGate[])
                    ↓ resolved at runtime to
              PresentationRequest (NEW: short-lived runtime artifact)
                    ↑ posted against by
              PresentationResponse (NEW: from wallet)
                    ↓ validated to
              DisclosedClaims (NEW: view-model)
                    ↓ used to autofill
              Application form submission
                    ↓ blueprint runtime issues
              BlueBadgeCredential (NEW: into SorchaLocalWallet)
```

## Validation rules (from spec FRs)

| FR | Rule | Enforcement point |
|---|---|---|
| FR-001, FR-002 | A blueprint starting action MAY have a `prerequisites.presentationRequests` block. | JSON schema validation at publish. |
| FR-003 | A `PresentationResponse` whose disclosed claims don't satisfy `requiredClaims` MUST be rejected. | `Sorcha.Verifier.Engine` + endpoint handler. |
| FR-016 | Consent surface is all-or-nothing. | PWA-side ConsentSheet; no per-claim toggle UI. |
| FR-017 | PWA confirms verifier + credential type before signing. | PWA-side dialog (mirrors F126 redeem-confirm). |
| FR-018 | A citizen lacking a matching credential sees a no-dead-end error state. | `CredentialGateComponent` after wallet picker returns empty. |
| FR-019 | A revoked credential's presentation is rejected. | `Sorcha.Verifier.Engine` (F079 status-list check). |
| FR-020 | Expired `PresentationRequest` surfaces a regenerate affordance. | `CredentialGateComponent`. |
| FR-021 | Council page learns of completion within 2 s in 95% of attempts. | `IPresentationSignal` + SignalR `PresentationReceived` event. |
