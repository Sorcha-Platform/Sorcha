# Phase 1 Data Model: Ethereum address-form issuer DID verification

This feature is verification/resolution logic — no persisted entities. The "data model" is the set of
in-memory types and the one new field that carry an **address-form issuer identity** through the
existing pipeline.

## New / changed types

### `VerificationMethod.BlockchainAccountId` (new field)

`src/Common/Sorcha.ServiceClients.Http/Did/DidDocument.cs`

| Field | Type | Notes |
|---|---|---|
| `BlockchainAccountId` | `string?` | CAIP-10 account id, e.g. `eip155:1:0x1234…`. `[JsonPropertyName("blockchainAccountId")]`, null-omitted. Set **only** on an `EcdsaSecp256k1RecoveryMethod2020` VM; mutually exclusive with `PublicKeyJwk` in practice. |

Validation/invariants:
- A VM is "address-form" iff `BlockchainAccountId is not null && PublicKeyJwk is null`.
- Existing key-form VMs never set it (purely additive; no migration).

### `IssuerKeyResolution.BlockchainAccountId` (new field)

`src/Core/Sorcha.Blueprint.Engine/Credentials/IIssuerKeyResolver.cs`

| Field | Type | Notes |
|---|---|---|
| `BlockchainAccountId` | `string?` | Set by `DidX5cIssuerKeyResolver` when the matched VM is address-form. When set, `PublicKey` is `[]` and `Algorithm` is `"ES256K"`. Consumers verify by address-recovery instead of key-match. |

Invariants:
- Exactly one of (`PublicKey` non-empty) / (`BlockchainAccountId` non-null) is populated for an
  address-or-key issuer resolution. X5c path is unchanged (always key/cert bytes).

### Recovery-JWK envelope (Verifier engine, transient `JsonElement`)

Not a declared type — a JWK-shaped `JsonElement` synthesised by `DidResolverBackedIssuerKeyResolver`
for an address-form VM:

```json
{ "kty": "EC", "crv": "secp256k1", "blockchainAccountId": "eip155:1:0x1234…" }
```

- Recognised by `VerifiablePresentationValidator.VerifyEs256k`: absence of `x`/`y` + presence of
  `blockchainAccountId` ⇒ address-recovery path.

## New primitive API (pure-managed, `Sorcha.Cryptography.Secp256k1`)

### `Secp256k1Recovery` (new static)

```csharp
public static class Secp256k1Recovery
{
    /// <summary>Recover candidate secp256k1 public keys from a JOSE ES256K signature
    /// (64-byte r‖s, no recid) over SHA-256(message). Tries recovery ids 0 and 1.
    /// Returns 0–2 valid candidates; never throws.</summary>
    public static IReadOnlyList<Secp256k1PublicKey> TryRecover(
        ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature);
}
```

### `Secp256k1Verifier.VerifyByAddress` (new; instance + static)

```csharp
// On ISecp256k1Verifier and as a static entry point (mirrors VerifyEs256k):
bool VerifyByAddress(ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, string expectedAddress);
public static bool VerifyByAddressStatic(ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, string expectedAddress);
```

- Recovers candidates, derives `EthereumAddress.FromPublicKey` for each, returns true iff any matches
  `expectedAddress` case-insensitively. `expectedAddress` may be the bare `0x…` address or a CAIP-10
  `eip155:{chain}:0x…` (the method extracts the trailing `0x{40hex}`). Never throws → false on any
  malformed input.

## New DID resolvers (`Sorcha.ServiceClients.Http/Did`)

| Type | `CanResolve` | Input → Output |
|---|---|---|
| `PkhDidResolver` | `"pkh"` | `did:pkh:eip155:{chainId}:0x{40hex}` → `DidDocument` with one `EcdsaSecp256k1RecoveryMethod2020` VM (`#blockchainAccountId`, `blockchainAccountId = eip155:{chainId}:0x…`), in `authentication` + `assertionMethod`. Malformed → null. |
| `EthrDidResolver` | `"ethr"` | address-form `did:ethr` (bare / named-network / hex-chain-id) → default-document `EcdsaSecp256k1RecoveryMethod2020` VM (`#controller`, normalised `blockchainAccountId`), in `authentication` + `assertionMethod`. Non-default-doc / unknown network → null. Optional `IEvmRpcClient` seam (null this phase). |

Both register in `HttpServiceCollectionExtensions.AddDidResolvers` alongside `KeyDidResolver` /
`JwkDidResolver`.

## State / flow

No state machine. The single decision each verify site makes:

```
resolve issuer DID → matched VM
  ├─ has publicKeyJwk (x/y)      → ES256K key-match   (Phase 1, unchanged)
  ├─ has blockchainAccountId     → ES256K recover+match (Phase 2, this feature)
  └─ neither                     → reject (unresolved)
```
