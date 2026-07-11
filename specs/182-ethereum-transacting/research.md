# Phase 0 Research — Ethereum Transacting (Phase 4)

The pivotal architectural decisions (Nethereum vs pure-managed; custody/authorization; scope; server-side
boundary) were settled during brainstorming and are recorded in the design doc
(`docs/superpowers/specs/2026-07-11-ethereum-verify-phase4-design.md`). This file consolidates the
remaining technical unknowns and anchors, each resolved so there are **no NEEDS CLARIFICATION** markers left
for planning.

## R1. Transaction encoding: pure-managed RLP + EIP-1559 (type-2)

- **Decision**: Hand-roll a minimal `Rlp` encoder + `EthereumTransaction` (type-2) builder in the primitive,
  reusing `Keccak256` + `Secp256k1Signer`. No Nethereum.
- **Rationale**: The primitive has been pure-managed/WASM-safe/zero-new-dependency for 3 phases; RLP and a
  fixed type-2 envelope are small and fully testable. Nethereum is a heavy transitive dependency, not
  WASM-safe, and only needed server-side.
- **Signing payload (unsigned)**: `0x02 ‖ rlp([chainId, nonce, maxPriorityFeePerGas, maxFeePerGas, gasLimit,
  to, value, data, accessList])` with `accessList = []` (empty list `0xc0`) and `data = 0x` (empty). Signing
  hash = `Keccak256(payload)`.
- **Signed tx**: `0x02 ‖ rlp([…same 9 fields…, yParity, r, s])`; `txHash = Keccak256(signedTx)`.
- **Alternatives considered**: Nethereum (rejected — dependency weight, WASM-unsafe, breaks the primitive
  thesis); hybrid (rejected — mixes idioms for no gain since ETH-transfer encoding is tiny).

## R2. RLP integer encoding rule (the subtle correctness core)

- **Decision**: Integers are encoded as **minimal big-endian** byte strings: strip leading zero bytes; the
  value `0` encodes as the **empty byte string** (`0x80`), not `0x00`. Addresses are fixed 20-byte strings;
  `r`/`s` are the signature's 32-byte values (also minimal-stripped as byte strings per RLP — but in
  practice encoded as their natural 32-byte big-endian, RLP-length-prefixed).
- **Rationale**: This is the most common source of malformed-tx bugs. `nonce=0`, `value` with leading zeros,
  and empty `data`/`accessList` must encode exactly.
- **Anchor**: dedicated `Rlp` unit tests against canonical RLP vectors (empty string → `0x80`, empty list →
  `0xc0`, single byte < 0x80 as itself, short string length prefix `0x80+len`, long string `0xb7+lenlen`,
  list prefixes `0xc0…`).

## R3. Recovery-id → yParity conversion

- **Decision**: `Secp256k1Signer.SignRecoverable` returns `v ∈ {27, 28}` (EIP-191 convention). Typed
  (EIP-2718) transactions carry `yParity ∈ {0, 1}`. The `EthereumTransaction.AssembleSigned` converts
  `yParity = v − 27`.
- **Rationale**: Reusing the existing signer unchanged keeps one signing primitive; the conversion is a
  one-liner and is asserted by the interop vector.

## R4. Interop anchor (known-key → known-raw-tx + hash)

- **Decision**: Anchor `BuildSigningPayload`/`AssembleSigned` with a **published EIP-1559 test vector**: a
  known private key + fixed `{chainId, nonce, maxPriorityFeePerGas, maxFeePerGas, gasLimit, to, value}`
  producing a known signed raw-tx hex and tx hash (derivable via ethers.js / documented EIP-1559 vectors).
  The test asserts the raw tx and hash byte-for-byte, plus a sign→`Secp256k1Recovery`→`from` round-trip.
- **Rationale**: Byte-exactness against an independent implementation is the only reliable proof RLP + the
  envelope + `yParity` are correct. To be produced/verified during planning-to-implementation (a fixed
  vector is committed alongside the test; no live network needed).

## R5. Fee & gas computation (EIP-1559)

