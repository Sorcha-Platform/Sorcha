# Data Model: Cross-node submission round-trip (Stage 5)

Phase 1 data shapes. These are additions/changes to existing models — no new persistent store
beyond what's noted. Field names are indicative; exact casing follows the host model's convention.

## 1. `HolderKeys` — the carried delivery-key field value (C3)

The value written by the `sorcha-holder-key` form field into the starting-action payload, carried
into replicated register state.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `holderJwk` | object (JWK) | yes | Slot-108 holder public key for the SD-JWT `cnf` binding. `{ kty, crv: "P-256"\|"Ed25519", x, y }`. Public only. |
| `encryptionPublicKey` | string (base64) | yes (v1) | Wallet public key used to wrap the on-register AEAD envelope. For ED25519 wallets this is derivable by the owner from the tx signature; carried for algorithm-agnostic robustness. |
| `algorithm` | string | yes | Wallet network/algorithm for `encryptionPublicKey` (`ED25519` \| `NISTP256`) — feeds `ExternalKeyInfo.Algorithm`. |

- **Provenance**: client-autofilled, read-only to the user. Private keys never present.
- **Validation**: `holderJwk` is a well-formed JWK of an allowed curve; `encryptionPublicKey` is valid base64 of the expected length for `algorithm`. Missing/malformed → the credential-issuance step fails closed (FR-012).
- **Schema declaration** (blueprint action): `{ "type":"object", "format":"sorcha-holder-key", "x-holder-key": { "required": true } }`.

## 2. `x-holder-key` schema extension (optional parser)

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| `required` | bool | true | Whether the field must be present for submission. |

Tolerated by the generic `x-` strip in `ValidationEngine`; no allowlist change. A
`HolderKeySchemaExtension.TryParseFromSchema` mirroring `FileSchemaExtension` is optional (only if
config grows beyond `required`).

## 3. Wallet-Service public-key endpoint DTOs (C3)

Response of the new `GET /api/v1/wallet/holder-keys` (citizen JWT, consumer-tier):

| Field | Type | Notes |
|-------|------|-------|
| `holderJwk` | object (JWK) | From `HolderKeyService.GetHolderPublicJwkAsync(walletAddress)` (slot 108). |
| `encryptionPublicKey` | string (base64) | Wallet X25519/Ed25519-derived public key. |
| `algorithm` | string | `ED25519` \| `NISTP256`. |
| `walletAddress` | string | The citizen's resolved wallet address. |

No request body (identity from JWT). Idempotent, cacheable client-side for the form session.

## 4. Issuance-request additions (C3 — the `cnf` binding)

Net-new fields threaded through the issuance chain so a *bound* credential is produced:

- `CredentialIssuanceConfig.HolderKeySourceField` (string, JSON Pointer, e.g. `/holderKeys/holderJwk`) — where the issuer reads the recipient holder JWK from reconstructed instance state. Optional; absent → no `cnf` (current behaviour).
- `IssueCredentialRequest.HolderJwk` (object, JWK, nullable) — passed to `SdJwtService.CreateTokenAsync(holderJwk:)`.
- `IWalletServiceClient.IssueCredentialAsync(...)` gains the `holderJwk` parameter.

Resolution precedence at issuance (FR-012), in `ActionExecutionService.IssueCredentialFromActionAsync`:

```
1. published participant record (register, replicated)  → authoritative
2. carried HolderKeys field (reconstructed instance state) → fallback
3. neither → fail closed (no credential)
```

For the AEAD envelope, the resolved `encryptionPublicKey` (or derived) is injected into
`request.ExternalRecipientKeys[recipientWallet] = { PublicKey, Algorithm }`; the existing
`ResolveRecipientKeysAsync` pipeline consumes it. Honour "published wins" by injecting the carried
key only when the register lookup misses (or reordering the existing external-first precedence).

## 5. Transaction metadata addition (C5)

`TransactionMetaData.NextActionId` (already exists on the type, `TransactionMetaData.cs:41`) MUST be
populated by the authoritative projection in `DocketBuildTriggerService` (`:593-608`) so the mirror
can seed `CurrentActionIds`. Value = the next action id implied by the sealed transaction's routing.

## 6. Mirror Instance changes (C5)

`InstanceMirrorReconstructor` upserts a read-only mirror `Instance` (`IsReadOnlyMirror=true`). **v1 decision (locked): Fix 1a + Fix 2a.**

- **`CurrentActionIds` (Fix 1a)**: seed from the now-populated `NextActionId` written by the authoritative projection (§5). Today it falls back to `[]`, blocking the analyst's submission. **Out of v1**: deriving the next action from blueprint routing (Fix 1b) and retiring the reconstructor's `:253-263` self-keyed-`ParticipantWallets` TODO — deferred to backlog.
- **Submission path (Fix 2a)**: `ActionExecutionService.ExecuteAsync` becomes mirror-aware — a submission against a mirror advances state via `UpdateMirrorAsync` (register-driven) instead of the guarded `UpdateAsync`, preserving the F106 invariant that mirrors are not locally authoritative while letting the owner act and re-derive on the sealed result. **Not** done by relaxing the write guard (Fix 2b, rejected).

No schema migration: `IsReadOnlyMirror` and `NextActionId` already exist; this is population + branching logic.

## 7. Event constant (C2)

`RegisterEventChannels.RegisterCreated = "register:created"` — replaces the two inline literals
(`RegisterManager.cs:85`, `RegisterEventBridgeService.cs:35`). Payload `RegisterCreatedEvent{RegisterId,…}`
(unchanged) drives per-register blueprint recovery in `BlueprintRecoveryService`.

## Entity relationships (round-trip view)

```
Citizen wallet (replica) ──signs──> Starting-action tx ──carries──> HolderKeys + instanceId
        │                                   │
        │ (X25519 priv)                     ▼ fan-out (F108)
        │                          Owner validator ──seals──> Docket ──replicates──> both nodes
        ▼                                   │
   InboundCredentialDetector  <──AEAD──  Credential-issuance tx (owner)
        │                                   ▲ binds cnf=holderJwk, wraps to encryptionPublicKey
        ▼                                   │
   CitizenInboxProjector → WalletHub → PWA  Analyst (owner) approves via mirror-aware submit
```
