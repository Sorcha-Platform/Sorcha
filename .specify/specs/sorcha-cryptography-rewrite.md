# Sorcha.Cryptography Library Rewrite Specification

**Version:** 1.0
**Date:** 2025-11-12
**Status:** Proposed
**Related Constitution:** [constitution.md](constitution.md)
**Related Plan:** [plan.md](plan.md)

## Executive Summary

This specification defines the requirements for rewriting the SorchaPlatformCryptography library into a clean, reusable, well-tested cryptography library named Sorcha.Cryptography that can be imported and used by external projects. The rewrite will remove unnecessary dependencies, eliminate Sorcha-specific coupling, provide comprehensive test coverage, and follow modern .NET best practices.

## Background

### Current State Analysis

The existing `SorchaPlatformCryptography` library has several issues that prevent it from being a reusable, standalone cryptography library:

#### Problems Identified

1. **Excessive Dependencies (24+ packages)**
   - Health checks (MongoDb, MySQL, Redis, UI) - not needed in crypto library
   - Dapr (AspNetCore, Client, Extensions) - infrastructure concern
   - Entity Framework Core (full suite) - not needed in crypto library
   - Serilog (multiple sinks) - logging should be optional
   - FluentValidation - validation not core to crypto
   - Swagger/Swashbuckle - API documentation tool (if API needed, use .NET 10 built-in OpenAPI + Scalar)
   - Test framework packages in library project (should be in test project only)

2. **Inappropriate Project References**
   - References RegisterCore project - creates circular coupling
   - Should have zero project dependencies

3. **Sorcha-Specific Coupling**
   - Transaction/Payload classes (v1-v4) are Sorcha-specific
   - Multiple versioned implementations suggest poor versioning strategy
   - Code is not generic enough for external use

4. **Test Coverage Issues**
   - Only 12 test files for a cryptography library
   - Test project has same dependency bloat as main project
   - Missing test vectors for standardized algorithms
   - Inadequate edge case and security testing

5. **API Design Issues**
   - No async/await support
   - Missing interfaces for testability and extensibility
   - Inconsistent error handling patterns
   - Limited XML documentation

### Goals

1. Create a **standalone, reusable cryptography library**
2. **Minimal dependencies** - only essential cryptographic libraries
3. **Comprehensive test coverage** (>90%) with test vectors
4. **Clean API design** following .NET conventions
5. **Well-documented** with XML docs and usage examples
6. **High performance** with async support where beneficial
7. **Secure by default** with proper key management
8. **Easy to consume** as a NuGet package

## Scope

### In Scope

#### Core Cryptographic Operations

1. **Key Management**
   - Key generation for ED25519, NIST P-256, RSA-4096
   - Key derivation and recovery
   - Mnemonic generation and recovery (BIP39-compatible)
   - Password-protected key storage
   - KeyRing and KeyChain management
   - Public key calculation from private keys

2. **Asymmetric Cryptography**
   - Digital signatures (ED25519, ECDSA P-256, RSA)
   - Signature verification
   - Public key encryption
   - Private key decryption
   - Support for multiple algorithms per interface

3. **Symmetric Cryptography**
   - AES-128-CBC
   - AES-256-CBC
   - AES-256-GCM
   - ChaCha20-Poly1305
   - XChaCha20-Poly1305

4. **Hashing Functions**
   - SHA-256
   - SHA-384
   - SHA-512
   - Blake2b (256-bit and 512-bit)
   - HMAC variants

5. **Encoding/Decoding Utilities**
   - Base58 encoding/decoding
   - Bech32 wallet address encoding/decoding
   - Hexadecimal conversion
   - Variable-length integer encoding
   - WIF (Wallet Import Format) support

6. **Data Utilities**
   - Compression (Deflate with configurable levels)
   - Decompression with validation
   - Secure random number generation
   - File type detection

### Out of Scope

1. Transaction building and formatting (Sorcha-specific)
2. Payload management (Sorcha-specific)
3. Register/ledger integration
4. Network communication
5. Database operations
6. Dapr integration
7. Health checks
8. Logging infrastructure (consumers can log)
9. API hosting/endpoints

