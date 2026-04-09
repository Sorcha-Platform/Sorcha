# Phase 1 Data Model: Credential & Presentation Security Fixes

**Feature**: 093-vc-security-fixes
**Date**: 2026-04-09
**Purpose**: Describe the data shapes this spec introduces or modifies. Everything here is additive or behaviourally neutral on the wire.

## Scope note

This spec does not introduce new persisted entities, new tables, or new API request/response types. It changes two things at the data level:

1. The **content** of the signed SD-JWT VC payload (new `credentialStatus` claim).
2. The **content** of the `publicKeyMultibase` string inside a `VerificationMethod` of a `DidDocument` returned by `SorchaDidResolver`.

All existing C# classes, EF entities, and DTOs retain their field shapes byte for byte.

## Entities

### 1. `credentialStatus` claim (new, embedded in signed SD-JWT payload)

**Where it lives**: inside the signed JWT payload of a Sorcha-issued SD-JWT VC, as a top-level claim. Non-disclosable (not part of the `_sd` digest array). Emitted by `Sorcha.Wallet.Service.Endpoints.CredentialEndpoints.IssueCredential` at signing time.

**Shape**: W3C `BitstringStatusListEntry` format, matching the existing Sorcha W3C Bitstring Status List producer.

```json
{
  "credentialStatus": {
    "id": "https://{deployment}/api/v1/credentials/status-lists/{listId}#{index}",
    "type": "BitstringStatusListEntry",
    "statusPurpose": "revocation",
    "statusListIndex": "{index}",
    "statusListCredential": "https://{deployment}/api/v1/credentials/status-lists/{listId}"
  }
}
```

**Field semantics**:

| Field | Type | Meaning |
|---|---|---|
| `id` | string (URL fragment) | Unique per credential. `{statusListCredential}#{statusListIndex}`. |
| `type` | string, fixed value `BitstringStatusListEntry` | Identifies the W3C BitstringStatusListCredential format. Matches the envelope published by the existing Sorcha status list endpoint. |
| `statusPurpose` | string, fixed value `revocation` in this spec | The bit represents revocation. A future spec may add a parallel claim with `statusPurpose: suspension`. |
| `statusListIndex` | string (decimal integer) | Position in the bitstring where the credential's status bit lives. W3C BitstringStatusListEntry defines this as a string. |
| `statusListCredential` | string (URL) | Public URL of the BitstringStatusListCredential resource. |

**Validation rules**:

- All five fields MUST be present in a newly issued credential when `CredentialStatus:EnableEmbedding` is true.
- `statusListCredential` MUST be an absolute HTTPS URL resolvable by external parties.
- `statusListIndex` MUST match the index returned by `IStatusListManager.AllocateIndexAsync` at the same call site.
- `type` MUST be the literal string `BitstringStatusListEntry`.
- The claim MUST NOT appear in the `_sd` disclosable set — it is always visible to any consumer that decodes the token.

**State transitions**: none. The claim is written once at issuance and never modified. The underlying bit in the status list flips via the existing lifecycle operations (revoke, suspend, reinstate); the claim in the token is immutable.

### 2. `VerificationMethod.publicKeyMultibase` (behavioural change)

**Where it lives**: inside a `DidDocument.VerificationMethod` returned by `SorchaDidResolver.ResolveWalletDidAsync` and `ResolveOrgDidAsync`. The existing `Sorcha.ServiceClients.Did.VerificationMethod` type carries a `PublicKeyMultibase` string field — no shape change.

**Current (bug) value** (from `SorchaDidResolver.cs:93, 140`):

```text
publicKeyMultibase = "z" + wallet.PublicKey  // e.g. "z0x1a2b3c..." — not valid multibase
```

**Fixed value** (for each supported algorithm):

| Algorithm | Raw public key bytes | Multicodec prefix (unsigned varint) | Resulting multibase string |
|---|---|---|---|
| Ed25519 | 32 bytes | `0xed 0x01` | `"z" + Base58btc(0xed 0x01 \|\| rawBytes)` |
| NIST P-256 | 33 bytes compressed SEC1 | `0x80 0x24` (varint of 0x1200) | `"z" + Base58btc(0x80 0x24 \|\| rawBytes)` |
| RSA-4096 | DER `SubjectPublicKeyInfo` bytes | `0x85 0x24` (varint of 0x1205) | `"z" + Base58btc(0x85 0x24 \|\| rawBytes)` |

