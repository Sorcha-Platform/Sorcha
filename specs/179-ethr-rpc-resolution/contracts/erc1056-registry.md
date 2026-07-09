# Contract: `Erc1056Registry`

**Project**: `src/Common/Sorcha.ServiceClients.Http/Evm` — the ERC-1056 read + event walk. Pure logic
over an injected `IEvmRpcClient`.

## `ReadAsync(chainId, identityAddress, ct) : Erc1056State`

| Step | RPC | Result handling |
|---|---|---|
| 1. last-change block | `eth_call changed(identity)` | `NotConfigured` → `Erc1056State{NoHistory=true}`; `Error` → `{RpcError=true}`; `Ok(0)` → `{NoHistory=true}`; `Ok(B>0)` → continue |
| 2. current owner | `eth_call identityOwner(identity)` | `Error` → `{RpcError=true}`; `Ok(addr)` → owner |
| 3. walk | `eth_getLogs [B,B] topics=[null,pad32(identity)]` | `Error` → `{RpcError=true}`; decode; next `B = previousChange`; repeat until `B==0` or `MaxHistoryHops` exceeded (→ `{RpcError=true}`, logged) |
| 4. fold | — | newest-first; keep entries with `validTo >= now`; supersession by `(type,delegate)`/`(name,value)` |

Returns:
```csharp
Erc1056State { RpcError, NoHistory, OwnerAddress, Delegates[], Attributes[] }
```

## Decoding rules

- `DIDOwnerChanged(identity, owner, previousChange)` — `data` = `owner`(32) ‖ `previousChange`(32). Newest owner wins (but step 2's `identityOwner` is authoritative for the *current* owner).
- `DIDDelegateChanged(identity, delegateType, delegate, validTo, previousChange)` — decode `delegateType`(bytes32 ASCII → trim NULs), `delegate`(address), `validTo`(uint). Keep if `validTo >= now` and type ∈ {`veriKey`,`sigAuth`}.
- `DIDAttributeChanged(identity, name, value, validTo, previousChange)` — decode `name`(bytes32 ASCII), `value`(dynamic bytes), `validTo`. Parse `name` = `did/pub/{algo}/{purpose}/{encoding}`; keep signing algos (`Secp256k1`,`Ed25519`) with `validTo >= now`; decode `value` per `encoding`; skip `did/svc/*`, `enc`, and unknown algos.

## Test contract (fake `IEvmRpcClient`)

- `changed==0` → `NoHistory`.
- Owner rotation (one `DIDOwnerChanged`) → `OwnerAddress` = the new owner.
- Active `veriKey` delegate (`validTo` future) → one delegate; **expired** (`validTo` past) → none.
- `did/pub/Secp256k1/veriKey/hex` attribute → one Secp256k1 key attr; `did/pub/Ed25519/veriKey/base64` → one Ed25519 attr; `did/svc/...` and `did/pub/X25519/enc/...` → ignored.
- Supersession: a later change overriding an earlier delegate keeps only the newest.
- `Error` from any call → `RpcError=true`; `MaxHistoryHops` exceeded → `RpcError=true`.
- `previousChange` walk terminates at 0.