## Technical Requirements

### Target Framework

- **.NET 10.0** (with .NET Standard 2.1 compatibility for broader reach)
- **C# 13** language features

### Dependencies

**Production Dependencies (Minimal)**

```xml
<ItemGroup>
  <!-- Core cryptography library -->
  <PackageReference Include="Sodium.Core" Version="1.3.4" />

  <!-- For async/await patterns (if needed) -->
  <PackageReference Include="System.Threading.Tasks.Extensions" Version="4.5.4" />
</ItemGroup>
```

**Development/Test Dependencies**

```xml
<ItemGroup>
  <!-- Test framework -->
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
  <PackageReference Include="xUnit" Version="2.9.2" />
  <PackageReference Include="xUnit.runner.visualstudio" Version="2.8.2" />

  <!-- Mocking -->
  <PackageReference Include="Moq" Version="4.20.70" />

  <!-- Code coverage -->
  <PackageReference Include="coverlet.collector" Version="6.0.2" />
  <PackageReference Include="coverlet.msbuild" Version="6.0.2" />

  <!-- Benchmarking -->
  <PackageReference Include="BenchmarkDotNet" Version="0.13.12" />

  <!-- Test utilities -->
  <PackageReference Include="FluentAssertions" Version="6.12.0" />
</ItemGroup>
```

### Project Structure

```
src/
  Sorcha.Cryptography/
    ├── Sorcha.Cryptography.csproj
    ├── Enums/
    │   ├── WalletNetworks.cs
    │   ├── HashType.cs
    │   ├── EncryptionType.cs
    │   ├── CompressionType.cs
    │   └── CryptoStatus.cs
    ├── Interfaces/
    │   ├── ICryptoModule.cs
    │   ├── IKeyManager.cs
    │   ├── IHashProvider.cs
    │   ├── ISymmetricCrypto.cs
    │   └── IEncodingProvider.cs
    ├── Core/
    │   ├── CryptoModule.cs
    │   ├── KeyManager.cs
    │   ├── HashProvider.cs
    │   ├── SymmetricCrypto.cs
    │   └── CryptoModuleBase.cs
    ├── Models/
    │   ├── CryptoKey.cs
    │   ├── KeySet.cs
    │   ├── KeyRing.cs
    │   └── KeyChain.cs
    ├── Utilities/
    │   ├── EncodingUtilities.cs
    │   ├── WalletUtilities.cs
    │   ├── CompressionUtilities.cs
    │   └── RandomnessProvider.cs
    └── Extensions/
        ├── EnumExtensions.cs
        └── ByteArrayExtensions.cs

tests/
  Sorcha.Cryptography.Tests/
    ├── Sorcha.Cryptography.Tests.csproj
    ├── Unit/
    │   ├── CryptoModuleTests.cs
    │   ├── KeyManagerTests.cs
    │   ├── HashProviderTests.cs
    │   ├── SymmetricCryptoTests.cs
    │   ├── EncodingUtilitiesTests.cs
    │   ├── WalletUtilitiesTests.cs
    │   └── CompressionUtilitiesTests.cs
    ├── Integration/
    │   ├── KeyRingIntegrationTests.cs
    │   ├── KeyChainIntegrationTests.cs
    │   └── EndToEndCryptoTests.cs
    ├── TestVectors/
    │   ├── ED25519TestVectors.cs
    │   ├── NISTP256TestVectors.cs
    │   ├── RSA4096TestVectors.cs
    │   └── HashTestVectors.cs
    ├── Performance/
    │   ├── SigningBenchmarks.cs
    │   ├── EncryptionBenchmarks.cs
    │   └── HashingBenchmarks.cs
    └── Security/
        ├── TimingAttackTests.cs
        ├── RandomnessTests.cs
        └── KeyGenerationSecurityTests.cs
```

## Functional Requirements

### FR-1: Key Generation

**Description:** Generate cryptographic key pairs for supported algorithms

