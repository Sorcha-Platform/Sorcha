# Data Model: System Register Genesis Trust Anchor

**Date**: 2026-04-10 | **Branch**: `099-genesis-trust-anchor`

## New Entities

### SystemRegisterGenesis

The complete genesis file model. Deserialized from `system-register-genesis.json`.

| Field | Type | Description |
|-------|------|-------------|
| Version | int | Format version (currently 1) |
| NetworkId | string | Human-readable network label (e.g., "sorcha-prod") |
| GenesisTransaction | GenesisTransactionData | The signed genesis transaction |
| ValidatorRoster | ValidatorRoster | Authorized docket-signing keys (reuses existing model) |
| GenesisPublicKeyFingerprint | string | SHA-256 fingerprint of genesis public key (hex, truncated to 32 chars) |

**Validation Rules**:
- Version must be 1 (only supported version)
- NetworkId: 1-64 chars, alphanumeric + hyphens
- GenesisTransaction: required, signature must verify
- ValidatorRoster: required, 1-10 validators
- GenesisPublicKeyFingerprint: must match computed fingerprint from GenesisTransaction.Signature.PublicKey

### GenesisTransactionData

The signed control record within the genesis file.

| Field | Type | Description |
|-------|------|-------------|
| TxId | string | Deterministic: SHA256(UTF8("genesis-{SystemRegisterId}")) |
| Payload | string | Base64Url-encoded RegisterControlRecord JSON |
| Signature | GenesisSignature | Cryptographic signature over the transaction |

**Validation Rules**:
- TxId must match SHA256("genesis-aebf26362e079087571ac0932d4db973")
- Payload must deserialize to a valid RegisterControlRecord
- Payload RegisterId must equal SystemRegisterConstants.SystemRegisterId

### GenesisSignature

Cryptographic signature attached to the genesis transaction.

| Field | Type | Description |
|-------|------|-------------|
| PublicKey | string | Base64-encoded signer's public key |
| SignatureValue | string | Base64-encoded signature bytes |
| Algorithm | string | Signing algorithm (e.g., "ED25519") |
| SignedAt | DateTimeOffset | Timestamp of signing |

**Validation Rules**:
- PublicKey: required, valid Base64
- SignatureValue: required, valid Base64
- Algorithm: must be a supported algorithm in Sorcha.Cryptography
- Signed data: SHA256(UTF8("{TxId}:{PayloadHash}")) where PayloadHash = SHA256(decoded payload bytes)

### GenesisValidatorKeyFile

The private key material output by the ceremony. Not persisted in any service — file only.

| Field | Type | Description |
|-------|------|-------------|
| Version | int | Format version (currently 1) |
| NetworkId | string | Must match the genesis file's NetworkId |
| WalletAddress | string | Derived wallet address for this key |
| PrivateKey | string | Base64-encoded private key bytes |
| PublicKey | string | Base64-encoded public key bytes |
| Algorithm | string | Key algorithm (e.g., "ED25519") |
| CreatedAt | DateTimeOffset | Ceremony timestamp |
| Fingerprint | string | SHA-256 fingerprint matching genesis file |

**Validation Rules**:
- PrivateKey/PublicKey must form a valid keypair (verify by signing/verifying a test message)
- Fingerprint must match the genesis file's GenesisPublicKeyFingerprint

### SystemRegisterOptions

Configuration model bound from `SystemRegister` config section.

| Field | Type | Description |
|-------|------|-------------|
| GenesisFile | string? | Absolute path to genesis JSON file. Null = use embedded resource. |

## Existing Entities (No Changes)

| Entity | Location | Why No Change |
|--------|----------|---------------|
| RegisterControlRecord | Sorcha.Register.Models | Genesis payload format unchanged |
| ValidatorRoster | Sorcha.Register.Models | Reused as-is in genesis file |
| ValidatorRosterEntry | Sorcha.Register.Models | Reused as-is in genesis file |
| TransactionSubmission | Sorcha.ServiceClients.Http | Used to submit pre-signed genesis to Validator |
| SignatureInfo | Sorcha.ServiceClients.Http | Used within TransactionSubmission |
| ValidatorRosterCache | Sorcha.Peer.Service | Key cache structure unchanged |
| Docket | Sorcha.Register.Models | Docket structure unchanged |

## Entity Relationships

```
SystemRegisterGenesis
├── GenesisTransactionData
│   ├── Payload → RegisterControlRecord (existing)
│   │   └── Validators → ValidatorRoster (existing)
│   │       └── Validators[] → ValidatorRosterEntry (existing)
│   └── Signature → GenesisSignature
└── ValidatorRoster (top-level copy for quick access)

GenesisValidatorKeyFile (separate output, matches genesis by Fingerprint)

SystemRegisterOptions (configuration, references genesis file path)
```

## State Transitions

### System Register Bootstrap State

```
[No System Register]
    │
    ├── (peers available) ──→ [Syncing from Peer]
    │                              │
    │                              ├── (genesis verified) ──→ [Operational]
    │                              └── (genesis rejected) ──→ [Stopped: Trust Mismatch]
    │
    ├── (no peers, genesis file found)
    │       │
    │       ├── (local validator rostered) ──→ [Sealing Genesis] ──→ [Operational]
    │       └── (not rostered) ──→ [Stopped: Import Key Required]
    │
    └── (no peers, no genesis file) ──→ [Stopped: Run Ceremony]

[Operational]
    └── (restart) ──→ [Check Local] ──→ [Operational] (idempotent)
```
