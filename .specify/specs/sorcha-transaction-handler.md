# Sorcha.TransactionHandler Library Specification

**Version:** 1.1
**Date:** 2025-11-13
**Status:** Proposed
**Related Constitution:** [constitution.md](../constitution.md)
**Related Specification:** [Sorcha-cryptography-rewrite.md](Sorcha-cryptography-rewrite.md)

**Related Documentation:**
- [Blockchain Transaction Format - JSON-LD Specification](../../docs/reference/blockchain-transaction-format.md)

## Executive Summary

This specification defines the requirements for creating a standalone, reusable transaction and payload management library named **Sorcha.TransactionHandler**. This library will handle transaction creation, signing, verification, serialization, and multi-recipient payload encryption for the Sorcha distributed ledger platform.

## Background

### Current State Analysis

The existing transaction and payload management code is embedded within `SorchaPlatformCryptography` and has several architectural issues:

#### Problems Identified

1. **Tight Coupling with Cryptography**
   - Transaction classes mixed with cryptographic primitives
   - Difficult to version independently
   - Cannot update crypto without affecting transactions

2. **Multiple Transaction Versions (v1-v4)**
   - Four different implementations with duplicated code
   - Inconsistent interfaces across versions
   - No clear versioning strategy
   - Difficult to maintain backward compatibility

3. **Complex Payload Management**
   - Per-recipient encryption logic spread across classes
   - Compression and encryption tightly coupled
   - Difficult to test payload operations independently

4. **Serialization Complexity**
   - Binary format with variable-length encoding
   - JSON format with different structure
   - Transport format for network transmission
   - No clear separation of concerns

5. **Limited Extensibility**
   - Hard to add new transaction types
   - Difficult to add custom metadata fields
   - No plugin architecture for custom payload types

### Goals

1. **Separation of Concerns** - Transaction logic separate from cryptography
2. **Clean Versioning** - Single implementation with version compatibility
3. **Modular Design** - Payloads, transactions, and serialization decoupled
4. **Comprehensive Testing** - >90% coverage with integration tests
5. **Well-Documented** - Clear API with usage examples
6. **Backward Compatible** - Support reading v1-v4 transactions
7. **Forward Compatible** - Design for future versions

## Scope

### In Scope

#### Core Transaction Management

1. **Transaction Builder**
   - Fluent API for transaction creation
   - Support for all transaction versions (v1-v4)
   - Transaction validation
   - Metadata management

2. **Transaction Signing & Verification**
   - Integration with Sorcha.Cryptography for signing
   - Double SHA-256 hashing for signatures
   - Multi-algorithm signature verification
   - Timestamp management

3. **Payload Management**
   - Multi-recipient payload encryption
   - Per-recipient access control
   - Payload compression with file type detection
   - Payload verification (hash validation)
   - Sparse payload support (metadata only, no data)

4. **Serialization Formats**
   - Binary serialization (variable-length encoding)
   - JSON serialization (for APIs)
   - JSON-LD serialization (for semantic web integration)
   - Transport format (for network transmission)
   - Format conversion utilities

5. **Transaction Versioning**
   - Version detection and routing
   - Backward compatibility (read v1-v4)
   - Forward compatibility design
   - Version migration utilities

### Out of Scope

1. Cryptographic primitives (handled by Sorcha.Cryptography)
2. Network communication and transport (handled by Sorcha.Peer.Service)
3. Database persistence and storage (managed by Aspire)
4. Register/ledger integration
5. Consensus mechanisms
6. Smart contract execution
7. Transaction pool management

## Technical Requirements

### Target Framework

- **.NET 10.0** (with .NET Standard 2.1 for compatibility)
- **C# 12** language features

### Dependencies

**Production Dependencies:**
```xml
<ItemGroup>
  <!-- Core cryptography -->
  <PackageReference Include="Sorcha.Cryptography" Version="2.0.0" />

  <!-- JSON serialization -->
  <PackageReference Include="System.Text.Json" Version="9.0.0" />
</ItemGroup>
```

