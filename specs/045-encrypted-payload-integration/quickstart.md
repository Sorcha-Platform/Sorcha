# Quickstart: Encrypted Payload Integration

**Feature**: 045-encrypted-payload-integration
**Date**: 2026-03-01

## What This Feature Does

Encrypts all action payload data before it's written to the register. Recipients can only read the fields they're entitled to see by decrypting with their private key. No plaintext payload data is stored on the ledger.

## Key Concepts

### Envelope Encryption
- A random 32-byte symmetric key (XChaCha20-Poly1305) encrypts the payload data once
- That symmetric key is then "wrapped" (encrypted) individually for each recipient using their public key
- Recipients unwrap the symmetric key with their private key, then decrypt the payload

### Disclosure Groups
- Recipients sharing the same disclosed fields are grouped together
- One ciphertext per group, one wrapped key per recipient within the group
- 10 recipients across 3 groups = 3 encryptions + 10 key wraps (not 10 encryptions)

### Async Pipeline
- Validation, calculation, routing, and disclosure happen synchronously
- HTTP 202 returned immediately with an operation tracking ID
- Encryption runs in the background with real-time SignalR progress notifications

## Architecture Overview

```
ActionExecutionService
├── 1. Validate (sync)
├── 2. Calculate (sync)
├── 3. Route (sync)
├── 4. Disclose (sync) ─── DisclosureProcessor (unchanged)
├── 5. Return HTTP 202 + operationId
│
└── Channel<EncryptionWorkItem>
    │
    EncryptionBackgroundService (async)
    ├── 6. Resolve public keys (batch from Register + external)
    ├── 7. Group recipients by disclosure field sets
    ├── 8. For each group: encrypt payload, wrap key per recipient
    ├── 9. Build encrypted transaction
    ├── 10. Sign transaction
    ├── 11. Estimate size (fail if > 4MB)
    └── 12. Submit to Validator
        │
        SignalR (ActionsHub)
        ├── EncryptionProgress → step, percentage, stepName
        ├── EncryptionComplete → transactionHash
        └── EncryptionFailed → error, failedRecipient
```

## Services Affected

| Service | Changes |
|---------|---------|
| **Blueprint Service** | Encryption pipeline, async processing, SignalR events |
| **Sorcha.Cryptography** | P-256 ECIES implementation |
| **Wallet Service** | ML-KEM-768 decapsulate fix |
| **Register Service** | Batch public key resolution endpoint |
| **Validator Service** | Transaction size enforcement (4MB) |
| **Sorcha.TransactionHandler** | Wire ActionExecutionService to PayloadManager encryption path |
| **Sorcha.ServiceClients** | Batch public key client method |

## Development Order

1. **Foundation** (no dependencies): P-256 ECIES, ML-KEM fix, size enforcement, batch endpoint
2. **Pipeline** (depends on foundation): Disclosure grouping, encryption service, Channel producer-consumer
3. **Integration** (depends on pipeline): Wire into ActionExecutionService, SignalR events, operation tracking
4. **Decryption** (depends on integration): Recipient decryption flow, integrity verification
5. **Testing** (depends on all): Integration tests, round-trip verification across all 4 algorithms

## Key Files to Read

| File | Why |
|------|-----|
| `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` | The orchestration method — lines 249-274 are the gap |
| `src/Common/Sorcha.TransactionHandler/Payload/PayloadManager.cs` | Existing envelope encryption — lines 78-159 |
| `src/Common/Sorcha.Cryptography/Core/CryptoModule.cs` | Algorithm dispatch — lines 319-398 |
| `src/Common/Sorcha.Cryptography/Core/SymmetricCrypto.cs` | XChaCha20-Poly1305 implementation |
| `src/Services/Sorcha.Blueprint.Service/Hubs/ActionsHub.cs` | SignalR hub to extend |
| `src/Services/Sorcha.Blueprint.Service/Services/Implementation/NotificationService.cs` | Notification patterns |
| `src/Services/Sorcha.Register.Service/Program.cs:1518-1560` | Existing public key resolution endpoint |
| `src/Services/Sorcha.Validator.Service/Services/TransactionReceiver.cs` | Size enforcement gap |
