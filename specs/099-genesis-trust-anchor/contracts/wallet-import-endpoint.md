# API Contract: Validator Key Import Endpoint

## POST /api/v1/wallets/import-validator-key

**Service**: Wallet Service (internal, not routed through API Gateway)
**Purpose**: Import a raw ED25519 private key for genesis validator docket signing.
**Auth**: Service-to-service JWT with `wallet:admin` scope.

### Request

```json
{
  "privateKey": "<base64-encoded private key bytes>",
  "publicKey": "<base64-encoded public key bytes>",
  "algorithm": "ED25519",
  "networkId": "sorcha-prod",
  "label": "genesis-validator"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| privateKey | string | yes | Base64-encoded private key |
| publicKey | string | yes | Base64-encoded public key |
| algorithm | string | yes | Must be a supported algorithm |
| networkId | string | yes | Network identifier for labelling |
| label | string | no | Human-readable label (default: "genesis-validator") |

### Response (201 Created)

```json
{
  "walletAddress": "s1abc123def456...",
  "algorithm": "ED25519",
  "label": "genesis-validator",
  "createdAt": "2026-04-10T14:35:00Z"
}
```

### Response (200 OK — already exists)

Same body as 201. Idempotent — if a wallet with matching public key exists, returns it.

### Response (400 Bad Request)

```json
{
  "error": "InvalidKeyPair",
  "message": "The provided private and public keys do not form a valid keypair."
}
```

### Response (401 Unauthorized)

Missing or invalid service JWT.

### Notes

- The imported wallet must be usable with `SignTransactionAsync()` at derivation path `sorcha:docket-signing`.
- Since this is a raw key (not HD-derived), the "derivation" is identity — the imported key IS the signing key regardless of derivation path requested.
- The wallet address is derived from the public key using the standard Sorcha address encoding.
