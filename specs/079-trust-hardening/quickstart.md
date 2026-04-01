# Quickstart: Trust Hardening (079)

**Branch**: `079-trust-hardening`

## Scenario 1: Transaction Receipt Flow

**Goal**: Submit a transaction and receive a cryptographic receipt proving it was sealed.

```
1. Create a register and wallet (existing flow)
2. Submit a blueprint action transaction
3. Wait for docket sealing (automatic based on time/size threshold)
4. Receive receipt via SignalR notification OR poll receipt endpoint
5. Verify the receipt signature independently
```

**Steps**:

```bash
# 1. Submit a transaction (existing)
POST /api/registers/{registerId}/transactions
Body: { standard transaction payload }

# 2. After sealing, retrieve the receipt
GET /api/registers/{registerId}/transactions/{txId}/receipt

# Response includes:
# - receiptId (deterministic hash)
# - merkleRoot (docket's Merkle root)
# - inclusionProof (sibling hashes from leaf to root)
# - signatures[] (validator ED25519 signature)
# - sealedAt (confirmation timestamp)

# 3. Verify the receipt offline
POST /api/registers/{registerId}/receipts/verify
Body: { receipt, validatorPublicKey }

# Or use Validator.Core library directly (no HTTP):
# var result = receiptValidator.Verify(receipt, validatorPublicKey);
```

**Expected**: Receipt returned with valid validator signature. Verification confirms receipt authenticity without register access.

## Scenario 2: Merkle Inclusion Proof Verification

**Goal**: Prove a specific transaction is included in a sealed docket using only a compact proof.

```
1. Get the inclusion proof for a sealed transaction
2. Verify the proof recomputes to the correct Merkle root
3. Confirm the Merkle root matches the docket's published root
```

**Steps**:

```bash
# 1. Get inclusion proof
GET /api/registers/{registerId}/transactions/{txId}/inclusion-proof

# Response:
# - transactionHash (leaf)
# - proofPath[] (sibling hashes with left/right positions)
# - leafIndex, treeSize
# - merkleRoot

# 2. Verify the proof
POST /api/registers/{registerId}/inclusion-proofs/verify
Body: { transactionHash, merkleRoot, proofPath }

# Or use Validator.Core library:
# var valid = merkleTree.VerifyMerkleProof(txHash, root, proofPath);
```

**Expected**: Proof is compact (log2(n) steps). Verification succeeds. Tampered hashes fail verification.

## Scenario 3: Transaction Revocation

**Goal**: Revoke a previously sealed transaction (e.g., erroneous VC).

```
1. Identify the transaction to revoke
2. Submit a revocation transaction with reason
3. Revocation is validated and sealed in a new docket
4. Query the original transaction's status — now shows "revoked"
```

**Steps**:

```bash
# 1. Submit revocation
POST /api/registers/{registerId}/transactions/revoke
Body: {
  "originalTxId": "tx-to-revoke",
  "reason": "erroneous",
  "metadata": { "note": "Wrong serial number" },
  "signerWalletAddress": "sorcha1abc..."
}

# 2. Wait for sealing (revocation is a regular transaction)
# The revocation itself gets a receipt!

# 3. Check original transaction's status
GET /api/registers/{registerId}/transactions/{originalTxId}/status

# Response:
# { "status": "revoked", "revocationTxId": "rev-xxx", "reason": "erroneous", "revokedAt": "..." }
```

**Expected**: Revocation sealed on-chain. Original transaction status is "revoked". Attempting to revoke again returns 400.

## Scenario 4: Supersession (Replace a Credential)

**Goal**: Replace an incorrect VC with a corrected version.

```bash
# 1. Submit the corrected transaction first
POST /api/registers/{registerId}/transactions
Body: { corrected VC payload }
# → new txId: "corrected-tx"

# 2. Revoke the original, pointing to the replacement
POST /api/registers/{registerId}/transactions/revoke
Body: {
  "originalTxId": "original-tx",
  "reason": "superseded",
  "supersededByTxId": "corrected-tx",
  "signerWalletAddress": "sorcha1abc..."
}

# 3. Check status — shows superseded with pointer to new tx
GET /api/registers/{registerId}/transactions/{originalTxId}/status
# { "status": "superseded", "supersededByTxId": "corrected-tx", ... }
```

## Scenario 5: Offline Verification Bundle

**Goal**: Export a portable bundle and verify it without network access.

```bash
# 1. Export the bundle
GET /api/registers/{registerId}/transactions/{txId}/verification-bundle

# Response: Self-contained JSON with:
# - credential (VC payload)
# - receipt (with inclusion proof)
# - revocationStatus (snapshot at export time)
# - validatorPublicKeys (for signature verification)

# 2. Transfer bundle to air-gapped machine (USB, QR code, etc.)

# 3. Verify using Validator.Core library (no network):
# var verifier = new BundleVerifier(merkleTree, hashProvider);
# var result = verifier.VerifyBundle(bundle, bundle.ValidatorPublicKeys);
# assert result.IsValid
# assert result.Checks.CredentialSignatureValid
# assert result.Checks.InclusionProofValid
# assert result.Checks.ReceiptSignatureValid
```

**Expected**: All four checks pass. If credential was revoked after export, the bundle shows status at export time with a warning.

## Scenario 6: Governance Roster Revocation (Admin Override)

**Goal**: An admin revokes a transaction they didn't originally sign.

```bash
# Admin submits revocation (different wallet from original signer)
POST /api/registers/{registerId}/transactions/revoke
Body: {
  "originalTxId": "someone-elses-tx",
  "reason": "regulatory",
  "signerWalletAddress": "admin-wallet-address..."
}

# Validator checks:
# 1. admin-wallet != original signer wallet → no direct match
# 2. Reconstruct governance roster
# 3. admin-wallet belongs to Owner or Admin → authorised
# 4. Revocation accepted and sealed
```

**Expected**: Admin can revoke any transaction on registers where they hold Owner/Admin rights.

## Key Invariants to Test

- Every sealed transaction has a receipt (100% coverage)
- Receipt signature is verifiable with validator's public key only
- Inclusion proof recomputes to correct Merkle root
- Tampered proof paths fail verification
- Double-revocation is rejected
- Revoking a revocation is rejected
- Non-authorised revocations are rejected
- Revocation receipts are generated (revocations are transactions too)
- Verification bundles work on air-gapped machines
- All operations work with FLE-encrypted payloads
