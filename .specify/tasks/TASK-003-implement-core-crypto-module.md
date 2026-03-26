# Task: Implement Core Cryptographic Module

**ID:** TASK-003
**Status:** Not Started
**Priority:** Critical
**Estimate:** 16 hours
**Assignee:** Claude Code
**Created:** 2025-11-12
**Updated:** 2025-11-12

## Context

Implement the core cryptographic module that handles key generation, digital signatures, encryption/decryption, and public key operations. This is the heart of the library and must be implemented with security and correctness as top priorities.

**Related Specifications:**
- [Sorcha.Cryptography Rewrite Spec - FR-1 through FR-4](../specs/sorcha-cryptography-rewrite.md#fr-1-key-generation)
- [Current CryptoModule Implementation](../../src/Common/SorchaPlatformCryptography/CryptoModule.cs)

**Dependencies:**
- TASK-001 (Project setup)
- TASK-002 (Enums and models)

## Objective

Implement ICryptoModule interface and CryptoModule class with full support for ED25519, NIST P-256, and RSA-4096 algorithms for key generation, signing, verification, encryption, and decryption.

## Implementation Details

### Files to Create

1. **Interfaces/ICryptoModule.cs** - Interface definition
2. **Core/CryptoModuleBase.cs** - Abstract base class
3. **Core/CryptoModule.cs** - Concrete implementation

### Technical Approach

**Interface: Interfaces/ICryptoModule.cs**
```csharp
namespace Sorcha.Cryptography.Interfaces;

/// <summary>
/// Provides cryptographic operations for key management, signing, and encryption.
/// </summary>
public interface ICryptoModule
{
    /// <summary>
    /// Generates a new cryptographic key pair.
    /// </summary>
    /// <param name="network">The wallet network/algorithm type.</param>
    /// <param name="seed">Optional seed for deterministic generation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CryptoResult<KeySet>> GenerateKeySetAsync(
        WalletNetworks network,
        byte[]? seed = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers a key set from key data.
    /// </summary>
    Task<CryptoResult<KeySet>> RecoverKeySetAsync(
        WalletNetworks network,
        byte[] keyData,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs a hash with a private key.
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

    /// <summary>
    /// Calculates public key from private key.
    /// </summary>
    Task<CryptoResult<byte[]>> CalculatePublicKeyAsync(
        byte network,
        byte[] privateKey,
        CancellationToken cancellationToken = default);
}
```

**Core Implementation Highlights:**

1. **ED25519 Operations:**
   - Use Sodium.PublicKeyAuth for key generation and signing
   - Use Sodium.SealedPublicKeyBox for encryption (via Curve25519 conversion)
   - Handle 32-byte public keys and 64-byte private keys

2. **NIST P-256 Operations:**
   - Use System.Security.Cryptography.ECDsa for signing
   - Use System.Security.Cryptography.ECDiffieHellman for ECIES encryption
   - Handle variable-length encoded public keys (X and Y coordinates)

3. **RSA-4096 Operations:**
   - Use System.Security.Cryptography.RSA
   - PKCS#1 padding for signatures
   - OAEP-SHA256 for encryption
   - Support PEM import/export

4. **Async Implementation:**
   - Most crypto operations are CPU-bound, use Task.Run for truly long operations (RSA key generation)
   - For fast operations (signing), use Task.CompletedTask pattern or ValueTask

5. **Error Handling:**
   - Catch cryptographic exceptions and convert to CryptoStatus
   - Never expose key material in error messages
   - Validate input parameters (key lengths, null checks)

6. **Security Considerations:**
   - Zeroize temporary key buffers
   - Use SecureRandom for all random number generation
   - Constant-time comparisons where applicable
   - No timing variations based on key data

### Key Algorithm Implementation Points

**ED25519:**
```csharp
// Key Generation
using KeyPair kp = PublicKeyAuth.GenerateKeyPair(seed);
var keySet = new KeySet
{
    PrivateKey = new CryptoKey(WalletNetworks.ED25519, kp.PrivateKey),
    PublicKey = new CryptoKey(WalletNetworks.ED25519, kp.PublicKey)
};

// Signing
byte[] signature = PublicKeyAuth.SignDetached(hash, privateKey);

// Verification
bool valid = PublicKeyAuth.VerifyDetached(signature, hash, publicKey);

// Encryption (via Curve25519 conversion)
byte[] curve25519PublicKey = PublicKeyAuth.ConvertEd25519PublicKeyToCurve25519PublicKey(publicKey);
byte[] ciphertext = SealedPublicKeyBox.Create(data, curve25519PublicKey);
```

**NIST P-256:**
```csharp
// Key Generation
var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
var parameters = ecdh.ExportParameters(true);
// Encode X and Y coordinates with variable-length prefix

// Signing
var ecdsa = ECDsa.Create(new ECParameters
{
    Curve = ECCurve.NamedCurves.nistP256,
    D = privateKey
});
byte[] signature = ecdsa.SignHash(hash);

// ECIES Encryption
// 1. Generate ephemeral key pair
// 2. Perform ECDH with recipient's public key
// 3. Derive encryption key using KDF (SHA-256)
// 4. Encrypt with AES-CBC
// 5. Package: [ephemeral_public_key][IV][ciphertext]
```

**RSA-4096:**
```csharp
// Key Generation
var rsa = RSA.Create(4096);
// Export as DER or PEM

// Signing
byte[] signature = rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

// Encryption
byte[] ciphertext = rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
```

### Constitutional Compliance

- ✅ Uses industry-standard cryptographic libraries
- ✅ Implements proper key management
- ✅ Provides async/await support
- ✅ Complete XML documentation
- ✅ Proper error handling without exposing sensitive data
- ✅ Security-first implementation

## Testing Requirements

### Unit Tests (Core/CryptoModuleTests.cs)

**Key Generation Tests:**
- [ ] Generate ED25519 key pair - verify key sizes
- [ ] Generate NIST P-256 key pair - verify key sizes
- [ ] Generate RSA-4096 key pair - verify key sizes
- [ ] Deterministic generation with seed (ED25519)
- [ ] Non-deterministic generation produces different keys
- [ ] Generated keys can sign and verify

**Signing Tests:**
- [ ] Sign with ED25519 and verify
- [ ] Sign with NIST P-256 and verify
- [ ] Sign with RSA-4096 and verify
- [ ] Verification fails with wrong key
- [ ] Verification fails with tampered data
- [ ] Invalid key length handling

**Encryption Tests:**
- [ ] Encrypt/decrypt round-trip for ED25519
- [ ] Encrypt/decrypt round-trip for NIST P-256
- [ ] Encrypt/decrypt round-trip for RSA-4096
- [ ] Decryption fails with wrong key
- [ ] Decryption fails with tampered ciphertext
- [ ] Maximum data size handling (RSA)

**Public Key Calculation:**
- [ ] Calculate public key from private key (all algorithms)
- [ ] Verify calculated key matches generated key
- [ ] Invalid private key handling

### Test Vectors (TestVectors/CryptoModuleTestVectors.cs)

- [ ] ED25519 test vectors from RFC 8032
- [ ] NIST P-256 test vectors from NIST CAVP
- [ ] RSA test vectors from PKCS#1
- [ ] Cross-implementation compatibility tests

### Security Tests (Security/CryptoModuleSecurityTests.cs)

- [ ] Timing attack resistance (constant-time operations)
- [ ] Key zeroization after use
- [ ] No key material in exception messages
- [ ] Random number quality (NIST randomness tests)

## Acceptance Criteria

- [ ] ICryptoModule interface fully defined with XML docs
- [ ] CryptoModuleBase abstract class implemented
- [ ] CryptoModule concrete implementation complete
- [ ] All three algorithms (ED25519, NIST P-256, RSA-4096) working
- [ ] Key generation async and cancellable
- [ ] Signing/verification working for all algorithms
- [ ] Encryption/decryption working for all algorithms
- [ ] Public key calculation working
- [ ] All unit tests passing
- [ ] Test vectors validated
- [ ] Security tests passing
- [ ] Code coverage >90%
- [ ] No security vulnerabilities
- [ ] Performance meets targets (see spec NFR-2)

## Implementation Notes

(Notes will be added during implementation)

## Review Checklist

- [ ] Code follows constitutional cryptographic standards
- [ ] All operations are secure (no timing leaks, proper randomness)
- [ ] Error handling doesn't expose sensitive data
- [ ] Tests include RFC/NIST test vectors
- [ ] Performance benchmarks meet targets
- [ ] Security audit completed
- [ ] Code review by cryptography expert

---

**Task Control**
- **Created By:** Claude Code
- **Reviewed By:** (Pending)
- **Approved By:** (Pending)
- **Security Review:** (Required before merge)