**Algorithms:**
- ED25519 (EdDSA signatures)
- NIST P-256 (ECDSA signatures)
- RSA-4096 (RSA signatures)

**Interface:**
```csharp
public interface ICryptoModule
{
    /// <summary>
    /// Generates a new cryptographic key pair for the specified network type.
    /// </summary>
    /// <param name="network">The wallet network/algorithm type</param>
    /// <param name="seed">Optional seed for deterministic key generation</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Result containing status and generated key set</returns>
    Task<CryptoResult<KeySet>> GenerateKeySetAsync(
        WalletNetworks network,
        byte[]? seed = null,
        CancellationToken cancellationToken = default);
}
```

**Test Requirements:**
- Generate keys for each supported algorithm
- Verify key sizes match algorithm specifications
- Test deterministic generation with seeds
- Test non-deterministic generation
- Validate key pair correctness (sign/verify round trip)

### FR-2: Mnemonic Recovery Phrase

**Description:** Generate and recover keys from BIP39-compatible mnemonic phrases

**Requirements:**
- 12-word mnemonics for ED25519
- 12-word mnemonics for NIST P-256
- PEM format for RSA-4096
- Optional password protection
- Checksum validation

**Interface:**
```csharp
public interface IKeyManager
{
    /// <summary>
    /// Creates a master key ring with mnemonic recovery phrase.
    /// </summary>
    Task<CryptoResult<KeyRing>> CreateMasterKeyRingAsync(
        WalletNetworks network,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers a key ring from a mnemonic recovery phrase.
    /// </summary>
    Task<CryptoResult<KeyRing>> RecoverMasterKeyRingAsync(
        string mnemonic,
        string? password = null,
        CancellationToken cancellationToken = default);
}
```

**Test Requirements:**
- Generate mnemonic and recover same keys
- Test with and without passwords
- Validate checksum detection
- Test invalid mnemonics
- Cross-check with known BIP39 test vectors

### FR-3: Digital Signatures

**Description:** Sign data and verify signatures for all supported algorithms

**Operations:**
- Sign hash with private key
- Verify signature with public key
- Support detached signatures

**Interface:**
```csharp
public interface ICryptoModule
{
    /// <summary>
    /// Signs a hash with the specified private key.
    /// </summary>
    Task<CryptoResult<byte[]>> SignAsync(
        byte[] hash,
        byte network,
        byte[] privateKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a signature against a hash and public key.
    /// </summary>
    Task<CryptoStatus> VerifyAsync(
        byte[] signature,
        byte[] hash,
        byte network,
        byte[] publicKey,
        CancellationToken cancellationToken = default);
}
```

**Test Requirements:**
- Sign and verify for each algorithm
- Test with known test vectors (NIST, RFC test vectors)
- Verify signature fails with wrong key
- Verify signature fails with tampered data
- Test signature malleability protection

### FR-4: Asymmetric Encryption

**Description:** Encrypt and decrypt data using public/private key pairs

**Support:**
- ED25519 (via Curve25519 conversion)
- NIST P-256 (ECIES)
- RSA-4096 (OAEP with SHA-256)

**Interface:**
```csharp
public interface ICryptoModule
{
    /// <summary>
    /// Encrypts data with a public key.
    /// </summary>
    Task<CryptoResult<byte[]>> EncryptAsync(
        byte[] data,
        byte network,
        byte[] publicKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts data with a private key.
    /// </summary>
    Task<CryptoResult<byte[]>> DecryptAsync(
        byte[] ciphertext,
        byte network,
        byte[] privateKey,
        CancellationToken cancellationToken = default);
}
```

**Test Requirements:**
- Encrypt/decrypt round trip for each algorithm
- Verify decryption fails with wrong key
- Test with varying data sizes
- Test maximum data size limits (RSA)
- Validate authenticated encryption (AEAD where applicable)

### FR-5: Symmetric Encryption

**Description:** Symmetric encryption with multiple algorithms

**Algorithms:**
- AES-128-CBC
- AES-256-CBC
- AES-256-GCM (authenticated)
- ChaCha20-Poly1305 (authenticated)
- XChaCha20-Poly1305 (authenticated)

