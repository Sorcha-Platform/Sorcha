# Ethereum-key VC Verification — Phase 2b (`did:ethr` on-chain resolution via read-only EVM RPC)

- **Date:** 2026-07-09
- **Status:** Design approved (brainstorm), pending implementation plan
- **Scope:** Phase 2b of the 4-phase Ethereum interoperability roadmap (Feature 179)
- **Builds on:** Phase 1 (F177 — the `Sorcha.Cryptography.Secp256k1` primitive, key-form resolvers, ES256K key-match) and Phase 2 (F178 — `PkhDidResolver`/`EthrDidResolver`, `EcdsaSecp256k1RecoveryMethod2020`/`blockchainAccountId`, recover-then-match; the `IEvmRpcClient` seam was designed-in there and is realised here)
- **Related:** DID resolution architecture (`Sorcha.ServiceClients.Did`), `WebDidResolver` SSRF pattern, F120 DID-backed issuer keys, F135 unified trust

---

## 1. Context & motivation

Phase 2 (F178) verifies `did:ethr` in its **default (no-rotation) document** — the address *is* the DID, offline. But `did:ethr` is an **ERC-1056** identity: its real DID document is derived from **on-chain registry state**. An owner can be rotated, signing **delegates** can be added, and **public-key attributes** can be published — all recorded as events on the `EthereumDIDRegistry` contract. A verifier that only knows the default document will:

- **accept a revoked key** — if the owner rotated away, the default document still names the original address, so a signature by the *old* owner would wrongly verify;
- **reject a valid delegate-signed credential** — Veramo (the dominant `did:ethr` issuer) routinely signs with a `sigAuth`/`veriKey` delegate, which the default document does not contain.

Phase 2b closes this by reading the ERC-1056 registry over **read-only EVM RPC** (a configured provider endpoint — **no node, no wallet, no on-chain write**) to build the DID's **current** document, then verifying through the **existing** pipeline.

Roadmap position:

