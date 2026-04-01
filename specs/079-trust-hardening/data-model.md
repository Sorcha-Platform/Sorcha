# Data Model: Trust Hardening (079)

**Branch**: `079-trust-hardening`
**Date**: 2026-03-31

## Entity Diagram

```
TransactionReceipt ──references──▶ Docket
       │                              │
       │ contains                     │ contains
       ▼                              ▼
MerkleInclusionProof          TransactionModel
       │                              │
       │ proves inclusion of          │ may be revoked by
       ▼                              ▼
  Transaction Hash            RevocationTransaction
                                      │
                                      │ produces
                                      ▼
                              TransactionStatus (derived)
                                      │
                                      │ included in
                                      ▼
                              VerificationBundle (portable)
```

## New Entities

### TransactionReceipt

A cryptographically signed attestation that a transaction was sealed in a specific docket.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| ReceiptId | string | Required, unique | SHA-256 hash of receipt content (deterministic ID) |
| TransactionId | string | Required, indexed | Transaction this receipt proves |
| RegisterId | string | Required, indexed | Register containing the transaction |
| DocketNumber | long | Required | Docket height where transaction was sealed |
| MerkleRoot | string | Required | Merkle root of the docket |
| InclusionProof | MerkleInclusionProof | Required | Proof path from leaf to root |
| Signatures | ValidatorSignature[] | Required, min 1 | Validator signature(s) over receipt content |
| SealedAt | DateTimeOffset | Required | When the docket was confirmed |
| Version | int | Required, default 1 | Receipt format version |

**Storage**: MongoDB `receipts` collection in per-register database (`sorcha_register_{registerId}`)
**Indexes**: `TransactionId` (unique), `RegisterId + DocketNumber` (compound)
**Immutability**: Receipts are append-only; once created, never modified or deleted.

### MerkleInclusionProof

A compact proof that a transaction hash is a leaf in a docket's Merkle tree.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| TransactionHash | string | Required | SHA-256 hash of the proven transaction |
| DocketNumber | long | Required | Docket containing the transaction |
| MerkleRoot | string | Required | Expected root hash |
| ProofPath | MerkleProofStep[] | Required | Sibling hashes from leaf to root |
| LeafIndex | int | Required, >= 0 | Position of transaction in the tree |
| TreeSize | int | Required, >= 1 | Total number of leaves in the docket |

**Not stored independently** — embedded in `TransactionReceipt` or generated on-demand.

### MerkleProofStep

A single step in the Merkle proof path.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Hash | string | Required | Sibling hash at this tree level |
| Position | ProofPosition | Required | Whether sibling is Left or Right |

```
enum ProofPosition { Left, Right }
```

### ValidatorSignature

A validator's signature on a receipt.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| ValidatorAddress | string | Required | Bech32 wallet address of signing validator |
| SignatureValue | byte[] | Required | Raw signature bytes |
| Algorithm | string | Required | Signing algorithm (ED25519, NISTP256, RSA4096) |
| SignedAt | DateTimeOffset | Required | When the signature was produced |

**Design note**: Array format from day one to accommodate future multi-validator consensus (TRUST-6) without breaking format changes.

### RevocationPayload

The payload of a revocation transaction. Stored as the transaction's `Payload` field.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| OriginalTxId | string | Required | Transaction being revoked |
| OriginalDocketNumber | long | Required | Docket of the target transaction |
| Reason | RevocationReason | Required | Why the transaction is being revoked |
| SupersededByTxId | string? | Optional | Replacement transaction (if reason = Superseded) |
| Metadata | Dictionary<string, string>? | Optional | Additional context (max 10 entries) |

```
enum RevocationReason
{
    Superseded = 0,   // Replaced by newer version
    Erroneous = 1,    // Data error in original
    Compromised = 2,  // Signing key was compromised
    Expired = 3,      // Credential past validity period
    Withdrawn = 4,    // Issuer voluntarily withdraws
    Regulatory = 5    // Revoked due to regulation
}
```

**Transaction type**: `TransactionType.Revocation = 4` (new enum value)
**Validation rules**:
- `OriginalTxId` must reference an existing sealed transaction on the same register
- Target transaction must not already be revoked (no double-revocation)
- `SupersededByTxId` required when `Reason = Superseded`, forbidden otherwise
- Revoker must be original transaction signer OR Owner/Admin in register governance roster

### TransactionStatusResponse (Derived View)

Not a stored entity — derived on-demand by querying revocation transactions.

| Field | Type | Description |
|-------|------|-------------|
| TransactionId | string | The queried transaction |
| Status | TransactionLifecycleStatus | Current lifecycle state |
| RevocationTxId | string? | ID of the revocation transaction (if revoked) |
| SupersededByTxId | string? | Replacement transaction (if superseded) |
| RevokedAt | DateTimeOffset? | When the revocation was sealed |
| Reason | RevocationReason? | Why it was revoked |

```
enum TransactionLifecycleStatus
{
    Active = 0,       // Transaction is valid and current
    Revoked = 1,      // Explicitly revoked
    Superseded = 2    // Replaced by a newer transaction
}
```

### VerificationBundle (Portable Package)

Exported for offline verification by third parties.

| Field | Type | Description |
|-------|------|-------------|
| Version | int | Bundle format version |
| TransactionId | string | Transaction being verified |
| RegisterId | string | Register containing the transaction |
| Credential | object | The VC/payload data |
| Receipt | TransactionReceipt | Signed receipt with inclusion proof |
| RevocationStatus | TransactionStatusResponse | Point-in-time status snapshot |
| ExportedAt | DateTimeOffset | When this bundle was generated |
| ValidatorPublicKeys | ValidatorKeyInfo[] | Public keys for signature verification |

**Format**: JSON, self-contained, no external references required for verification.

## Modified Entities

### TransactionType (Existing Enum — Extended)

```
enum TransactionType
{
    Control = 0,      // existing
    Action = 1,       // existing
    Docket = 2,       // existing
    Participant = 3,  // existing
    Revocation = 4    // NEW
}
```

### MerkleTree (Existing Class — Extended)

New methods added:

| Method | Parameters | Returns | Description |
|--------|-----------|---------|-------------|
| GenerateInclusionProof | leafIndex, transactionHashes | MerkleInclusionProof | Generate proof for one leaf |
| GenerateAllProofs | transactionHashes | MerkleInclusionProof[] | Generate all proofs in one O(n log n) pass |

## Relationships

| Source | Target | Cardinality | Description |
|--------|--------|-------------|-------------|
| TransactionReceipt | Docket | N:1 | Many receipts per docket (one per transaction) |
| TransactionReceipt | MerkleInclusionProof | 1:1 | Each receipt embeds one proof |
| RevocationTransaction | Transaction | N:1 | One revocation per target (enforced) |
| VerificationBundle | TransactionReceipt | 1:1 | Bundle contains one receipt |
| VerificationBundle | TransactionStatusResponse | 1:1 | Bundle contains one status snapshot |

## State Transitions

### Transaction Lifecycle

```
Submitted → Pooled → Validated → Sealed → Active
                                            │
                                     [revocation tx sealed]
                                            │
                                     ┌──────┴──────┐
                                     ▼              ▼
                                  Revoked      Superseded
                                  (terminal)   (terminal)
```

- **Active → Revoked**: When a `RevocationTransaction` targeting this tx is sealed (any reason except Superseded)
- **Active → Superseded**: When a `RevocationTransaction` with `Reason=Superseded` is sealed
- **Revoked/Superseded → ***: Terminal states. Cannot be un-revoked. To reinstate, issue a new transaction.