**Interface:**
```csharp
public interface ISymmetricCrypto
{
    /// <summary>
    /// Encrypts data with symmetric encryption.
    /// </summary>
    Task<CryptoResult<SymmetricCiphertext>> EncryptAsync(
        byte[] plaintext,
        EncryptionType encryptionType = EncryptionType.XCHACHA20_POLY1305,
        byte[]? key = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts data with symmetric encryption.
    /// </summary>
    Task<CryptoResult<byte[]>> DecryptAsync(
        SymmetricCiphertext ciphertext,
        CancellationToken cancellationToken = default);
}

public class SymmetricCiphertext
{
    public byte[] Data { get; init; }
    public byte[] Key { get; init; }
    public byte[] IV { get; init; }
    public EncryptionType Type { get; init; }
}
```

**Test Requirements:**
- Encrypt/decrypt for each algorithm
- Verify authentication tag validation (GCM, Poly1305)
- Test with different key sizes
- Test IV/nonce uniqueness
- Performance benchmarks

### FR-6: Hash Functions

**Description:** Cryptographic hash functions

**Algorithms:**
- SHA-256
- SHA-384
- SHA-512
- Blake2b-256
- Blake2b-512

**Interface:**
```csharp
public interface IHashProvider
{
    /// <summary>
    /// Computes hash of data using specified algorithm.
    /// </summary>
    byte[] ComputeHash(byte[] data, HashType hashType = HashType.SHA256);

    /// <summary>
    /// Computes hash of data stream using specified algorithm.
    /// </summary>
    Task<byte[]> ComputeHashAsync(
        Stream dataStream,
        HashType hashType = HashType.SHA256,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes HMAC of data.
    /// </summary>
    byte[] ComputeHMAC(byte[] data, byte[] key, HashType hashType = HashType.SHA256);
}
```

**Test Requirements:**
- Test each hash algorithm with known test vectors
- Verify hash determinism
- Test empty input
- Test large inputs (streaming)
- HMAC test vectors

### FR-7: Wallet Address Encoding

**Description:** Convert public keys to/from wallet addresses

**Format:**
- Bech32 encoding with "ws1" prefix
- Network byte identification
- Checksum validation

**Interface:**
```csharp
public interface IWalletUtilities
{
    /// <summary>
    /// Converts a public key to a wallet address.
    /// </summary>
    string? PublicKeyToWallet(byte[] publicKey, byte network);

    /// <summary>
    /// Converts a wallet address to public key and network.
    /// </summary>
    (byte Network, byte[] PublicKey)? WalletToPublicKey(string walletAddress);

    /// <summary>
    /// Validates multiple wallet addresses.
    /// </summary>
    (bool[] Valid, CryptoKey[] ValidWallets) ValidateWallets(string[] walletAddresses);
}
```

**Test Requirements:**
- Round-trip conversion
- Invalid address detection
- Checksum validation
- Network byte validation
- Batch validation

### FR-8: WIF (Wallet Import Format)

**Description:** Convert private keys to/from WIF format

**Features:**
- Base58Check encoding
- Network identification
- Checksum validation

**Interface:**
```csharp
public interface IWalletUtilities
{
    /// <summary>
    /// Converts a private key to WIF format.
    /// </summary>
    string? PrivateKeyToWIF(byte[] privateKey, byte network);

    /// <summary>
    /// Converts WIF to private key and network.
    /// </summary>
    (byte Network, byte[] PrivateKey)? WIFToPrivateKey(string wif);
}
```

**Test Requirements:**
- Round-trip conversion
- Checksum validation
- Invalid WIF detection
- Bitcoin/cryptocurrency WIF compatibility

### FR-9: KeyChain Management

**Description:** Manage multiple key rings with encryption

**Features:**
- Store multiple named key rings
- Password-protected export
- Encrypted storage (Argon2id + XChaCha20-Poly1305)
- Compression

