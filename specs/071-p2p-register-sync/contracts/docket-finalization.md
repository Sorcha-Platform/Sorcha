# Contract: Docket-Driven Finalization

**Feature**: 071-p2p-register-sync | **Date**: 2026-03-28

## Overview

New `DocketFinalizationService` in the Peer Service processes replicated dockets, verifies validator signatures, and writes finalized transactions to the local Register Service via `IRegisterServiceClient.WriteDocketAsync()`.

## Finalization Flow

### Trigger

Called when a replicated docket arrives in `RegisterCache` — either during initial sync (PullFullReplicaAsync) or live streaming (SubscribeToLiveTransactionsAsync).

### Steps

1. **Extract validator key**: On first docket for a register, resolve the validator's public key from the genesis docket's `ProposerSignature.PublicKey`. Cache in `ValidatorKeyCache`.

2. **Recompute docket hash**: Use `DocketHasher.ComputeDocketHash(RegisterId, DocketNumber, PreviousHash, MerkleRoot, Timestamp)` to produce the expected hash deterministically.

3. **Verify signature**: Using `Sorcha.Cryptography` verification:
   - Input: `ProposerSignature.SignatureValue`, computed hash bytes, `ProposerSignature.PublicKey`, `ProposerSignature.Algorithm`
   - Output: bool (valid/invalid)

4. **Verify chain integrity**: `Docket.PreviousHash` must match the hash of the previous docket (DocketNumber - 1). For genesis (DocketNumber 0), PreviousHash is null.

5. **Write to Register Service**: Call `IRegisterServiceClient.WriteDocketAsync(docketModel)` which POSTs to `api/registers/{registerId}/dockets`. The docket includes its full transaction list.

6. **Handle idempotency**: If the docket already exists in Register Service (duplicate write), treat as success (no error).

## Existing API Used

### IRegisterServiceClient.WriteDocketAsync (existing — no changes)

```
POST api/registers/{registerId}/dockets
```

**Request body** (DocketModel):
- DocketId, RegisterId, DocketNumber, PreviousHash, DocketHash
- CreatedAt, ProposerValidatorId, MerkleRoot
- Transactions: List<TransactionModel> (full transaction objects)

**Response**: bool (success/failure)

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Invalid signature | Reject docket, log alert, do not write |
| Chain integrity failure (PreviousHash mismatch) | Reject docket, log alert, mark subscription for resync |
| Register Service unavailable | Retry with exponential backoff (2s, 4s, 8s, max 30s), transactions remain in cache |
| Duplicate docket write | Treat as success (idempotent) |
| Unknown algorithm | Reject docket, log warning |

## New Service: DocketFinalizationService

**Location**: `src/Services/Sorcha.Peer.Service/Replication/DocketFinalizationService.cs`

**Dependencies**:
- `IRegisterServiceClient` — write finalized dockets
- `DocketHasher` (Sorcha.Cryptography) — recompute hash for verification
- `IWalletService` or direct crypto verification — signature validation
- `RegisterCache` — read replicated dockets and transactions
- `ValidatorKeyCache` — cached public keys per register

**Integration point**: Called from `RegisterReplicationService` after dockets are cached, and from `RelayMessageHandler` when live docket notifications arrive.
