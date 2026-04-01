# API Contract: Verification Bundle & Inclusion Proofs

**Service**: Register Service
**Base Path**: `/api/registers/{registerId}`

## Endpoints

### GET /transactions/{txId}/inclusion-proof

Generate a Merkle inclusion proof for a specific transaction.

**Authorization**: `CanReadTransactions` policy

**Path Parameters**:
| Parameter | Type | Description |
|-----------|------|-------------|
| registerId | string | Register ID |
| txId | string | Transaction ID |

**Response 200 OK**:
```json
{
  "transactionHash": "e5f6a7b8...",
  "docketNumber": 42,
  "merkleRoot": "f0e1d2c3...",
  "proofPath": [
    { "hash": "1a2b3c4d...", "position": "left" },
    { "hash": "5e6f7a8b...", "position": "right" },
    { "hash": "9c0d1e2f...", "position": "left" }
  ],
  "leafIndex": 3,
  "treeSize": 10
}
```

**Response 404 Not Found**: Transaction not found or not yet sealed.

### POST /inclusion-proofs/verify

Verify a Merkle inclusion proof (stateless — no register access needed).

**Authorization**: None (public endpoint)

**Request Body**:
```json
{
  "transactionHash": "e5f6a7b8...",
  "merkleRoot": "f0e1d2c3...",
  "proofPath": [
    { "hash": "1a2b3c4d...", "position": "left" },
    { "hash": "5e6f7a8b...", "position": "right" }
  ]
}
```

**Response 200 OK**:
```json
{
  "isValid": true,
  "computedRoot": "f0e1d2c3..."
}
```

### GET /transactions/{txId}/verification-bundle

Export a portable verification bundle for offline verification.

**Authorization**: `CanReadTransactions` policy

**Path Parameters**:
| Parameter | Type | Description |
|-----------|------|-------------|
| registerId | string | Register ID |
| txId | string | Transaction ID |

**Response 200 OK**:
```json
{
  "version": 1,
  "transactionId": "e5f6a7b8...",
  "registerId": "reg-001",
  "credential": {
    "type": "VerifiableCredential",
    "issuer": "did:sorcha:org:abc...",
    "credentialSubject": { /* VC payload */ }
  },
  "receipt": {
    "receiptId": "a1b2c3d4...",
    "transactionId": "e5f6a7b8...",
    "docketNumber": 42,
    "merkleRoot": "f0e1d2c3...",
    "inclusionProof": { /* MerkleInclusionProof */ },
    "signatures": [ /* ValidatorSignature[] */ ],
    "sealedAt": "2026-03-31T12:00:00Z",
    "version": 1
  },
  "revocationStatus": {
    "transactionId": "e5f6a7b8...",
    "status": "active",
    "revocationTxId": null,
    "supersededByTxId": null,
    "revokedAt": null,
    "reason": null
  },
  "exportedAt": "2026-03-31T15:00:00Z",
  "validatorPublicKeys": [
    {
      "address": "sorcha1abc...",
      "publicKey": "base64...",
      "algorithm": "ED25519"
    }
  ]
}
```

**Response 404 Not Found**: Transaction not found.
**Response 409 Conflict**: Transaction not yet sealed (no receipt available).

### POST /verification-bundles/verify

Verify a complete verification bundle (all four checks).

**Authorization**: None (public endpoint for offline-compatible verification)

**Request Body**:
```json
{
  "bundle": { /* VerificationBundle object */ }
}
```

**Response 200 OK**:
```json
{
  "isValid": true,
  "checks": {
    "credentialSignatureValid": true,
    "inclusionProofValid": true,
    "receiptSignatureValid": true,
    "revocationStatusCurrent": true
  },
  "warnings": []
}
```

**Response 200 OK (with warnings)**:
```json
{
  "isValid": true,
  "checks": {
    "credentialSignatureValid": true,
    "inclusionProofValid": true,
    "receiptSignatureValid": true,
    "revocationStatusCurrent": false
  },
  "warnings": [
    "Revocation status was captured at 2026-03-31T15:00:00Z. Online status check recommended for current status."
  ]
}
```

## Portable Verification Library

The `Sorcha.Validator.Core` library provides offline verification without network access:

```csharp
// Offline verification API (no HTTP, no DB)
var verifier = new BundleVerifier(merkleTree, hashProvider);

var result = verifier.VerifyBundle(bundle, validatorPublicKeys);
// result.IsValid
// result.Checks.CredentialSignatureValid
// result.Checks.InclusionProofValid
// result.Checks.ReceiptSignatureValid
// result.Checks.RevocationStatusAtExportTime
```

This library runs in any .NET environment (desktop, server, mobile, WASM) without external dependencies.