**Interface:**
```csharp
public class KeyChain
{
    /// <summary>
    /// Adds a key ring to the chain.
    /// </summary>
    CryptoStatus AddKeyRing(string name, KeyRing keyRing);

    /// <summary>
    /// Retrieves a key ring by name.
    /// </summary>
    CryptoResult<KeyRing> GetKeyRing(string name);

    /// <summary>
    /// Removes a key ring from the chain.
    /// </summary>
    CryptoStatus RemoveKeyRing(string name);

    /// <summary>
    /// Exports the entire keychain with password protection.
    /// </summary>
    Task<CryptoResult<byte[]>> ExportAsync(
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a keychain from encrypted data.
    /// </summary>
    Task<CryptoStatus> ImportAsync(
        byte[] encryptedData,
        string password,
        ICryptoModule? cryptoModule = null,
        CancellationToken cancellationToken = default);
}
```

**Test Requirements:**
- Add/retrieve/remove operations
- Export/import round trip
- Password verification
- Duplicate name detection
- Empty chain handling

### FR-10: Compression

**Description:** Data compression and decompression

**Features:**
- Deflate algorithm
- Configurable compression levels
- File type detection (avoid compressing already compressed)
- Format identification marker

**Interface:**
```csharp
public interface ICompressionUtilities
{
    /// <summary>
    /// Compresses data if beneficial.
    /// </summary>
    (byte[]? Data, bool WasCompressed) Compress(
        byte[] data,
        CompressionType type = CompressionType.Balanced);

    /// <summary>
    /// Decompresses data if it was compressed.
    /// </summary>
    byte[]? Decompress(byte[] data);
}
```