- **Decision**: `maxPriorityFeePerGas = eth_maxPriorityFeePerGas`; `baseFee = baseFeePerGas` of the pending
  block (`eth_getBlockByNumber("pending", false)`); `maxFeePerGas = baseFee × 2 + maxPriorityFeePerGas`,
  **clamped** to `EthereumTransactionOptions.MaxFeePerGasWei` (over-ceiling ⇒ refuse, not clamp-and-send —
  refuse per FR-009); `gasLimit = eth_estimateGas` (fail-closed — no estimate ⇒ no send).
- **Rationale**: `base×2` gives headroom for base-fee rises over the next few blocks (standard heuristic);
  the ceiling bounds a mis-fetched fee. A plain transfer is 21000 gas but estimating is robust and future-
  proofs the seam. If some providers omit `eth_maxPriorityFeePerGas`, fall back to a sane default tip
  (e.g. 1.5 gwei) — a documented fallback, not a failure.
- **Alternatives considered**: `eth_feeHistory` percentile modelling (rejected — over-engineered for
  testnet); fixed gas price / legacy type-0 (rejected — out of scope).

## R6. Nonce management

- **Decision**: `nonce = eth_getTransactionCount(address, "pending")`. No local nonce tracking/queueing.
- **Rationale**: Correct for sequential, low-volume testnet use. Concurrent sends from one address racing on
  the same pending nonce is a **documented limitation** (spec Edge Cases + Assumptions), not solved here; a
  per-address nonce tracker is a designed-in later seam.

## R7. Chains & policy defaults

- **Decision**: `EthereumTransactionOptions` (config `Ethereum:Transactions`): `EnabledChainIds` default
  `[11155111 (Sepolia), 17000 (Holešky)]`; `AllowMainnet` default `false` (any chain **not** in a built-in
  known-testnet set is refused unless true); `MaxValueWei` default `100000000000000000` (0.1 ETH);
  `MaxFeePerGasWei` sanity ceiling (operator-set; a conservative default documented).
- **Rationale**: Testnet-first minimises blast radius; the master switch makes mainnet a deliberate,
  auditable operator action. Namespace is distinct from the Phase-2b DID-resolution config
  (`DidResolver:Ethr`) because transacting is a different concern — but the per-chain **RPC URL** may point
  at the same node provided it is **write-capable** (public read-only endpoints reject
  `eth_sendRawTransaction`).
- **Known-testnet set**: Sepolia (11155111), Holešky (17000) at minimum; extendable. Everything else is
  "mainnet-class" for the gate's purposes unless explicitly a recognised testnet.

## R8. Broadcast & status semantics

- **Decision**: Fire-and-report-hash — the send endpoint returns `{ txHash, status:"submitted" }` right after
  `eth_sendRawTransaction`; a separate `GET` endpoint maps `eth_getTransactionReceipt` → `pending` (null
  receipt) / `success` (`status == 0x1`) / `reverted` (`status == 0x0`), with `blockNumber` + `gasUsed`.
- **Rationale**: Confirmation can take minutes; blocking a request is unacceptable. Caller-driven polling
  keeps the surface simple (no push notifications this phase).

## R9. Layering & server-only registration

- **Decision**: `Sorcha.Wallet.Core` builds+signs from a fully-specified `EthereumTransactionRequest` and
  takes **no** `IEvmRpcClient` dependency. `Sorcha.Wallet.Service` gathers chain params + broadcasts and
  registers `IEvmRpcClient` (server host only). A composition test asserts the WASM PWA registers no
  transaction service / write RPC.
- **Rationale**: Preserves the Core-has-no-Infra-dependency constitution rule and the Phase-2b server-only
  RPC boundary; keeps value-moving signing off-device.

## R10. Authorization

- **Decision**: A distinct `CanTransactEthereum` authorization policy gates send + preview (status is plain
  authenticated). It defaults to the same requirement as `CanManageWallets` but is separately nameable so it
  can be tightened without touching other wallet operations.
- **Rationale**: "Gated path" (decision Q2) wants an explicit, independently-tunable authorization surface
  for the value-moving action, not a silent reuse of the general wallet-management gate.
