# Implementation Plan: Transaction Handler Library

**Feature Branch**: `transaction-handler`
**Created**: 2025-12-03
**Status**: 68% Complete

## Summary

The Sorcha.TransactionHandler library provides transaction and payload management for the Sorcha decentralised register platform. It handles transaction creation, signing, verification, multi-recipient payload encryption, and serialization with backward compatibility for V1-V4 transaction formats.

## Design Decisions

### Decision 1: Fluent Builder Pattern

**Approach**: Use fluent builder pattern for transaction creation.

**Rationale**:
- Type-safe construction
- Discoverable API
- Natural chaining
- Clear validation points

**Alternatives Considered**:
- Constructor parameters - Too many options
- Property setters - No validation flow

### Decision 2: Separation from Cryptography

**Approach**: Transaction handler depends on Sorcha.Cryptography but is separate library.

**Rationale**:
- Independent versioning
- Single responsibility
- Clear dependency direction
- Easier testing with mocks

### Decision 3: Per-Recipient Key Encryption

**Approach**: Symmetric key encrypted with each recipient's public key.

**Rationale**:
- Efficient for large payloads (encrypt once)
- Secure key distribution
- Standard hybrid encryption pattern

### Decision 4: Double SHA-256 for Signing

**Approach**: Use SHA256(SHA256(data)) for transaction hash before signing.

**Rationale**:
- Industry standard (Bitcoin compatibility)
- Additional security against length-extension attacks
- Consistent with existing implementation

## Technical Approach

### Architecture

```
┌─────────────────────────────────────────────────────────┐
│              Sorcha.TransactionHandler                   │
│                   (.NET 10 Library)                      │
├─────────────────────────────────────────────────────────┤
│  Interfaces/                                             │
│  ├── ITransaction.cs                                    │
│  ├── ITransactionBuilder.cs                             │
│  ├── IPayload.cs                                        │
│  ├── IPayloadManager.cs                                 │
│  ├── ITransactionSerializer.cs                          │
│  └── ITransactionVerifier.cs                            │
├─────────────────────────────────────────────────────────┤
│  Core/                                                   │
│  ├── Transaction.cs                                     │
│  ├── TransactionBuilder.cs      (Fluent API)           │
│  ├── TransactionVerifier.cs                             │
│  └── TransactionFactory.cs                              │
├─────────────────────────────────────────────────────────┤
│  Payload/                                                │
│  ├── Payload.cs                                         │
│  ├── PayloadManager.cs          (Encryption/decryption) │
│  ├── PayloadOptions.cs                                  │
│  └── PayloadContainer.cs        (Per-recipient keys)    │
├─────────────────────────────────────────────────────────┤
│  Serialization/                                          │
│  ├── BinarySerializer.cs        (VarInt encoding)       │
│  ├── JsonSerializer.cs          (API format)            │
│  ├── JsonLdSerializer.cs        (Semantic format)       │
│  └── TransportSerializer.cs     (Network format)        │
├─────────────────────────────────────────────────────────┤
│  Versioning/                                             │
│  ├── VersionDetector.cs                                 │
│  ├── VersionRouter.cs                                   │
│  └── BackwardCompatibility.cs   (V1-V4 support)         │
└─────────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────┐
│ Sorcha.Cryptography │
└─────────────────────┘
```

### Component Status

| Component | Status | Notes |
|-----------|--------|-------|
| ITransaction | 100% | Interface defined |
| Transaction | 80% | Core implementation |
| TransactionBuilder | 70% | Fluent API mostly complete |
| PayloadManager | 60% | Encryption working |
| BinarySerializer | 80% | V4 format complete |
| JsonSerializer | 100% | JSON format complete |
| VersionDetector | 50% | V4 detection working |
| BackwardCompatibility | 40% | V3/V4 working |
| TransactionVerifier | 60% | Signature verification |
| Unit Tests | 65% | Core paths covered |

### Transaction Format (V4)

```
┌────────────────────────────────────────────────────────────┐
│ Transaction Binary Format (V4)                             │
├────────────────────────────────────────────────────────────┤
│ Version         : VarInt (1 byte for V4)                   │
│ PrevTxHash      : 32 bytes (or 0x00 if genesis)            │
│ Timestamp       : 8 bytes (Unix milliseconds)              │
│ SenderWallet    : VarString (Bech32 address)               │
│ RecipientCount  : VarInt                                   │
│ Recipients[]    : VarString[] (Bech32 addresses)           │
│ MetadataLength  : VarInt                                   │
│ Metadata        : bytes (JSON UTF-8)                       │
│ PayloadCount    : VarInt                                   │
│ Payloads[]      : Payload[] (see payload format)           │
│ Signature       : VarBytes (64 bytes for ED25519)          │
└────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────┐
│ Payload Format                                              │
├────────────────────────────────────────────────────────────┤
│ Flags           : 1 byte (compression, protection, type)   │
│ EncryptionType  : 1 byte                                   │
│ HashType        : 1 byte                                   │
│ Hash            : VarBytes (32 bytes for SHA256)           │
│ IV              : VarBytes (12-24 bytes)                   │
│ RecipientCount  : VarInt                                   │
│ EncryptedKeys[] : VarBytes[] (per recipient)               │
│ DataLength      : VarInt                                   │
│ EncryptedData   : bytes                                    │
└────────────────────────────────────────────────────────────┘
```

## Dependencies

### Production Dependencies

```xml
<PackageReference Include="Sorcha.Cryptography" Version="2.0.0" />
<PackageReference Include="System.Text.Json" Version="9.0.0" />
```

### Test Dependencies

```xml
<PackageReference Include="xUnit" Version="2.9.2" />
<PackageReference Include="FluentAssertions" Version="6.12.0" />
<PackageReference Include="Moq" Version="4.20.70" />
```

### Service Dependencies

- Sorcha.Cryptography (for signing, encryption, hashing)

## Migration/Integration Notes

### Usage Example

```csharp
// Create and sign a transaction
var tx = await new TransactionBuilder()
    .Create(TransactionVersion.V4)
    .WithPreviousTransaction(prevTxHash)
    .WithRecipients("ws1abc...", "ws1def...")
    .WithMetadata("""{"type": "document_transfer"}""")
    .AddPayload(documentData, new[] { "ws1abc..." }, new PayloadOptions
    {
        EncryptionType = EncryptionType.XCHACHA20_POLY1305,
        CompressionType = CompressionType.Balanced
    })
    .SignAsync(senderWifKey)
    .Build();

// Verify transaction
var verifier = new TransactionVerifier(crypto);
var result = await verifier.VerifyAsync(tx.Value);

// Decrypt payload
var manager = new PayloadManager(crypto);
var data = await manager.DecryptPayloadAsync(tx.Value.Payloads[0], recipientWifKey);
```

### Breaking Changes from Old Implementation

1. Async APIs throughout
2. Result types instead of exceptions
3. Separate library (not part of Cryptography)
4. V4 is default version

## Open Questions

1. Should we support streaming serialization for very large transactions?
2. How to handle payload size limits (10MB, 100MB)?
3. Should we add JSON-LD support for semantic web integration?
