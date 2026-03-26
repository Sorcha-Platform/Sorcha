# Task: Implement Enums and Data Models

**ID:** TASK-002
**Status:** Not Started
**Priority:** Critical
**Estimate:** 6 hours
**Assignee:** Claude Code
**Created:** 2025-11-12
**Updated:** 2025-11-12

## Context

Define the foundational enums and data models that will be used throughout the Sorcha.Cryptography library. These types form the contract between components and must be well-designed for extensibility and clarity.

**Related Specifications:**
- [Sorcha.Cryptography Rewrite Spec](../specs/sorcha-cryptography-rewrite.md#functional-requirements)
- [Master Plan](../MASTER-PLAN.md)

**Dependencies:**
- TASK-001 (Project setup must be complete)

## Objective

Implement all enumerations and data model classes with complete XML documentation, ensuring type safety and clear API contracts for the cryptography library.

## Implementation Details

### Changes Required

1. **Enums/WalletNetworks.cs**
   - Define supported cryptographic algorithms
   - Add extension methods for metadata (key sizes, algorithm names)

2. **Enums/HashType.cs**
   - Define supported hash algorithms

3. **Enums/EncryptionType.cs**
   - Define supported symmetric encryption algorithms
   - Extension methods for key and IV sizes

4. **Enums/CompressionType.cs**
   - Define compression levels

5. **Enums/CryptoStatus.cs**
   - Define all possible operation status codes

6. **Models/CryptoKey.cs**
   - Struct for holding key data with network type

7. **Models/KeySet.cs**
   - Struct for public/private key pairs

8. **Models/KeyRing.cs**
   - Class for complete key information with mnemonics

9. **Models/KeyChain.cs**
   - Class for managing multiple key rings

10. **Models/CryptoResult.cs**
    - Generic result type for operations

11. **Models/SymmetricCiphertext.cs**
    - Container for encrypted data with metadata

### Technical Approach

**File: Enums/WalletNetworks.cs**
```csharp
namespace Sorcha.Cryptography.Enums;

/// <summary>
/// Supported cryptographic networks/algorithms for wallet operations.
/// </summary>
public enum WalletNetworks : byte
{
    /// <summary>
    /// Ed25519 elliptic curve signature algorithm.
    /// Public key size: 32 bytes, Private key size: 64 bytes.
    /// </summary>
    ED25519 = 0x00,

    /// <summary>
    /// NIST P-256 elliptic curve (secp256r1).
    /// Public key size: 64 bytes (X,Y coordinates), Private key size: 32 bytes.
    /// </summary>
    NISTP256 = 0x01,

    /// <summary>
    /// RSA 4096-bit key.
    /// Public key size: variable (ASN.1 DER), Private key size: variable (ASN.1 DER).
    /// </summary>
    RSA4096 = 0x02
}
```

**File: Enums/CryptoStatus.cs**
```csharp
namespace Sorcha.Cryptography.Enums;

/// <summary>
/// Status codes for cryptographic operations.
/// </summary>
public enum CryptoStatus
{
    /// <summary>
    /// Operation completed successfully.
    /// </summary>
    Success = 0,

    // Key Management Errors (100-199)
    /// <summary>
    /// Failed to generate key pair.
    /// </summary>
    KeyGenerationFailed = 100,

    /// <summary>
    /// Invalid or corrupted mnemonic phrase.
    /// </summary>
    InvalidMnemonic = 101,

    /// <summary>
    /// Incorrect password provided.
    /// </summary>
    InvalidPassword = 102,

    /// <summary>
    /// Invalid cryptographic key.
    /// </summary>
    InvalidKey = 103,

    /// <summary>
    /// Unknown or missing key ring.
    /// </summary>
    UnknownKeyRing = 104,

    /// <summary>
    /// Key ring already exists.
    /// </summary>
    DuplicateKeyRing = 105,

    /// <summary>
    /// Key chain is empty.
    /// </summary>
    EmptyKeyChain = 106,

    // Cryptographic Operation Errors (200-299)
    /// <summary>
    /// Signature creation failed.
    /// </summary>
    SigningFailed = 200,

    /// <summary>
    /// Signature verification failed.
    /// </summary>
    InvalidSignature = 201,

    /// <summary>
    /// Encryption operation failed.
    /// </summary>
    EncryptionFailed = 202,

    /// <summary>
    /// Decryption operation failed.
    /// </summary>
    DecryptionFailed = 203,

    /// <summary>
    /// Hash computation failed.
    /// </summary>
    HashingFailed = 204,

    // General Errors (900-999)
    /// <summary>
    /// Invalid input parameter provided.
    /// </summary>
    InvalidParameter = 900,

    /// <summary>
    /// Operation was cancelled.
    /// </summary>
    Cancelled = 901,

    /// <summary>
    /// Unexpected error occurred.
    /// </summary>
    UnexpectedError = 999
}
```

**File: Models/CryptoResult.cs**
```csharp
namespace Sorcha.Cryptography.Models;

/// <summary>
/// Represents the result of a cryptographic operation.
/// </summary>
/// <typeparam name="T">The type of value returned on success.</typeparam>
public class CryptoResult<T>
{
    /// <summary>
    /// Gets the status of the operation.
    /// </summary>
    public CryptoStatus Status { get; init; }

    /// <summary>
    /// Gets the result value if the operation succeeded.
    /// </summary>
    public T? Value { get; init; }

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets whether the operation succeeded.
    /// </summary>
    public bool IsSuccess => Status == CryptoStatus.Success;

    /// <summary>
    /// Creates a successful result with a value.
    /// </summary>
    public static CryptoResult<T> Success(T value) => new()
    {
        Status = CryptoStatus.Success,
        Value = value
    };

    /// <summary>
    /// Creates a failed result with status and optional error message.
    /// </summary>
    public static CryptoResult<T> Failure(CryptoStatus status, string? errorMessage = null) => new()
    {
        Status = status,
        ErrorMessage = errorMessage
    };
}
```

**File: Models/CryptoKey.cs**
```csharp
namespace Sorcha.Cryptography.Models;

/// <summary>
/// Represents a cryptographic key with its associated network type.
/// </summary>
public readonly struct CryptoKey
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CryptoKey"/> struct.
    /// </summary>
    /// <param name="network">The wallet network/algorithm type.</param>
    /// <param name="key">The key bytes.</param>
    public CryptoKey(WalletNetworks network, byte[]? key)
    {
        Network = network;
        Key = key;
    }

    /// <summary>
    /// Gets the wallet network/algorithm type.
    /// </summary>
    public WalletNetworks Network { get; init; }

    /// <summary>
    /// Gets the key bytes.
    /// </summary>
    public byte[]? Key { get; init; }

    /// <summary>
    /// Zeroes out the key data for security.
    /// </summary>
    public void Zeroize()
    {
        if (Key != null)
        {
            Array.Clear(Key, 0, Key.Length);
        }
    }
}
```

**File: Models/KeySet.cs**
```csharp
namespace Sorcha.Cryptography.Models;

/// <summary>
/// Represents a public/private key pair.
/// </summary>
public struct KeySet
{
    /// <summary>
    /// Gets or sets the private key.
    /// </summary>
    public CryptoKey PrivateKey { get; set; }

    /// <summary>
    /// Gets or sets the public key.
    /// </summary>
    public CryptoKey PublicKey { get; set; }

    /// <summary>
    /// Zeroes out sensitive key material.
    /// </summary>
    public void Zeroize()
    {
        PrivateKey.Zeroize();
        PublicKey.Zeroize();
    }
}
```

**File: Extensions/EnumExtensions.cs**
```csharp
namespace Sorcha.Cryptography.Extensions;

/// <summary>
/// Extension methods for cryptography enumerations.
/// </summary>
public static class EnumExtensions
{
    /// <summary>
    /// Gets the public key size in bytes for the network type.
    /// </summary>
    public static int GetPublicKeySize(this WalletNetworks network) => network switch
    {
        WalletNetworks.ED25519 => 32,
        WalletNetworks.NISTP256 => 64,
        WalletNetworks.RSA4096 => 550, // Approximate DER-encoded size
        _ => throw new ArgumentException($"Unknown network type: {network}", nameof(network))
    };

    /// <summary>
    /// Gets the private key size in bytes for the network type.
    /// </summary>
    public static int GetPrivateKeySize(this WalletNetworks network) => network switch
    {
        WalletNetworks.ED25519 => 64,
        WalletNetworks.NISTP256 => 32,
        WalletNetworks.RSA4096 => 1193, // Approximate DER-encoded size
        _ => throw new ArgumentException($"Unknown network type: {network}", nameof(network))
    };

    /// <summary>
    /// Gets the symmetric key size in bytes for the encryption type.
    /// </summary>
    public static int GetSymmetricKeySize(this EncryptionType type) => type switch
    {
        EncryptionType.AES_128 => 16,
        EncryptionType.AES_256 => 32,
        EncryptionType.AES_GCM => 32,
        EncryptionType.CHACHA20_POLY1305 => 32,
        EncryptionType.XCHACHA20_POLY1305 => 32,
        _ => throw new ArgumentException($"Unknown encryption type: {type}", nameof(type))
    };

    /// <summary>
    /// Gets the initialization vector size in bytes for the encryption type.
    /// </summary>
    public static int GetIVSize(this EncryptionType type) => type switch
    {
        EncryptionType.AES_128 => 16,
        EncryptionType.AES_256 => 16,
        EncryptionType.AES_GCM => 12,
        EncryptionType.CHACHA20_POLY1305 => 12,
        EncryptionType.XCHACHA20_POLY1305 => 24,
        _ => throw new ArgumentException($"Unknown encryption type: {type}", nameof(type))
    };
}
```

### Constitutional Compliance

- ✅ Follows .NET coding conventions
- ✅ Complete XML documentation on all public members
- ✅ Nullable reference types enabled
- ✅ Immutable types where appropriate (readonly struct, init properties)
- ✅ Clear error codes and status enums

## Testing Requirements

### Unit Tests

Create `Sorcha.Cryptography.Tests/Unit/EnumsAndModelsTests.cs`:

- [ ] Test enum extension methods return correct sizes
- [ ] Test CryptoResult.Success creates correct result
- [ ] Test CryptoResult.Failure creates correct result
- [ ] Test CryptoKey.Zeroize clears key data
- [ ] Test KeySet.Zeroize clears both keys
- [ ] Test SymmetricCiphertext properties
- [ ] Test extension method argument validation

### Edge Case Tests
- [ ] Test extension methods with invalid enum values
- [ ] Test Zeroize on null key data
- [ ] Test CryptoResult with null values

## Acceptance Criteria

- [ ] All enum types defined with complete XML docs
- [ ] All model classes defined with complete XML docs
- [ ] Extension methods implemented for enum metadata
- [ ] CryptoResult generic type implemented
- [ ] Zeroize methods implemented for sensitive data
- [ ] All types compile without warnings
- [ ] Unit tests written and passing
- [ ] Code coverage >95% for models and enums
- [ ] No nullable warnings

## Implementation Notes

(Notes will be added during implementation)

## Review Checklist

- [ ] Code follows constitutional principles
- [ ] All public members have XML documentation
- [ ] Enums have descriptive values and docs
- [ ] Models are immutable where appropriate
- [ ] Extension methods have proper validation
- [ ] Tests cover all scenarios
- [ ] No security issues (key zeroization works)

---

**Task Control**
- **Created By:** Claude Code
- **Reviewed By:** SF
- **Approved By:** SF