**Development/Test Dependencies:**
```xml
<ItemGroup>
  <!-- Test framework -->
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
  <PackageReference Include="xUnit" Version="2.9.2" />
  <PackageReference Include="xUnit.runner.visualstudio" Version="2.8.2" />
  <PackageReference Include="Moq" Version="4.20.70" />
  <PackageReference Include="FluentAssertions" Version="6.12.0" />
  <PackageReference Include="coverlet.collector" Version="6.0.2" />
</ItemGroup>
```

### Project Structure

```
src/
  common/
    Sorcha.TransactionHandler/
      ├── Sorcha.TransactionHandler.csproj
      ├── Enums/
      │   ├── TransactionVersion.cs
      │   ├── TransactionStatus.cs
      │   ├── PayloadType.cs
      │   └── SerializationFormat.cs
      ├── Interfaces/
      │   ├── ITransaction.cs
      │   ├── ITransactionBuilder.cs
      │   ├── IPayload.cs
      │   ├── IPayloadManager.cs
      │   ├── ITransactionSerializer.cs
      │   └── ITransactionVerifier.cs
      ├── Core/
      │   ├── Transaction.cs
      │   ├── TransactionBuilder.cs
      │   ├── TransactionVerifier.cs
      │   └── TransactionFactory.cs
      ├── Payload/
      │   ├── Payload.cs
      │   ├── PayloadManager.cs
      │   ├── PayloadOptions.cs
      │   └── PayloadContainer.cs
      ├── Serialization/
      │   ├── BinarySerializer.cs
      │   ├── JsonSerializer.cs
      │   ├── JsonLdSerializer.cs
      │   ├── TransportSerializer.cs
      │   └── SerializationHelpers.cs
      ├── Models/
      │   ├── TransactionModel.cs
      │   ├── TransactionMetadata.cs
      │   ├── PayloadInfo.cs
      │   └── TransactionResult.cs
      └── Versioning/
          ├── VersionDetector.cs
          ├── VersionRouter.cs
          └── BackwardCompatibility.cs

tests/
  Sorcha.TransactionHandler.Tests/
    ├── Sorcha.TransactionHandler.Tests.csproj
    ├── Unit/
    │   ├── TransactionBuilderTests.cs
    │   ├── PayloadManagerTests.cs
    │   ├── SerializerTests.cs
    │   └── VersioningTests.cs
    ├── Integration/
    │   ├── EndToEndTransactionTests.cs
    │   ├── MultiRecipientTests.cs
    │   └── BackwardCompatibilityTests.cs
    └── Performance/
        ├── TransactionBenchmarks.cs
        └── PayloadBenchmarks.cs
```

## Functional Requirements

### FR-1: Transaction Builder (Fluent API)

**Description:** Create transactions using a fluent, builder-pattern API.

**Interface:**
```csharp
public interface ITransactionBuilder
{
    /// <summary>
    /// Creates a new transaction with specified version.
    /// </summary>
    ITransactionBuilder Create(TransactionVersion version = TransactionVersion.V4);

    /// <summary>
    /// Sets the previous transaction hash for chaining.
    /// </summary>
    ITransactionBuilder WithPreviousTransaction(string txHash);

    /// <summary>
    /// Adds recipient wallets who can access the transaction.
    /// </summary>
    ITransactionBuilder WithRecipients(params string[] walletAddresses);

    /// <summary>
    /// Sets transaction metadata (must be valid JSON).
    /// </summary>
    ITransactionBuilder WithMetadata(string jsonMetadata);

    /// <summary>
    /// Adds a payload to the transaction.
    /// </summary>
    ITransactionBuilder AddPayload(
        byte[] data,
        string[] recipientWallets,
        PayloadOptions? options = null);

    /// <summary>
    /// Signs the transaction with the sender's private key.
    /// </summary>
    Task<ITransactionBuilder> SignAsync(
        string wifPrivateKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds and returns the final transaction.
    /// </summary>
    TransactionResult<ITransaction> Build();
}
```

