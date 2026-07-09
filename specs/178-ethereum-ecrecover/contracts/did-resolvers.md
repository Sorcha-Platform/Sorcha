# Contract: `PkhDidResolver` + `EthrDidResolver`

**Project**: `src/Common/Sorcha.ServiceClients.Http/Did` — both `IDidResolver`, offline, registered in
`AddDidResolvers`.

## `PkhDidResolver` (`CanResolve("pkh")`)

| Input | Output |
|---|---|
| `did:pkh:eip155:1:0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B` | `DidDocument` with one VM (below). |
| wrong namespace (`did:pkh:bip122:…`), bad chainId, address ≠ 40 hex, extra segments | `null` |

VM emitted:
```json
{
  "id": "{did}#blockchainAccountId",
  "type": "EcdsaSecp256k1RecoveryMethod2020",
  "controller": "{did}",
  "blockchainAccountId": "eip155:1:0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B"
}
```
`authentication` = `assertionMethod` = `["{did}#blockchainAccountId"]`. No `publicKeyJwk`.

## `EthrDidResolver` (`CanResolve("ethr")`)

| Input | Normalised chainId | Output |
|---|---|---|
| `did:ethr:0x{40hex}` | 1 (mainnet default) | default-doc VM |
| `did:ethr:mainnet:0x{40hex}` | 1 | default-doc VM |
| `did:ethr:sepolia:0x{40hex}` | 11155111 | default-doc VM |
| `did:ethr:0x89:0x{40hex}` (hex chain-id) | 137 | default-doc VM |
| unknown network name; non-address-form / would need registry read | — | `null` |

VM emitted (ERC-1056 default document):
```json
{
  "id": "{did}#controller",
  "type": "EcdsaSecp256k1RecoveryMethod2020",
  "controller": "{did}",
  "blockchainAccountId": "eip155:{chainId}:0x{addr}"
}
```
`authentication` = `assertionMethod` = `["{did}#controller"]`. No `publicKeyJwk`.

**Phase 2b seam**: optional ctor `IEvmRpcClient? rpc = null`; null ⇒ the default document above.

## Test contract

- Each row above resolves to the stated VM (type, id fragment, `blockchainAccountId`,
  `assertionMethod` membership).
- Malformed / unsupported inputs return `null` with **no** network call (offline assertion).
- `did:jwk` / `did:key` behaviour is unaffected (regression).