**Test Requirements:**
- Compress/decompress round trip
- File type detection
- Small data (shouldn't compress)
- Already compressed data (shouldn't re-compress)
- Compression ratio measurements

## Non-Functional Requirements

### NFR-1: Security

**Requirements:**
- Constant-time operations where possible (timing attack resistance)
- Secure random number generation (cryptographically secure)
- Key zeroization when no longer needed
- No cryptographic keys in exception messages
- Memory protection for sensitive data
- Side-channel attack resistance considerations

**Testing:**
- Timing attack tests
- Random number quality tests (NIST SP 800-22)
- Key zeroization verification
- Exception message sanitization

### NFR-2: Performance

**Targets:**
- Key generation: < 100ms for ED25519/NIST P-256, < 500ms for RSA-4096
- Signing: < 10ms for ED25519/NIST P-256, < 50ms for RSA-4096
- Hashing: > 100 MB/s for SHA-256
- Symmetric encryption: > 200 MB/s for ChaCha20-Poly1305

**Testing:**
- BenchmarkDotNet performance tests
- Comparison with baseline implementations
- Memory allocation profiling
- Large data performance tests

### NFR-3: Test Coverage

**Requirements:**
- Minimum 90% code coverage
- 100% coverage for core cryptographic operations
- All public APIs tested
- Test vectors from standards (NIST, RFC)
- Edge case testing
- Security-specific tests

**Test Categories:**
1. Unit tests (per component)
2. Integration tests (cross-component)
3. Test vectors (standards compliance)
4. Performance benchmarks
5. Security tests

### NFR-4: API Design

**Principles:**
- Async/await for I/O and long-running operations
- CancellationToken support
- Result types instead of exceptions for expected failures
- Clear XML documentation on all public members
- Consistent naming conventions
- Fluent interfaces where appropriate

**Example Result Pattern:**
```csharp
public class CryptoResult<T>
{
    public CryptoStatus Status { get; init; }
    public T? Value { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsSuccess => Status == CryptoStatus.Success;

    public static CryptoResult<T> Success(T value) => new() { Status = CryptoStatus.Success, Value = value };
    public static CryptoResult<T> Failure(CryptoStatus status, string? error = null) => new() { Status = status, ErrorMessage = error };
}
```

### NFR-5: Documentation

**Requirements:**
- XML documentation on all public types and members
- README with quick start guide
- API documentation (generated from XML)
- Code examples for common scenarios
- Migration guide from SorchaPlatformCryptography
- Security best practices guide

**Documentation Sections:**
1. Getting Started
2. Core Concepts
3. API Reference
4. Code Examples
5. Security Considerations
6. Performance Tuning
7. Migration Guide

### NFR-6: Packaging

**Requirements:**
- NuGet package with semantic versioning
- Strong-name signing
- Deterministic builds
- Source link support
- Symbol packages
- Target framework (.NET 10.0)

**Package Metadata:**
```xml
<PropertyGroup>
  <PackageId>Sorcha.Cryptography</PackageId>
  <Version>2.0.0</Version>
  <Authors>Sorcha Development Team</Authors>
  <Company>Sorcha Contributors</Company>
  <Product>Sorcha.Cryptography</Product>
  <Description>Standalone cryptography library for the Sorcha platform, providing key management, digital signatures, encryption, and encoding utilities.</Description>
  <PackageTags>cryptography;Sorcha;ed25519;ecdsa;rsa;encryption;signing</PackageTags>
  <RepositoryUrl>https://github.com/Sorcha/Sorcha</RepositoryUrl>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
</PropertyGroup>
```

## Migration Strategy

### Phase 1: Create New Library (Week 1-2)

1. Create new `Sorcha.Cryptography` project
2. Define interfaces and models
3. Set up project structure
4. Configure build and packaging

### Phase 2: Core Implementation (Week 3-4)

1. Implement `CryptoModule` with key generation, signing, verification
2. Implement `KeyManager` with mnemonics
3. Implement `SymmetricCrypto`
4. Implement `HashProvider`

### Phase 3: Utilities Implementation (Week 5)

1. Implement encoding utilities (Base58, Bech32, Hex)
2. Implement wallet utilities
3. Implement compression utilities
4. Implement KeyChain

### Phase 4: Testing (Week 6-7)

1. Unit tests for all components (target >90% coverage)
2. Integration tests
3. Test vector validation
4. Performance benchmarks
5. Security tests

### Phase 5: Documentation (Week 8)

1. XML documentation
2. README and guides
3. Code examples
4. Migration guide
5. API documentation generation

### Phase 6: Integration (Week 9-10)

1. Update Sorcha projects to use new library
2. Deprecate old SorchaPlatformCryptography
3. Regression testing
4. Performance validation

## Success Criteria

1. **Functionality:** All cryptographic operations working correctly
2. **Test Coverage:** >90% code coverage with comprehensive test suite
3. **Performance:** Meets or exceeds performance targets
4. **Security:** Passes security audit (timing attacks, randomness, etc.)
5. **Documentation:** Complete API documentation and guides
6. **Usability:** Successfully integrated into at least one external project
7. **Quality:** Zero critical bugs, minimal technical debt
8. **Packaging:** Published as NuGet package

## Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Breaking changes affect Sorcha platform | High | Medium | Maintain compatibility layer, phased migration |
| Performance regression | Medium | Low | Comprehensive benchmarks, performance testing |
| Security vulnerabilities | Critical | Low | Security audit, test vectors, best practices |
| Incomplete test coverage | Medium | Medium | TDD approach, coverage reporting, CI enforcement |
| API design issues | Medium | Medium | Design review, prototype validation, community feedback |
| Cryptographic algorithm errors | Critical | Low | Use test vectors, peer review, external audit |

## References

- [Current SorchaPlatformCryptography](../../src/Common/SorchaPlatformCryptography/)
- [libsodium documentation](https://doc.libsodium.org/)
- [NIST Cryptographic Standards](https://csrc.nist.gov/publications)
- [RFC 8032 - EdDSA](https://www.rfc-editor.org/rfc/rfc8032)
- [BIP39 - Mnemonic Code](https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki)
- [.NET Cryptography Best Practices](https://learn.microsoft.com/en-us/dotnet/standard/security/cryptography-model)

---

**Document Control**
- **Created:** 2025-11-12
- **Owner:** Sorcha Architecture Team
- **Review Schedule:** Weekly during implementation
- **Next Review:** 2025-11-19
