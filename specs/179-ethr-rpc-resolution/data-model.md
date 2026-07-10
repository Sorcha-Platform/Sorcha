# Phase 1 Data Model: `did:ethr` on-chain resolution

Resolution logic — no persisted entities. The "model" is the transient types carrying ERC-1056 state
into the existing `DidDocument` shape. **`DidDocument` / `VerificationMethod` are reused unchanged**
(they already carry `PublicKeyJwk` and `BlockchainAccountId` from Phase 1/2).

## New types (all in `Sorcha.ServiceClients.Http/Evm`)

### `IEvmRpcClient` / `EvmRpcClient`

```csharp
public interface IEvmRpcClient
{
    /// eth_call at latest block. Returns hex result, null if the chain is UNCONFIGURED,
    /// or throws-internally→returns an error sentinel distinct from unconfigured.
    Task<EvmCallResult> CallAsync(long chainId, string to, string dataHex, CancellationToken ct);

    /// eth_getLogs for a single block, filtered by address + topics.
    Task<EvmLogsResult> GetLogsAsync(long chainId, string address, string?[] topics, long block, CancellationToken ct);
}
```

`EvmCallResult` / `EvmLogsResult` each distinguish three outcomes (critical for FR-006/FR-007):

| Outcome | Meaning |
|---|---|
| `NotConfigured` | No RPC URL for the chain → caller uses the offline default document |
| `Error` | Configured but timeout/network/SSRF-blocked/malformed → caller **fails closed** |
| `Ok(value)` | Hex result / decoded logs |

### `EvmRpcOptions` (bound from `DidResolver:Ethr`)

| Field | Config key | Notes |
|---|---|---|
| `Rpc` | `DidResolver:Ethr:Rpc:{chainId}` | chainId → RPC URL map |
| `RegistryAddress` | `DidResolver:Ethr:RegistryAddress:{chainId}` | optional per-chain override; default `0xdca7…f21b` |
| `AllowPrivateAddresses` | `DidResolver:AllowPrivateAddresses` | shared with `WebDidResolver` |
| `MaxHistoryHops` | `DidResolver:Ethr:MaxHistoryHops` | default 128; exceed → fail closed |

### `AbiCodec` (static)

`Selector(string sig)`, `EventTopic(string sig)` (via `Keccak256`); `EncodeAddress`, `DecodeAddress`,
`DecodeUInt` (`BigInteger`), `DecodeBytes32`, `DecodeBytes`, `Pad32Topic(address)`.

### `Erc1056Registry` — resolution result

```csharp
// Returned by the reader; consumed by EthrDidResolver to build the document.
public sealed class Erc1056State
{
    public bool RpcError { get; init; }          // configured-but-errored → resolver returns null
    public bool NoHistory { get; init; }         // changed==0 or unconfigured → default document
    public string OwnerAddress { get; init; }     // current owner (rotation)
    public IReadOnlyList<Erc1056Delegate> Delegates { get; init; } // active veriKey/sigAuth (address)
    public IReadOnlyList<Erc1056KeyAttribute> Attributes { get; init; } // active did/pub/* keys
}

public sealed record Erc1056Delegate(string DelegateType, string Address);          // "veriKey"|"sigAuth"
public sealed record Erc1056KeyAttribute(string Algo, string Purpose, byte[] Key);   // Secp256k1/Ed25519
```

Invariants: entries present only if `validTo >= now`; newest-first supersession applied; `enc`/service
attributes excluded.

## Mapping ERC-1056 state → `DidDocument` (in `EthrDidResolver`)

| ERC-1056 entity | VM id | VM type | Payload | Relationships |
|---|---|---|---|---|
| current owner | `{did}#controller` | `EcdsaSecp256k1RecoveryMethod2020` | `blockchainAccountId = eip155:{chain}:{owner}` | authentication + assertionMethod |
| `veriKey` delegate | `{did}#delegate-{n}` | `EcdsaSecp256k1RecoveryMethod2020` | `blockchainAccountId = eip155:{chain}:{addr}` | assertionMethod |
| `sigAuth` delegate | `{did}#delegate-{n}` | `EcdsaSecp256k1RecoveryMethod2020` | `blockchainAccountId = eip155:{chain}:{addr}` | authentication |
| Secp256k1 key attr | `{did}#delegate-{n}` | `JsonWebKey2020` | `publicKeyJwk {kty:EC,crv:secp256k1,x,y}` | per purpose |
| Ed25519 key attr | `{did}#delegate-{n}` | `JsonWebKey2020` | `publicKeyJwk {kty:OKP,crv:Ed25519,x}` | per purpose |

The issuer-key resolvers + verify branches consume this exactly as Phase 1 (key VMs) / Phase 2 (recovery
VMs) — no change.

## Decision flow (`EthrDidResolver.ResolveAsync`)

```
rpc == null                         → Phase-2 offline default document
rpc != null:
  Erc1056Registry.ReadAsync(...)
    RpcError    → null              (FAIL CLOSED — FR-006)
    NoHistory   → default document  (FR-007/FR-008)
    Ok(state)   → build multi-VM current document from owner+delegates+attributes
```