**Unsupported algorithms** (including any future ML-DSA / SLH-DSA that might be added to Sorcha before a multibase assignment exists): the resolver sets `publicKeyMultibase = null` and sets `publicKeyJwk` instead (JWK form carrying the raw public key in its natural encoding for the algorithm), or fails closed with a clear error if neither path is configured.

**Validation rules**:

- The string MUST start with the literal character `z` (base58btc multibase prefix per RFC 9562 draft / W3C DID Core).
- The remainder after `z` MUST be a valid base58btc encoding (no invalid characters per the Bitcoin base58 alphabet).
- Decoding the remainder and stripping the multicodec prefix MUST yield the original raw public key bytes exactly.
- Round-trip through a W3C-compliant DID Core parser (for example `@digitalbazaar/did-io` in JavaScript, or equivalent) MUST succeed without multibase errors.

### 3. `PresentationRequest` (unchanged shape, changed population path)

**Where it lives**: `Sorcha.Wallet.Service.Models.PresentationRequest` (existing).

**What changes**: the `VerificationResult` field is now populated from the output of `ISdJwtService.VerifyPresentationAsync(request.VpToken, issuerPublicKey, algorithm, ct)` rather than from claim values read out of `credential.ClaimsJson` on the server side. The `VerificationResult` shape itself is unchanged.

**Validation rules** (behavioural):

- If `ISdJwtService.VerifyPresentationAsync` returns `IsValid = false`, the `PresentationRequest.Status` transitions to `Denied` and `VerificationResult.Errors` contains the errors from the verification outcome. Server-side claim values MUST NOT appear in `VerificationResult.VerifiedClaims`.
- If `IsValid = true`, `VerificationResult.VerifiedClaims` is populated from the verified presentation's disclosed claims, not from `credential.ClaimsJson`.
- The `IssuerDid` comparison now compares the verified token's `iss` claim against the recorded credential's `IssuerDid` field; a mismatch fails the request with an issuer mismatch error (FR-004).

**State transitions** (unchanged from spec 039):

```text
Pending ─── submit(valid)    → Submitted ─── verify(success)    → Verified
   │                              │
   │                              └── verify(failure)            → Denied
   ├── submit(invalid)             ──────────────────────────────→ Denied
   ├── timeout                     ──────────────────────────────→ Expired
   └── deny                        ──────────────────────────────→ Denied
```

### 4. `CredentialEntity` (unchanged shape, additional invariant)

**Where it lives**: `Sorcha.Wallet.Core.Domain.Entities.CredentialEntity` at `src/Core/Sorcha.Wallet.Portable/Domain/Entities/CredentialEntity.cs`.

**What changes**: no field additions, no field removals. New invariant: if a credential is issued after this spec ships, `RawToken`'s signed payload contains a `credentialStatus` claim, and the `StatusListUrl` / `StatusListIndex` fields on the entity row MUST agree with the claim's `statusListCredential` / `statusListIndex`. These were previously written post-hoc; now they are written in lockstep at issuance time.

**Validation rules**:

- For credentials with `CreatedAt >= 2026-04-10` (or whatever ship date), both the server-side row and the in-payload claim MUST exist and agree.
- For credentials with `CreatedAt < 2026-04-10`, the server-side row is authoritative (the in-payload claim does not exist). This is the pre-fix fallback from FR-010.

## Relationships

All relationships are unchanged from the pre-fix state. No new foreign keys, no new joins.

- `CredentialEntity` still belongs to a `WalletAddress` and references `IssuerDid` and `SubjectDid` as string DID URIs.
- `PresentationRequest` still references a credential by ID (looked up via `ICredentialStore`).
- `DidDocument` still carries a list of `VerificationMethod` entries; this spec corrects the encoding of one field within each entry.

## Migration notes

No EF Core migration is required. No schema changes. No data backfill.

- Historic credentials retain their pre-fix payload shape (no `credentialStatus` claim) and the verifier falls back to the server-side row per FR-010.
- Historic `PresentationRequest` records are in memory only (`PresentationRequestService._requests`) and do not persist across a service restart. The fix takes effect on the first request submitted after the service restarts with the fix deployed.
- The `Multicodec` helper is new code in `Sorcha.Cryptography.Utilities`; no persisted data depends on it.

## Summary

The data-level impact of this spec is small and entirely additive or corrective:

- One new non-disclosable claim inside newly signed credential payloads (`credentialStatus`).
- One corrected encoding inside DID documents (`publicKeyMultibase`).
- One corrected source-of-truth for claim values inside `VerificationResult`.

No schema changes, no migrations, no breaking changes, no backwards-incompatible behaviour.
