# Contract: `EthrDidResolver` (RPC path) + verification reuse

**Project**: `src/Common/Sorcha.ServiceClients.Http/Did/EthrDidResolver.cs` — gains the
`IEvmRpcClient? rpc = null` seam and the multi-VM current-document builder.

## Resolution decision

| Condition | Output |
|---|---|
| `rpc == null` (WASM / no server RPC) | Phase-2 **offline default document** (unchanged) |
| `rpc != null`, `Erc1056State.NoHistory` (changed==0 / unconfigured chain) | offline default document |
| `rpc != null`, `Erc1056State.RpcError` (configured but errored) | **null** (fail closed — FR-006) |
| `rpc != null`, `Ok` | **current multi-VM document** (below) |

## Current document VMs

| Source | id | type | payload | in |
|---|---|---|---|---|
| owner | `{did}#controller` | `EcdsaSecp256k1RecoveryMethod2020` | `blockchainAccountId eip155:{chain}:{owner}` | authentication, assertionMethod |
| `veriKey` delegate | `{did}#delegate-{n}` | `EcdsaSecp256k1RecoveryMethod2020` | `blockchainAccountId eip155:{chain}:{addr}` | assertionMethod |
| `sigAuth` delegate | `{did}#delegate-{n}` | `EcdsaSecp256k1RecoveryMethod2020` | `blockchainAccountId eip155:{chain}:{addr}` | authentication |
| Secp256k1 attr | `{did}#delegate-{n}` | `JsonWebKey2020` | `publicKeyJwk {kty:EC,crv:secp256k1,x,y}` | per purpose |
| Ed25519 attr | `{did}#delegate-{n}` | `JsonWebKey2020` | `publicKeyJwk {kty:OKP,crv:Ed25519,x}` | per purpose |

`did:pkh` and the offline default did:ethr path are unchanged.

## Verification reuse (no change)

- `DidResolverBackedIssuerKeyResolver` / `DidX5cIssuerKeyResolver`: kid-exact match selects the signing
  VM (Veramo sets `kid`); no-kid → first authorised VM (documented best-effort). A key VM → Phase-1
  key-match; an address VM → Phase-2 recover-then-match. The **assertionMethod gate** ensures only
  currently-authorised keys verify — so a rotated-away owner or expired delegate (absent from the doc)
  cannot verify, and a `sigAuth`-only delegate (authentication) is rejected for issuer assertion.
- Trust (allowlist / `WarnOnUnlistedVerifiedIssuer`) unchanged.

## End-to-end test contract (fake RPC)

- Rotated DID, credential signed by **current owner** → **Pass**; by **former owner** → **Reject**.
- Credential signed by an unexpired **`veriKey` delegate** → **Pass**; by an **expired** delegate → **Reject**.
- Credential signed by a published **Secp256k1 / Ed25519** key attribute → **Pass**.
- Configured-RPC **error** → **Reject** (no stale doc); **unconfigured** chain / **no history** → default document (Phase-2 behaviour).
- WASM composition (`rpc == null`) → default document, **zero** network calls.
