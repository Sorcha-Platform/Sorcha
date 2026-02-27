# Task: Implement Symmetric Cryptography

**ID:** TASK-005
**Status:** Not Started
**Priority:** High
**Estimate:** 8 hours
**Assignee:** Unassigned
**Created:** 2025-11-12
**Updated:** 2025-11-12

## Context

Implement symmetric encryption and decryption supporting multiple algorithms (AES-128/256, AES-GCM, ChaCha20-Poly1305, XChaCha20-Poly1305). This component handles bulk data encryption used by payload management.

**Related Specifications:**
- [Sorcha.Cryptography Rewrite Spec - FR-5](../specs/sorcha-cryptography-rewrite.md#fr-5-symmetric-encryption)
- [Current WalletUtils Encrypt/Decrypt](../../src/Common/Sorcha.Cryptography/WalletUtils.cs)

**Dependencies:**
- TASK-001 (Project setup)
- TASK-002 (Enums and models)

## Objective

Implement ISymmetricCrypto interface with support for five symmetric encryption algorithms, providing secure, authenticated encryption for payload data.

## Implementation Details

### Files to Create

1. **Interfaces/ISymmetricCrypto.cs** - Interface definition
2. **Core/SymmetricCrypto.cs** - Implementation
3. **Models/SymmetricCiphertext.cs** - Expand model from TASK-002
4. **Utilities/RandomnessProvider.cs** - Secure random number generation

### Technical Approach

**Interface: Interfaces/ISymmetricCrypto.cs**
```csharp
namespace Sorcha.Cryptography.Interfaces;

/// <summary>
/// Provides symmetric encryption and decryption operations.
/// </summary>
public interface ISymmetricCrypto
{
    /// <summary>
    /// Encrypts data using symmetric encryption.
    /// </summary>
    /// <param name="plaintext">Data to encrypt.</param>
    /// <param name="encryptionType">Encryption algorithm to use.</param>
    /// <param name="key">Optional key (generated if not provided).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CryptoResult<SymmetricCiphertext>> EncryptAsync(
        byte[] plaintext,
        EncryptionType encryptionType = EncryptionType.XCHACHA20_POLY1305,
        byte[]? key = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts symmetrically encrypted data.
    /// </summary>
    /// <param name="ciphertext">The ciphertext container.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CryptoResult<byte[]>> DecryptAsync(
        SymmetricCiphertext ciphertext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a random key for the specified encryption type.
    /// </summary>
    byte[] GenerateKey(EncryptionType encryptionType);

    /// <summary>
    /// Generates a random IV/nonce for the specified encryption type.
    /// </summary>
    byte[] GenerateIV(EncryptionType encryptionType);
}
```

**Model: Models/SymmetricCiphertext.cs**
```csharp
namespace Sorcha.Cryptography.Models;

/// <summary>
/// Container for symmetrically encrypted data with metadata.
/// </summary>
public sealed class SymmetricCiphertext
{
    /// <summary>
    /// Gets the encrypted data.
    /// </summary>
    public required byte[] Data { get; init; }

    /// <summary>
    /// Gets the encryption key.
    /// </summary>
    public required byte[] Key { get; init; }

    /// <summary>
    /// Gets the initialization vector/nonce.
    /// </summary>
    public required byte[] IV { get; init; }

    /// <summary>
    /// Gets the encryption algorithm used.
    /// </summary>
    public required EncryptionType Type { get; init; }

    /// <summary>
    /// Gets the authentication tag (for AEAD modes).
    /// </summary>
    public byte[]? AuthTag { get; init; }

    /// <summary>
    /// Zeroizes sensitive key material.
    /// </summary>
    public void Zeroize()
    {
        Array.Clear(Key, 0, Key.Length);
        if (AuthTag != null)
            Array.Clear(AuthTag, 0, AuthTag.Length);
    }
}
```

**Algorithm Implementations:**

1. **AES-128-CBC**
   - Key: 16 bytes (128 bits)
   - IV: 16 bytes
   - Mode: CBC with PKCS7 padding
   - Library: System.Security.Cryptography.Aes

2. **AES-256-CBC**
   - Key: 32 bytes (256 bits)
   - IV: 16 bytes
   - Mode: CBC with PKCS7 padding
   - Library: System.Security.Cryptography.Aes

3. **AES-256-GCM** (Authenticated Encryption)
   - Key: 32 bytes (256 bits)
   - Nonce: 12 bytes
   - Auth Tag: 16 bytes
   - Library: Sodium.SecretAeadAes

4. **ChaCha20-Poly1305** (Authenticated Encryption)
   - Key: 32 bytes (256 bits)
   - Nonce: 12 bytes
   - Auth Tag: 16 bytes
   - Library: Sodium.SecretAeadChaCha20Poly1305

5. **XChaCha20-Poly1305** (Authenticated Encryption, Extended Nonce)
   - Key: 32 bytes (256 bits)
   - Nonce: 24 bytes (extended)
   - Auth Tag: 16 bytes
   - Library: Sodium.SecretAeadXChaCha20Poly1305

### Implementation Code Examples

**AES-CBC Encryption:**
```csharp
private async Task<CryptoResult<SymmetricCiphertext>> EncryptAESAsync(
    byte[] plaintext,
    EncryptionType type,
    byte[]? key,
    CancellationToken ct)
{
    int keySize = type == EncryptionType.AES_128 ? 16 : 32;
    key ??= GenerateKey(type);
    byte[] iv = GenerateIV(type);

    using var aes = Aes.Create();
    aes.Key = key;
    aes.IV = iv;
    aes.Mode = CipherMode.CBC;
    aes.Padding = PaddingMode.PKCS7;

    using var encryptor = aes.CreateEncryptor();
    byte[] ciphertext = await Task.Run(
        () => encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length),
        ct);

    return CryptoResult<SymmetricCiphertext>.Success(new SymmetricCiphertext
    {
        Data = ciphertext,
        Key = key,
        IV = iv,
        Type = type
    });
}
```

**ChaCha20-Poly1305 Encryption:**
```csharp
private Task<CryptoResult<SymmetricCiphertext>> EncryptChaCha20Async(
    byte[] plaintext,
    byte[]? key,
    CancellationToken ct)
{
    key ??= GenerateKey(EncryptionType.CHACHA20_POLY1305);
    byte[] nonce = GenerateIV(EncryptionType.CHACHA20_POLY1305); // 12 bytes

    byte[] ciphertext = SecretAeadChaCha20Poly1305.Encrypt(
        plaintext,
        nonce,
        key,
        additionalData: null);

    return Task.FromResult(CryptoResult<SymmetricCiphertext>.Success(
        new SymmetricCiphertext
        {
            Data = ciphertext[..^16], // Ciphertext without tag
            Key = key,
            IV = nonce,
            Type = EncryptionType.CHACHA20_POLY1305,
            AuthTag = ciphertext[^16..] // Last 16 bytes are auth tag
        }));
}
```

**XChaCha20-Poly1305 Encryption (Extended Nonce):**
```csharp
private Task<CryptoResult<SymmetricCiphertext>> EncryptXChaCha20Async(
    byte[] plaintext,
    byte[]? key,
    CancellationToken ct)
{
    key ??= GenerateKey(EncryptionType.XCHACHA20_POLY1305);
    byte[] nonce = GenerateIV(EncryptionType.XCHACHA20_POLY1305); // 24 bytes

    byte[] ciphertext = SecretAeadXChaCha20Poly1305.Encrypt(
        plaintext,
        nonce,
        key,
        additionalData: null);

    return Task.FromResult(CryptoResult<SymmetricCiphertext>.Success(
        new SymmetricCiphertext
        {
            Data = ciphertext[..^16],
            Key = key,
            IV = nonce,
            Type = EncryptionType.XCHACHA20_POLY1305,
            AuthTag = ciphertext[^16..]
        }));
}
```

### Secure Random Number Generation

**Utilities/RandomnessProvider.cs**
```csharp
namespace Sorcha.Cryptography.Utilities;

/// <summary>
/// Provides cryptographically secure random number generation.
/// </summary>
public static class RandomnessProvider
{
    /// <summary>
    /// Generates cryptographically secure random bytes.
    /// </summary>
    /// <param name="length">Number of bytes to generate.</param>
    public static byte[] GetBytes(int length)
    {
        byte[] buffer = new byte[length];
        RandomNumberGenerator.Fill(buffer);
        return buffer;
    }

    /// <summary>
    /// Fills a buffer with cryptographically secure random bytes.
    /// </summary>
    public static void Fill(Span<byte> buffer)
    {
        RandomNumberGenerator.Fill(buffer);
    }
}
```

### Constitutional Compliance

- ✅ Uses authenticated encryption (AEAD) by default
- ✅ Supports multiple algorithms for flexibility
- ✅ Secure random number generation
- ✅ Proper key management (zeroization)
- ✅ Complete XML documentation
- ✅ Async/await support

## Testing Requirements

### Unit Tests (Unit/SymmetricCryptoTests.cs)

**Encryption/Decryption Tests:**
- [ ] Encrypt/decrypt round trip for AES-128-CBC
- [ ] Encrypt/decrypt round trip for AES-256-CBC
- [ ] Encrypt/decrypt round trip for AES-256-GCM
- [ ] Encrypt/decrypt round trip for ChaCha20-Poly1305
- [ ] Encrypt/decrypt round trip for XChaCha20-Poly1305
- [ ] Decryption fails with wrong key
- [ ] Decryption fails with tampered ciphertext
- [ ] Decryption fails with tampered auth tag (AEAD modes)

**Key Generation Tests:**
- [ ] GenerateKey produces correct key size for each algorithm
- [ ] GenerateIV produces correct IV size for each algorithm
- [ ] Generated keys are cryptographically random
- [ ] Key generation is non-deterministic

**Edge Cases:**
- [ ] Empty plaintext encryption/decryption
- [ ] Large plaintext (> 1 GB) handling
- [ ] Null parameter validation
- [ ] Invalid encryption type handling

**AEAD Authentication Tests:**
- [ ] Authentication tag validation (GCM)
- [ ] Authentication tag validation (ChaCha20-Poly1305)
- [ ] Authentication tag validation (XChaCha20-Poly1305)
- [ ] Tampered data detected

### Test Vectors (TestVectors/SymmetricCryptoTestVectors.cs)

- [ ] AES-128-CBC NIST test vectors
- [ ] AES-256-CBC NIST test vectors
- [ ] AES-256-GCM NIST test vectors
- [ ] ChaCha20-Poly1305 RFC 8439 test vectors
- [ ] XChaCha20-Poly1305 test vectors

### Performance Tests (Performance/SymmetricCryptoBenchmarks.cs)

**Benchmarks (BenchmarkDotNet):**
- [ ] AES-128-CBC encryption throughput
- [ ] AES-256-CBC encryption throughput
- [ ] AES-256-GCM encryption throughput
- [ ] ChaCha20-Poly1305 encryption throughput (target: >200 MB/s)
- [ ] XChaCha20-Poly1305 encryption throughput
- [ ] Small data (< 1 KB) performance
- [ ] Large data (> 100 MB) performance

## Acceptance Criteria

- [ ] ISymmetricCrypto interface fully defined with XML docs
- [ ] SymmetricCrypto implementation complete
- [ ] All five algorithms implemented correctly
- [ ] Authenticated encryption (AEAD) working
- [ ] Key and IV generation working
- [ ] Secure random number generation verified
- [ ] All unit tests passing (>90% coverage)
- [ ] Test vectors validated
- [ ] Performance benchmarks meet targets (>200 MB/s for ChaCha20)
- [ ] Authentication tag validation working
- [ ] SymmetricCiphertext.Zeroize() working

## Implementation Notes

**Security Considerations:**
1. Always prefer AEAD modes (GCM, Poly1305)
2. Never reuse IV/nonce with same key
3. Use cryptographically secure RNG
4. Zeroize keys after use
5. Validate authentication tags in constant time

**Performance Considerations:**
- ChaCha20-Poly1305 is typically faster than AES on platforms without hardware AES
- AES-GCM benefits from hardware acceleration (AES-NI)
- XChaCha20 has larger nonce (better for random nonces, no collision risk)

**Default Algorithm Selection:**
- Default: XChaCha20-Poly1305
- Reason: Authenticated, fast, large nonce space, no hardware requirement

## Review Checklist

- [ ] Code follows constitutional principles
- [ ] All algorithms use secure implementations
- [ ] AEAD modes properly validate authentication tags
- [ ] Key generation uses secure RNG
- [ ] No IV/nonce reuse vulnerabilities
- [ ] Test vectors from standards passing
- [ ] Performance targets met
- [ ] Security audit completed

---

**Task Control**
- **Created By:** Claude Code
- **Reviewed By:** (Pending)
- **Approved By:** (Pending)
- **Security Review:** (Required before merge)