| Phase | Capability | Status |
|---|---|---|
| 1 (F177) | Verify ES256K VCs where the DID resolves offline to a **key** | Shipped (#1140) |
| 2 (F178) | `did:pkh` + address-form `did:ethr` **default doc** via offline `ecrecover` | Shipped (#1141) |
| **2b (this spec)** | **`did:ethr` current document (rotation/delegates/attributes) via read-only EVM RPC** | This spec (F179) |
| 3 | Prove-control / SIWE — expose the wallet ETH address + secp256k1 signing | Deferred |
| 4 | Transacting via Nethereum | Deferred |

**No PII on-chain, ever.** Read-only.

## 2. Goal & non-goals

**Goal.** For a `did:ethr` issuer, Sorcha resolves the **current** DID document from ERC-1056 registry state over read-only EVM RPC and verifies the credential's ES256K issuer signature against the currently-authorised keys — the current owner (rotation), unexpired `sigAuth`/`veriKey` delegates, and unexpired published `did/pub/*` key attributes. Verification flows through the **existing** issuer-key resolvers and verify branches with **no seam changes** — the resolved document uses only the VM shapes Phase 1 (key VMs) and Phase 2 (recovery VMs) already verify.

**Non-goals (deferred or excluded):**

- **RPC in the browser.** Resolution runs **server-side only** (decision §3.1). The Blazor WASM PWA keeps Phase-2 offline behaviour; the server is authoritative for rotation.
- **Service endpoints / non-signing attributes.** Only signing-relevant entities (owner, `sigAuth`/`veriKey` delegates, `did/pub/*` key attributes) are read; service endpoints and other attributes add nothing to signature verification and are excluded.
- **`did:pkh`** — inherently rotation-free; untouched.
- **Holder key-binding by address; secp256k1 signing; EIP-712 / EAS; on-chain writes; Nethereum** — later phases / excluded.
- **A full Ethereum client.** Two JSON-RPC methods only (`eth_call`, `eth_getLogs`); no block sync, no mempool, no tx submission.

## 3. Confirmed design decisions

1. **Zero verify-seam reuse.** The resolved current document maps every ERC-1056 signing entity to an **existing** VM: current owner + each active address delegate → `EcdsaSecp256k1RecoveryMethod2020` VM (`blockchainAccountId`, Phase-2 recover-then-match); each active `did/pub/*` key attribute → a `publicKeyJwk` VM (Phase-1 key-match; secp256k1 hex → secp256k1 JWK, Ed25519 → OKP JWK). All placed in `assertionMethod`. **No change to any issuer-key resolver or verify branch.**
2. **Server-side only RPC.** `EthrDidResolver` gains the optional `IEvmRpcClient? rpc = null` seam; `IEvmRpcClient` is registered **only in server hosts**. WASM ⇒ `rpc == null` ⇒ Phase-2 offline default doc.
3. **Scope = owner + signing keys.** Owner rotation, `sigAuth`/`veriKey` delegates, `did/pub/*` key attributes, each honouring `validTo` expiry. No service endpoints.
4. **Fail-closed on RPC error, not stale.** If RPC is **configured** for a chain but errors (timeout/network/bad response), the resolver returns **null** (reject) — it MUST NOT fall back to the offline default document, which for a rotated DID would accept a revoked key. Only when RPC is **not configured** for the chain does the Phase-2 offline default document apply (the operator's explicit offline choice, same behaviour as F178).
5. **Pure-managed, no new dependency.** JSON-RPC over `HttpClient`; a minimal ABI codec (32-byte-word encode/decode) computes function selectors and event topic hashes with the **existing `Keccak256`** from the primitive. No Nethereum, no ABI library. WASM-safe by construction (though only run server-side).
6. **SSRF-guarded like `WebDidResolver`.** Host-allow check (`IsPrivateOrReservedAddress`), request timeout, `DidResolver:AllowPrivateAddresses` for local dev nodes.
7. **`did:pkh` untouched; recovery/keccak/verify primitives reused as-is; no signing; no `WalletNetworks` change.**

## 4. Where it fits in the codebase (project map)

| Project | Change |
|---|---|
| **`src/Common/Sorcha.ServiceClients.Http`** | **New** `Evm/IEvmRpcClient.cs` + `Evm/EvmRpcClient.cs` (JSON-RPC `eth_call`/`eth_getLogs`, SSRF-guarded). **New** `Evm/AbiCodec.cs` (32-byte-word encode/decode; selector/topic via `Keccak256`). **New** `Evm/Erc1056Registry.cs` (the ERC-1056 read + event walk-back → owner/delegates/attributes). **Edit** `Did/EthrDidResolver.cs` — add the `IEvmRpcClient?` seam + the multi-VM current-document builder. **Edit** `Extensions/HttpServiceCollectionExtensions.cs` — register `IEvmRpcClient` (server hosts) + bind config. |
| **`src/Common/Sorcha.ServiceClients.Http/Did/DidDocument.cs`** | Reused as-is (may already carry everything; `VerificationMethod` already has `PublicKeyJwk` + `BlockchainAccountId`). No model change expected. |
| **Server host wiring** (Blueprint/Verifier/Haip service `Program.cs` or ServiceDefaults DID registration) | Register `IEvmRpcClient` **only server-side** so the WASM PWA stays offline. |
| **Docs** | STANDARDS.md (`did:ethr` row → "on-chain resolution, Feature 179"), `verifiable-credentials` skill, memory. |

> Verify seams (`SdJwtService`, `VerifiablePresentationValidator`, the two issuer-key resolvers, `TrustEvaluator`) are **untouched** by design.

## 5. Architecture

### 5.1 `IEvmRpcClient` / `EvmRpcClient`

A thin read-only JSON-RPC client over `HttpClient`:

- `Task<string?> CallAsync(long chainId, string to, string data, CancellationToken)` → `eth_call` (latest block), returns the hex result or null.
- `Task<IReadOnlyList<EvmLog>?> GetLogsAsync(long chainId, string address, string?[] topics, long fromBlock, long toBlock, CancellationToken)` → `eth_getLogs`.
- Resolves the chain's RPC URL from config (`DidResolver:Ethr:Rpc:{chainId}`); returns null if unconfigured. SSRF host-allow check + 5s timeout mirror `WebDidResolver`. All failures → null (never throws to the caller); the resolver turns a *configured-but-null* result into fail-closed.

### 5.2 `AbiCodec`

Minimal, pure-managed ABI helpers (no library):

- `Selector(string signature)` → first 4 bytes of `Keccak256(signature)` (e.g. `identityOwner(address)`, `changed(address)`).
- `EventTopic(string signature)` → full `Keccak256` (e.g. `DIDDelegateChanged(address,bytes32,address,uint256,uint256)`).
- Word encode/decode: `EncodeAddress`, `DecodeAddress`, `DecodeUInt`, `DecodeBytes32`, `DecodeBytes` (32-byte right/left alignment per the ABI spec). Uses `System.Numerics.BigInteger`.

### 5.3 `Erc1056Registry`

The read logic, given an `IEvmRpcClient`, chainId, registry address, and identity:

1. `changed(identity)` via `eth_call`. `0` → **default document** (no on-chain history) → hand back to the Phase-2 default builder.
2. `identityOwner(identity)` → the current owner address.
3. Walk the event history: starting at the `changed` block, `eth_getLogs` for `[block, block]` filtered by the registry address and `topics = [null, pad32(identity)]`; decode `DIDOwnerChanged`, `DIDDelegateChanged`, `DIDAttributeChanged`; take the `previousChange` from the decoded event(s) as the next block; repeat until `0`.
4. Fold the events **newest-first** into: the current owner (already from step 2), the set of `sigAuth`/`veriKey` delegates with `validTo > now`, and the `did/pub/*` key attributes with `validTo > now` (first-seen-wins per key when walking newest-first; superseded/expired entries dropped).

Registry address: canonical `0xdca7ef03e98e0dc2b855be647c39abe984fcf21b` unless overridden by `DidResolver:Ethr:RegistryAddress:{chainId}`.

### 5.4 `EthrDidResolver` — the current-document builder

- `rpc == null` → the existing Phase-2 offline default document (unchanged).
- `rpc != null` → `Erc1056Registry` read. On a `changed == 0`/no-history result → the same default document. Otherwise build a multi-VM document:
  - **owner** → `{did}#controller`, `EcdsaSecp256k1RecoveryMethod2020`, `blockchainAccountId = eip155:{chain}:{owner}`.
  - **each active address delegate** → `{did}#delegate-{n}`, recovery VM, `blockchainAccountId = eip155:{chain}:{delegate}`.
  - **each active key attribute** → `{did}#delegate-{n}` (or `#key-{n}`), `publicKeyJwk` (secp256k1 hex → `{kty:EC,crv:secp256k1,x,y}` via the primitive's decompress; Ed25519 → `{kty:OKP,crv:Ed25519,x}`).
  - all VM ids in `authentication` + `assertionMethod`.
- On any **configured-RPC error** (`Erc1056Registry` signals a call failure, distinct from a clean `changed==0`) → **return null** (fail-closed, §3.4).

### 5.5 Verification (unchanged)

The multi-VM document flows through `DidResolverBackedIssuerKeyResolver` / `DidX5cIssuerKeyResolver` exactly as Phase 1/2: kid-exact match picks the signing VM (Veramo sets `kid`); a key VM → Phase-1 key-match, an address VM → Phase-2 recover-then-match; the `assertionMethod` gate ensures only currently-authorised keys verify. Trust (allowlist / `WarnOnUnlistedVerifiedIssuer`) is unchanged.

## 6. Data flow (end-to-end)

1. A `did:ethr` ES256K credential is presented; `iss = did:ethr:{chain}:0x…`, `kid = did:ethr:…#controller` (or `#delegate-n`).
2. `EthrDidResolver` (server host, `rpc != null`) reads ERC-1056: `changed` → owner → event walk → active delegates/attributes.
3. It builds the current multi-VM document (recovery VMs for owner/address-delegates, key VMs for published keys), all in `assertionMethod`.
4. The issuer-key resolver kid-matches the signing VM; the verify branch key-matches or recover-matches; a rotated-away owner or expired delegate is simply **absent** from the document → its signature fails to resolve → reject.
5. Trust maps the verified signature → Pass (allowlisted) / Warn (unlisted + flag) / Reject.

## 7. Error handling & safety

- **Configured RPC error** (timeout, network, malformed response, SSRF-blocked host) → resolver returns **null** → reject. Never a stale default doc.
- **`changed == 0`** (no on-chain history) → default document (identical to F178). This is the only path that yields the offline default when RPC *is* configured, and it is safe (the chain confirms no rotation).
- **Unconfigured chain** → offline default document (operator's explicit offline choice; unchanged F178 behaviour).
- **Malformed DID / unknown chain / non-address-form** → null (unchanged).
- All RPC/ABI code is defensive: decode failures → null, never a throw to the pipeline.

## 8. Testing strategy

- **`AbiCodec` unit tests:** `Selector("identityOwner(address)")` / `changed(address)` against known 4-byte selectors; `EventTopic` against the known `DIDDelegateChanged`/`DIDOwnerChanged`/`DIDAttributeChanged` topic hashes; address/uint/bytes32 word round-trips against fixtures.
- **`Erc1056Registry` unit tests:** with a **fake `IEvmRpcClient`** returning canned `eth_call`/`eth_getLogs` payloads — `changed==0` → default; owner rotation → new owner VM; an active `sigAuth` delegate → delegate VM; an expired delegate (`validTo < now`) → dropped; a `did/pub/Secp256k1/veriKey/hex` attribute → key VM; the `previousChange` walk terminates.
- **`EthrDidResolver` integration (fake RPC):** rotated DID → current-owner recovery VM in `assertionMethod`; delegate-signed credential → verifies against the delegate VM; RPC error (configured) → null (fail-closed); unconfigured chain → default document.
- **End-to-end verify (both surfaces, fake RPC):** a Veramo-style ES256K JWT-VC signed by a `veriKey` delegate → **Pass** (allowlisted) through the engine and verifier; a signature by the rotated-away old owner → **Reject**.
- **`EvmRpcClient` SSRF/timeout tests:** private-address host blocked; timeout → null; mirrors `WebDidResolver` tests.
- **Regression:** every Phase-1/Phase-2 suite green; `did:pkh` unaffected; the WASM/offline default-doc path (`rpc == null`) still yields the Phase-2 document.

## 9. Seams for later phases (designed-in, not built)

- **Phase 3 (SIWE / signing):** recoverable secp256k1 sign in the primitive; expose the wallet ETH address. The `IEvmRpcClient` here is read-only and does not become a signer.
- **Phase 4 (transacting):** Nethereum for RLP/ABI/tx submission, a separate heavier dependency; the read-only `IEvmRpcClient` and `AbiCodec` remain the verification-side path.
- **Caching:** DID-document caching already exists at the registry layer (`DidResolverCache`); ERC-1056 reads inherit it. A shorter TTL for on-chain-resolved `did:ethr` (rotation can happen any block) can be tuned later.

## 10. Risks & assumptions to verify during planning

1. **ABI selector / event-topic exactness.** The 4-byte selectors and 32-byte event topics must match ERC-1056 exactly; anchor each with a known-vector test before wiring the registry read.
2. **Event walk-back correctness.** `previousChange` chaining and newest-first folding (with `validTo` expiry and supersession) is the subtle core; the fake-RPC `Erc1056Registry` tests must cover rotation-then-rotation, delegate add-then-expire, and attribute supersession.
3. **Delegate-type / attribute-name encodings.** `sigAuth`/`veriKey` bytes32 encodings and the `did/pub/{Algo}/{Purpose}/{Encoding}` attribute-name grammar (hex/base64 value decoding) must be parsed exactly; confirm against the ethr-did-resolver reference during research.
4. **Server-only registration.** `IEvmRpcClient` must be registered in server hosts only; a test/asserting that the WASM composition (`rpc == null`) still yields the default document guards the offline invariant.
5. **kid-less multi-key documents.** Rely on Veramo's `kid`; document the first-authorised-VM fallback when a credential omits `kid`. If a real issuer omits `kid` with multiple keys, a later refinement can try all candidate VMs.
6. **Fail-closed vs offline.** The configured-error → null rule must be provably distinct from the unconfigured → default-doc path (a test asserting each).

## 11. Out of scope (YAGNI)

Browser/WASM RPC; service endpoints & non-signing attributes; multi-candidate kid-less verification; `did:pkh` changes; on-chain writes / tx / Nethereum; secp256k1 signing / `WalletNetworks`; block sync / mempool / any RPC method beyond `eth_call` + `eth_getLogs`; on-chain PII.
