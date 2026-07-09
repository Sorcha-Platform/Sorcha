# Phase 0 Research: `did:ethr` on-chain resolution (Phase 2b)

Confirmed against **EIP-1056** (event signatures, registry address) and the **DIF `did-ethr` method
spec / `ethr-did-resolver`** (the de-facto resolution conventions). Line numbers indicative.

## R1 — ERC-1056 contract surface (verbatim from EIP-1056)

Registry: **`EthereumDIDRegistry`**, canonical address **`0xdca7ef03e98e0dc2b855be647c39abe984fcf21b`**
(mainnet + most testnets; per-chain override via config for chains that differ).

Events (exact):
```solidity
event DIDOwnerChanged(address indexed identity, address owner, uint previousChange);
event DIDDelegateChanged(address indexed identity, bytes32 delegateType, address delegate, uint validTo, uint previousChange);
event DIDAttributeChanged(address indexed identity, bytes32 name, bytes value, uint validTo, uint previousChange);
```
Functions:
- `identityOwner(address identity) view returns (address)` — current owner (returns `identity` if never changed).
- `changed(address identity) view returns (uint256)` — public mapping getter; block of the most recent change, `0` if none.

**ABI canonicalisation for selectors/topics**: `uint` → `uint256`. So:
- selector `changed(address)` = `Keccak256("changed(address)")[..4]`
- selector `identityOwner(address)` = `Keccak256("identityOwner(address)")[..4]`
- topic0 `DIDOwnerChanged` = `Keccak256("DIDOwnerChanged(address,address,uint256)")`
- topic0 `DIDDelegateChanged` = `Keccak256("DIDDelegateChanged(address,bytes32,address,uint256,uint256)")`
- topic0 `DIDAttributeChanged` = `Keccak256("DIDAttributeChanged(address,bytes32,bytes,uint256,uint256)")`
- `identity` is `indexed` ⇒ it is **topic1** (`0x` + 12 zero bytes + 20-byte address). The non-indexed
  params are ABI-encoded in the log `data`.

## R2 — The event walk-back

1. `eth_call` `changed(identity)` → last-change block `B`. `B == 0` ⇒ **no history** → default document.
2. `eth_call` `identityOwner(identity)` → current owner address.
3. Loop from `B`: `eth_getLogs { address: registry, fromBlock: B, toBlock: B, topics: [null, pad32(identity)] }`.
   Decode each log by `topics[0]`; take `previousChange` (the **last** 32-byte word of `data`) as the
   next `B`; repeat until `B == 0`.
4. Fold **newest-first**: keep the current owner (from step 2), and the first-seen (newest) entry per
   `(delegateType, delegate)` / per attribute `name+value` — **including** an entry only if its
   `validTo` (seconds) `>= now`. Superseded/expired entries are dropped.

Bounded-walk guard: cap the number of hops (e.g. 128) and fail-closed if exceeded — a defensive bound
against a pathological history, logged.

## R3 — Delegate types → DID relationships (did-ethr spec)

Both delegate types are addresses and both emit an `EcdsaSecp256k1RecoveryMethod2020` VM with a CAIP-10
`blockchainAccountId` (Phase-2 recover-then-match), differing only in relationship:
- **`veriKey`** (bytes32 = ASCII `"veriKey"` right-zero-padded, `0x766572694b6579000…`) → VM in
  `verificationMethod` **and `assertionMethod`**.
- **`sigAuth`** (`0x73696741757468000…`) → VM in `verificationMethod` **and `authentication`**.

For **issuer** signature verification (the assertionMethod gate), only owner (`#controller`, in both)
and `veriKey` delegates authorise a signature; `sigAuth`-only delegates are for authentication and are
correctly rejected by the existing assertionMethod gate. We still emit both with correct relationships
and let the gate filter — no special-casing.

## R4 — Attribute name grammar → DID VMs (did-ethr spec)

`DIDAttributeChanged.name` (bytes32, ASCII) grammar: `did/pub/{algo}/{purpose}/{encoding}`:
- `algo` ∈ `Secp256k1 | Ed25519 | X25519 | …`; `purpose` ∈ `veriKey | sigAuth | enc`; `encoding` ∈ `hex | base64 | base58`.
- **Secp256k1** value (the EC point bytes) → a `publicKeyJwk` `{kty:EC, crv:secp256k1, x, y}` via the
  primitive (`Secp256k1PublicKey.FromSec1` handles compressed/uncompressed → `Secp256k1Jwk`).
- **Ed25519** value (raw 32-byte key) → a `publicKeyJwk` `{kty:OKP, crv:Ed25519, x}` (base64url of the
  key). *Note*: the did-ethr spec emits Ed25519 as `publicKeyMultibase`, but Sorcha's issuer-key
  resolvers consume `publicKeyJwk`, so we emit the JWK form our verify path can key-match.
- `purpose` sets the relationship: `veriKey` → assertionMethod, `sigAuth` → authentication.
- **`enc` (X25519 / key-agreement) and any non-signing attribute are OUT of scope** (FR-014) — skipped.
- `value` bytes are decoded per `encoding` (hex → `Convert.FromHexString` after `0x` strip; base64 →
  `Convert.FromBase64String`; base58 → SimpleBase, already referenced).

## R5 — Default document (no history) = Phase 2

`changed == 0` (or unconfigured chain) → the existing Phase-2 default document: one
`EcdsaSecp256k1RecoveryMethod2020` VM `{did}#controller`, `blockchainAccountId = eip155:{chain}:{addr}`,
in authentication + assertionMethod. `EthrDidResolver` already builds this; the RPC path reuses it.

## R6 — Server-side-only registration + the fail-closed rule

- **Registration**: `IEvmRpcClient` is added in server-host DI only (where `AddDidResolvers` /
  `AddServiceClients` runs for a service). The WASM PWA composition does not register it, so
  `EthrDidResolver` resolves with `rpc == null` → default document. A test asserts the null-rpc path.
- **Fail-closed (FR-006/FR-007)**: distinguish *unconfigured* (no URL for the chain → `EvmRpcClient`
  returns a sentinel "not configured" → default document) from *configured-but-errored* (URL present,
  call throws/times-out/malformed → **null resolution → reject**). `Erc1056Registry` surfaces the two
  cases distinctly (e.g. a nullable result + an `RpcError` flag) so the resolver never emits a stale
  default doc on a real error.

## R7 — SSRF, timeout, config (reuse `WebDidResolver`)

- Reuse `WebDidResolver.IsPrivateOrReservedAddress` (make it the shared guard) + a 5s timeout +
  `DidResolver:AllowPrivateAddresses` for local dev nodes.
- Config: `DidResolver:Ethr:Rpc:{chainId}` → RPC URL; `DidResolver:Ethr:RegistryAddress:{chainId}` →
  optional address override (default canonical). Bound via `EvmRpcOptions`.

## R8 — No ABI library; KAT the keccak machinery

Selectors/topics are computed with the primitive's `Keccak256`. Anchor the machinery with a KAT against
a **published** hash independent of our code — `Keccak256("Transfer(address,address,uint256)")` ==
`0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef` (the canonical ERC-20 Transfer
topic) — then compute the ERC-1056 selectors/topics from their signature strings with the same code.
This proves correctness without hard-coding possibly-wrong ERC-1056 constants.

## Open items deliberately NOT resolved (out of scope)

Service endpoints / `enc` attributes / non-signing data; versioned (`versionId`/`versionTime`)
resolution; browser/WASM RPC; caching-TTL tuning for on-chain state; any RPC method beyond `eth_call` +
`eth_getLogs`; signing / transacting / Nethereum.