**Usage Example:**
```csharp
var transaction = await new TransactionBuilder()
    .Create(TransactionVersion.V4)
    .WithRecipients("ws1qyqszqgp...", "ws1pqpszqgp...")
    .WithMetadata("{\"type\": \"document_transfer\"}")
    .AddPayload(documentData, new[] { "ws1qyqszqgp..." })
    .SignAsync(senderWifKey)
    .Build();
```

### FR-2: Transaction Signing & Verification

**Description:** Sign transactions with double SHA-256 and verify signatures.

**Signing Process:**
1. Serialize transaction data (version, recipients, payloads, metadata)
2. Compute double SHA-256 hash: `SHA256(SHA256(data))`
3. Sign hash with private key (using Sorcha.Cryptography)
4. Calculate sender's public key from private key
5. Generate final transaction hash

**Verification Process:**
1. Re-serialize transaction data
2. Compute double SHA-256 hash
3. Verify signature with sender's public key
4. Verify all payload hashes

**Interface:**
```csharp
public interface ITransactionVerifier
{
    /// <summary>
    /// Verifies transaction signature and payload integrity.
    /// </summary>
    Task<TransactionVerificationResult> VerifyAsync(
        ITransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies only the signature (not payloads).
    /// </summary>
    Task<bool> VerifySignatureAsync(
        ITransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies only the payload hashes (not signature).
    /// </summary>
    Task<bool> VerifyPayloadsAsync(
        ITransaction transaction,
        CancellationToken cancellationToken = default);
}
```

### FR-3: Multi-Recipient Payload Management

**Description:** Encrypt payloads for multiple recipients with per-recipient access control.

**Encryption Process:**
```
Raw Data
  ↓
[Optional] Compression (Deflate)
  ↓
Symmetric Encryption (ChaCha20-Poly1305, AES-GCM, etc.)
  - Generate random symmetric key (32 bytes)
  - Generate random IV (12-24 bytes)
  ↓
For each recipient:
  - Asymmetric encrypt symmetric key with recipient's public key
  - Store encrypted key in payload container
  ↓
Compute hash of original data (before encryption)
  ↓
Store in Payload:
  - Encrypted data
  - Array of encrypted keys (one per recipient)
  - IV
  - Hash
  - Encryption/compression metadata
```

**Interface:**
```csharp
public interface IPayloadManager
{
    /// <summary>
    /// Adds an encrypted payload to the transaction.
    /// </summary>
    Task<PayloadResult> AddPayloadAsync(
        byte[] data,
        string[] recipientWallets,
        PayloadOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all payloads the specified wallet can access.
    /// </summary>
    Task<IEnumerable<IPayload>> GetAccessiblePayloadsAsync(
        string walletAddress,
        string wifPrivateKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts a specific payload for a recipient.
    /// </summary>
    Task<PayloadResult<byte[]>> DecryptPayloadAsync(
        IPayload payload,
        string wifPrivateKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants access to a payload for an additional recipient.
    /// </summary>
    Task<TransactionStatus> GrantPayloadAccessAsync(
        uint payloadId,
        string recipientWallet,
        string ownerWifKey,
        CancellationToken cancellationToken = default);
}
```

### FR-4: Transaction Serialization

**Description:** Serialize transactions to multiple formats (binary, JSON, transport).

**Binary Format:**
- Variable-length integer encoding (VarInt)
- Compact representation for network efficiency
- Same format as current v1-v4 implementations

**JSON Format:**
- Human-readable
- For API responses
- Includes metadata expansion

**Transport Format:**
- Optimized for network transmission
- Includes RegisterId and TxId
- Minimal overhead

**Interface:**
```csharp
public interface ITransactionSerializer
{
    /// <summary>
    /// Serializes transaction to binary format.
    /// </summary>
    byte[] SerializeToBinary(ITransaction transaction);

    /// <summary>
    /// Deserializes transaction from binary format.
    /// </summary>
    ITransaction DeserializeFromBinary(byte[] data);

    /// <summary>
    /// Serializes transaction to JSON.
    /// </summary>
    string SerializeToJson(ITransaction transaction);

    /// <summary>
    /// Deserializes transaction from JSON.
    /// </summary>
    ITransaction DeserializeFromJson(string json);

    /// <summary>
    /// Creates transport format for network transmission.
    /// </summary>
    TransportPacket CreateTransportPacket(ITransaction transaction);
}
```

