# Research: Trust Hardening (079)

**Branch**: `079-trust-hardening`
**Date**: 2026-03-31

## R1: Receipt Generation Insertion Point

**Decision**: Generate receipts synchronously in `DocketDistributor` after successful `WriteDocketAsync()` call.

**Rationale**: The `ValidatorOrchestrator` pipeline follows this sequence:
1. `DocketBuilder.BuildDocketAsync()` — creates docket with Merkle root, signs with system wallet
2. `ConsensusEngine.AchieveConsensusAsync()` — collects validator votes
3. `DocketDistributor.SubmitToRegisterServiceAsync()` — writes to Register Service via `POST /api/registers/{registerId}/dockets`
4. Post-seal cleanup — removes from memory pool, broadcasts to peers

The insertion point is step 3, immediately after `WriteDocketAsync()` succeeds. At this point:
- The docket is persisted with `State=Sealed`
- All consensus signatures are available in `docket.Votes`
- All transaction IDs and the Merkle root are known
- The Validator's system wallet address is available via `SystemWalletProvider`

Receipt generation (computing inclusion proofs + signing) adds minimal latency (~5-10ms) because:
- Inclusion proofs are O(n log n) for the full docket but we generate all proofs in one pass
- Signing is a single ED25519 operation per receipt (~0.1ms)

**Alternatives considered**:
- Generating in `ValidatorOrchestrator` after `SubmitToRegisterServiceAsync()` — rejected because the orchestrator doesn't have direct access to the docket's internal transaction hash list
- Generating asynchronously via background service — rejected due to eventual consistency gap

## R2: Merkle Inclusion Proof Generation

**Decision**: Extend `MerkleTree` class with `GenerateInclusionProof()` and `GenerateAllProofs()` methods.

**Rationale**: The existing `MerkleTree.cs` has:
- `ComputeMerkleRoot(IReadOnlyList<string> transactionHashes)` — builds tree and returns root
- `VerifyMerkleProof(string dataHash, string merkleRoot, IReadOnlyList<string> proof)` — verifies a proof path

The verification method exists but there's no generation method. The tree structure is computed inside `ComputeMerkleRoot()` but discarded. We need to:
1. Refactor `ComputeMerkleRoot()` to retain the tree structure (array of levels)
2. Add `GenerateInclusionProof(int leafIndex, IReadOnlyList<string> transactionHashes)` — returns sibling path for one leaf
3. Add `GenerateAllProofs(IReadOnlyList<string> transactionHashes)` — returns all proofs in one O(n log n) pass

The proof format will be `MerkleProofStep { Hash, Position (Left/Right) }` to match the existing verification logic which uses lexicographic ordering.

**Alternatives considered**:
- Storing full tree structure in MongoDB — rejected due to storage redundancy
- Using the ZK proof provider instead — rejected because ZK proofs serve a different use case (privacy-preserving, heavier computation)

## R3: Revocation Transaction Type vs Governance Operation

**Decision**: Add `Revocation = 4` to `TransactionType` enum as a new first-class transaction type, NOT as a `GovernanceOperationType`.

**Rationale**: Revocation is fundamentally different from governance operations (Add/Remove/Transfer roster members):
- Governance operations modify the register's admin roster
- Revocations reference and supersede previously sealed data/action transactions
- Revocations need their own validation rules (target exists, not already revoked, authorised revoker)
- Revocations should produce receipts like any other transaction

The `GovernanceOperationType` enum is specifically for roster mutations within `ControlTransactionPayload`. A revocation has a different payload structure entirely:
```
RevocationPayload = { OriginalTxId, OriginalDocketNumber, Reason, SupersededByTxId?, Metadata? }
```

The Validator's `RightsEnforcementService` currently only handles Control transactions via the `"register-governance-v1"` blueprint check. For revocations, we need a separate validation path in `ValidationEngine` that:
1. Checks the target transaction exists and is not already revoked
2. Verifies the revoker is the original signer OR has a roster role with revocation rights
3. Validates the revocation reason is a known enum value

**Alternatives considered**:
- Using `GovernanceOperationType.Revoke = 3` — rejected because revocations don't modify the roster and shouldn't use `ControlTransactionPayload`
- Creating a new blueprint type `"revocation-v1"` — rejected as over-engineering; revocations are a platform primitive, not a blueprint workflow