### FR-5: Transaction Versioning & Compatibility

**Description:** Support multiple transaction versions with backward compatibility.

**Version Strategy:**
- V4 is the current/default version
- Read support for V1, V2, V3 (backward compatible)
- Write only V4 (forward-looking)
- Version detection from binary format

**Interface:**
```csharp
public interface IVersionDetector
{
    /// <summary>
    /// Detects transaction version from binary data.
    /// </summary>
    TransactionVersion DetectVersion(byte[] data);

    /// <summary>
    /// Detects transaction version from JSON.
    /// </summary>
    TransactionVersion DetectVersion(string json);
}

public interface ITransactionFactory
{
    /// <summary>
    /// Creates a transaction of the specified version.
    /// </summary>
    ITransaction Create(TransactionVersion version);

    /// <summary>
    /// Deserializes transaction, auto-detecting version.
    /// </summary>
    ITransaction Deserialize(byte[] data);
}
```

### FR-6: Payload Options & Configuration

**Description:** Configure payload encryption, compression, and protection settings.

**Model:**
```csharp
public class PayloadOptions
{
    /// <summary>
    /// Encryption algorithm to use.
    /// </summary>
    public EncryptionType EncryptionType { get; set; }
        = EncryptionType.XCHACHA20_POLY1305;

    /// <summary>
    /// Compression level.
    /// </summary>
    public CompressionType CompressionType { get; set; }
        = CompressionType.Balanced;

    /// <summary>
    /// Hash algorithm for payload verification.
    /// </summary>
    public HashType HashType { get; set; }
        = HashType.SHA256;

    /// <summary>
    /// Payload type identifier.
    /// </summary>
    public PayloadType PayloadType { get; set; }
        = PayloadType.Data;

    /// <summary>
    /// Marks payload as protected (cannot be modified after creation).
    /// </summary>
    public bool IsProtected { get; set; } = false;

    /// <summary>
    /// Custom user flags (application-specific).
    /// </summary>
    public uint UserFlags { get; set; } = 0;
}
```

## Non-Functional Requirements

### NFR-1: Performance

**Targets:**
- Transaction creation: < 100ms (excluding signing)
- Transaction signing: < 50ms (ED25519/NISTP256)
- Payload encryption (1 recipient): < 20ms
- Binary serialization: < 10ms
- JSON serialization: < 50ms

### NFR-2: Test Coverage

**Requirements:**
- Minimum 90% code coverage
- 100% coverage for critical paths (signing, verification)
- Integration tests for end-to-end flows
- Backward compatibility tests (v1-v4)
- Performance benchmarks

### NFR-3: API Design

**Principles:**
- Fluent, builder-pattern APIs
- Async/await throughout
- CancellationToken support
- Result types for expected failures
- Complete XML documentation

### NFR-4: Security

**Requirements:**
- No sensitive data in exceptions
- Proper disposal of key material
- Constant-time comparisons where applicable
- Validate all inputs
- Prevent payload tampering

### NFR-5: Backward Compatibility

**Requirements:**
- Read all transaction versions (v1-v4)
- Verify old transaction signatures
- Decrypt old payloads
- Migration utilities for version upgrades

## Architecture

### Component Diagram