## R4: Revocation Authority Check

**Decision**: Check original signer match first, then fall back to governance roster for Owner/Admin roles.

**Rationale**: The existing `RightsEnforcementService` reconstructs the admin roster from Control transaction history. This same roster reconstruction can be reused for revocation authority checks:

1. **Original signer check**: Extract `senderWallet` from the revocation transaction, compare with `senderWallet` of the target transaction. If match → authorised.
2. **Roster check**: If no match, reconstruct admin roster for the register, check if the revoker has Owner or Admin role. If yes → authorised.

This avoids creating a new "Revoker" role in the roster (which would require schema changes). Owner and Admin roles already imply organisational authority. A dedicated "Revoker" role can be added later if granular access control is needed.

**Alternatives considered**:
- Adding `RegisterRole.Revoker = 4` — deferred, adds complexity without clear current need
- Checking blueprint-level participant roles — rejected, doesn't cover non-blueprint transactions

## R5: Receipt Storage Location

**Decision**: Store receipts in MongoDB alongside dockets in the Register Service, in a new `receipts` collection per register database.

**Rationale**: Receipts are tightly coupled with dockets — they reference docket numbers, Merkle roots, and are generated at seal time. The per-register database pattern (`sorcha_register_{registerId}`) already exists. A `receipts` collection indexed by `txId` provides O(1) lookup.

Receipts flow: Validator generates → sends to Register Service alongside docket write → Register Service persists in `receipts` collection.

**Alternatives considered**:
- Storing in Validator Service — rejected because the Validator is stateless by design
- Embedding receipts in the docket document — rejected because receipts are per-transaction, dockets contain many transactions, and the receipt retrieval pattern is by txId not by docketId

## R6: Transaction Status Derivation

**Decision**: Derive transaction status on-demand by querying for revocation transactions that reference the target txId, rather than maintaining a separate status index.

**Rationale**: For v1, the query pattern is simple: `db.transactions.find({ "Metadata.OriginalTxId": targetTxId, "Metadata.TransactionType": "Revocation" })`. With an index on `Metadata.OriginalTxId`, this is O(1).

A materialised status index (separate collection updated on each revocation) adds write complexity and consistency concerns. The on-demand approach is sufficient for the expected query volume and can be optimised with caching later.

**Alternatives considered**:
- Materialised `transaction_status` collection updated on each revocation — deferred to v2 if performance requires it
- Adding a `Status` field to existing transaction documents — rejected because transactions are immutable once sealed

## R7: Portable Verification Library Scope

**Decision**: Extend `Sorcha.Validator.Core` with receipt verification and inclusion proof verification. Keep it dependency-light.

**Rationale**: `Validator.Core` is already enclave-safe with dependencies only on `Sorcha.Cryptography`, `Sorcha.Blueprint.Models`, and `Sorcha.Register.Models`. It already contains `DocketValidator`, `TransactionValidator`, and `ConsensusValidator`.

Adding:
- `ReceiptValidator` — verifies receipt signature against validator public key
- `InclusionProofValidator` — recomputes Merkle root from proof path (can delegate to `MerkleTree.VerifyMerkleProof()`)
- `BundleValidator` — orchestrates all four checks (VC signature, inclusion proof, receipt, revocation status)

The `MerkleTree.VerifyMerkleProof()` already exists in `Sorcha.Cryptography` which is a dependency of `Validator.Core`.

## R8: SignalR Receipt Push

**Decision**: Push receipt notifications via the existing `RegisterHub` by adding a new `TransactionReceipt` client method, delivered to the `register:{registerId}` group.

**Rationale**: The `RegisterEventBridgeService` already subscribes to Redis Stream events and bridges them to SignalR. Adding a `receipt:generated` event follows the same pattern as `docket:confirmed`:

1. Validator generates receipt → publishes `receipt:generated` to Redis Stream
2. `RegisterEventBridgeService` subscribes → pushes to `RegisterHub` clients in `register:{registerId}` group
3. Clients receive `TransactionReceipt` notification

The wallet-group pattern (used for encryption progress) could also work, but register-group is more appropriate because receipts are register-scoped events, and multiple participants in the register may want to see receipts for transactions they're involved in.