```
┌────────────────────────────────────────────────────────┐
│          Sorcha.TransactionHandler                     │
├────────────────────────────────────────────────────────┤
│                                                         │
│  ┌──────────────────┐        ┌──────────────────┐    │
│  │ TransactionBuilder│───────▶│   Transaction    │    │
│  └──────────────────┘        └──────────────────┘    │
│           │                            │               │
│           │                            │               │
│           ▼                            ▼               │
│  ┌──────────────────┐        ┌──────────────────┐    │
│  │  PayloadManager  │───────▶│   Payload Array  │    │
│  └──────────────────┘        └──────────────────┘    │
│           │                            │               │
│           │                            │               │
│           ▼                            ▼               │
│  ┌──────────────────────────────────────────────┐    │
│  │        Transaction Serializers               │    │
│  │  ┌─────────┐  ┌─────────┐  ┌──────────┐    │    │
│  │  │ Binary  │  │  JSON   │  │Transport │    │    │
│  │  └─────────┘  └─────────┘  └──────────┘    │    │
│  └──────────────────────────────────────────────┘    │
│           │                            │               │
│           └────────────────┬───────────┘               │
│                            ▼                           │
│  ┌──────────────────────────────────────────────┐    │
│  │        Version Router & Compatibility        │    │
│  └──────────────────────────────────────────────┘    │
│                                                         │
└────────────────┬────────────────────────────┬─────────┘
                 │                             │
                 ▼                             ▼
        ┌─────────────────┐         ┌──────────────────┐
        │Sorcha.Cryptography│         │System.Text.Json│
        └─────────────────┘         └──────────────────┘
```

### Data Flow: Transaction Creation

```
1. TransactionBuilder.Create()
   ↓
2. Builder.WithRecipients(["wallet1", "wallet2"])
   ↓
3. Builder.AddPayload(data, ["wallet1"])
   ↓
   3a. PayloadManager.AddPayloadAsync()
       ↓
   3b. Compression (if beneficial)
       ↓
   3c. Symmetric encryption (ChaCha20)
       ↓
   3d. Per-recipient key encryption
       ├─▶ Encrypt for wallet1
       └─▶ (wallet2 cannot access this payload)
       ↓
   3e. Hash computation (SHA-256)
   ↓
4. Builder.WithMetadata("{...}")
   ↓
5. Builder.SignAsync(wifKey)
   ↓
   5a. Serialize transaction data
       ↓
   5b. Double SHA-256 hash
       ↓
   5c. Sign with Sorcha.Cryptography
       ↓
   5d. Calculate sender wallet
       ↓
   5e. Generate final TxId
   ↓
6. Builder.Build()
   ↓
7. Return ITransaction
```

## Migration Strategy

### Phase 1: Create New Library (Week 1-2)

1. Create `Sorcha.TransactionHandler` project
2. Define interfaces and models
3. Set up project structure and dependencies
4. Configure build and testing

### Phase 2: Core Implementation (Week 3-4)

1. Implement TransactionBuilder (fluent API)
2. Implement Transaction model
3. Implement PayloadManager
4. Implement serializers (binary, JSON, transport)

### Phase 3: Versioning Support (Week 5)

1. Implement version detection
2. Implement backward compatibility (v1-v4)
3. Implement version router
4. Migration utilities

### Phase 4: Testing (Week 6-7)

1. Unit tests (>90% coverage)
2. Integration tests
3. Backward compatibility tests
4. Performance benchmarks

### Phase 5: Documentation (Week 8)

1. XML documentation
2. Usage examples
3. Migration guide
4. API documentation

### Phase 6: Integration (Week 9-10)

1. Update Sorcha services
2. Regression testing
3. Performance validation
4. NuGet package publish

## Success Criteria

1. **Functionality:** All transaction operations working
2. **Test Coverage:** >90% code coverage
3. **Performance:** Meets NFR targets
4. **Compatibility:** Reads v1-v4 transactions
5. **Documentation:** Complete API docs and examples
6. **Integration:** Successfully integrated into SorchaV3
7. **Quality:** Zero critical bugs

## Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Breaking changes to transaction format | High | Low | Maintain v1-v4 compatibility, extensive testing |
| Performance regression | Medium | Medium | Benchmarks, profiling, optimization |
| Cryptography integration issues | High | Low | Use stable Sorcha.Cryptography v2.0 API |
| Incomplete backward compatibility | High | Medium | Comprehensive v1-v4 test suite |

## References

- [Current Transaction Implementation](../../src/Common/SorchaPlatformCryptography/)
- [Sorcha.Cryptography Specification](Sorcha-cryptography-rewrite.md)
- [Bitcoin Transaction Format](https://developer.bitcoin.org/reference/transactions.html)

---

**Document Control**
- **Created:** 2025-11-12
- **Owner:** SorchaV3 Architecture Team
- **Review Schedule:** Weekly during implementation
