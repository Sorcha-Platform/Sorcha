# Task: Implement Hash Provider

**ID:** TASK-006
**Status:** Not Started
**Priority:** High
**Estimate:** 6 hours
**Assignee:** Unassigned
**Created:** 2025-11-12
**Updated:** 2025-11-12

## Context

Implement hash provider supporting SHA-2 family (SHA-256, SHA-384, SHA-512) and Blake2b variants. This component provides cryptographic hashing for data integrity, signatures, and key derivation.

**Related Specifications:**
- [Sorcha.Cryptography Rewrite Spec - FR-6](../specs/sorcha-cryptography-rewrite.md#fr-6-hash-functions)
- [Current WalletUtils Hashing](../../src/Common/Sorcha.Cryptography/WalletUtils.cs)

**Dependencies:**
- TASK-001 (Project setup)
- TASK-002 (Enums and models)

## Objective

Implement IHashProvider interface with support for five hash algorithms, including streaming support for large files and HMAC variants.

## Implementation Details

### Files to Create

1. **Interfaces/IHashProvider.cs** - Interface definition
2. **Core/HashProvider.cs** - Implementation

### Technical Approach

**Interface: Interfaces/IHashProvider.cs**
```csharp
namespace Sorcha.Cryptography.Interfaces;

/// <summary>
/// Provides cryptographic hash functions.
/// </summary>
public interface IHashProvider
{
    /// <summary>
    /// Computes hash of data using specified algorithm.
    /// </summary>
    /// <param name="data">Data to hash.</param>
    /// <param name="hashType">Hash algorithm to use.</param>
    byte[] ComputeHash(byte[] data, HashType hashType = HashType.SHA256);

    /// <summary>
    /// Computes hash of data stream asynchronously.
    /// Useful for hashing large files without loading into memory.
    /// </summary>
    /// <param name="dataStream">Stream containing data to hash.</param>
    /// <param name="hashType">Hash algorithm to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<byte[]> ComputeHashAsync(
        Stream dataStream,
        HashType hashType = HashType.SHA256,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes HMAC of data using specified hash algorithm.
    /// </summary>
    /// <param name="data">Data to authenticate.</param>
    /// <param name="key">Secret key for HMAC.</param>
    /// <param name="hashType">Hash algorithm to use.</param>
    byte[] ComputeHMAC(
        byte[] data,
        byte[] key,
        HashType hashType = HashType.SHA256);

    /// <summary>
    /// Computes double hash (hash of hash) - commonly used in blockchain.
    /// </summary>
    /// <param name="data">Data to hash.</param>
    /// <param name="hashType">Hash algorithm to use.</param>
    byte[] ComputeDoubleHash(
        byte[] data,
        HashType hashType = HashType.SHA256);

    /// <summary>
    /// Gets the hash length in bytes for the specified hash type.
    /// </summary>
    int GetHashLength(HashType hashType);
}
```

**Implementation: Core/HashProvider.cs**

```csharp
namespace Sorcha.Cryptography.Core;

/// <summary>
/// Provides cryptographic hash functions.
/// </summary>
public sealed class HashProvider : IHashProvider
{
    public byte[] ComputeHash(byte[] data, HashType hashType = HashType.SHA256)
    {
        ArgumentNullException.ThrowIfNull(data);

        return hashType switch
        {
            HashType.SHA256 => SHA256.HashData(data),
            HashType.SHA384 => SHA384.HashData(data),
            HashType.SHA512 => SHA512.HashData(data),
            HashType.Blake2b_256 => GenericHash.Hash(data, null, 32),
            HashType.Blake2b_512 => GenericHash.Hash(data, null, 64),
            _ => throw new ArgumentException($"Unsupported hash type: {hashType}")
        };
    }

    public async Task<byte[]> ComputeHashAsync(
        Stream dataStream,
        HashType hashType = HashType.SHA256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataStream);

        return hashType switch
        {
            HashType.SHA256 => await ComputeSHA256StreamAsync(dataStream, cancellationToken),
            HashType.SHA384 => await ComputeSHA384StreamAsync(dataStream, cancellationToken),
            HashType.SHA512 => await ComputeSHA512StreamAsync(dataStream, cancellationToken),
            HashType.Blake2b_256 => await ComputeBlake2bStreamAsync(dataStream, 32, cancellationToken),
            HashType.Blake2b_512 => await ComputeBlake2bStreamAsync(dataStream, 64, cancellationToken),
            _ => throw new ArgumentException($"Unsupported hash type: {hashType}")
        };
    }

    public byte[] ComputeHMAC(byte[] data, byte[] key, HashType hashType = HashType.SHA256)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(key);

        return hashType switch
        {
            HashType.SHA256 => HMACSHA256.HashData(key, data),
            HashType.SHA384 => HMACSHA384.HashData(key, data),
            HashType.SHA512 => HMACSHA512.HashData(key, data),
            HashType.Blake2b_256 => GenericHash.Hash(data, key, 32),
            HashType.Blake2b_512 => GenericHash.Hash(data, key, 64),
            _ => throw new ArgumentException($"Unsupported hash type: {hashType}")
        };
    }

    public byte[] ComputeDoubleHash(byte[] data, HashType hashType = HashType.SHA256)
    {
        byte[] firstHash = ComputeHash(data, hashType);
        return ComputeHash(firstHash, hashType);
    }

    public int GetHashLength(HashType hashType) => hashType switch
    {
        HashType.SHA256 => 32,
        HashType.SHA384 => 48,
        HashType.SHA512 => 64,
        HashType.Blake2b_256 => 32,
        HashType.Blake2b_512 => 64,
        _ => throw new ArgumentException($"Unsupported hash type: {hashType}")
    };

    private static async Task<byte[]> ComputeSHA256StreamAsync(
        Stream stream,
        CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        return await sha256.ComputeHashAsync(stream, ct);
    }

    private static async Task<byte[]> ComputeBlake2bStreamAsync(
        Stream stream,
        int outputLength,
        CancellationToken ct)
    {
        const int bufferSize = 81920; // 80 KB buffer
        byte[] buffer = new byte[bufferSize];
        var state = GenericHash.Init(null, outputLength);

        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, 0, bufferSize, ct)) > 0)
        {
            GenericHash.Update(state, buffer, bytesRead);
        }

        return GenericHash.Final(state, outputLength);
    }
}
```

### Algorithm Details

**Supported Hash Functions:**

1. **SHA-256**
   - Output: 32 bytes (256 bits)
   - Library: System.Security.Cryptography.SHA256
   - Use case: General purpose, signatures
   - Speed: ~400 MB/s (typical)

2. **SHA-384**
   - Output: 48 bytes (384 bits)
   - Library: System.Security.Cryptography.SHA384
   - Use case: Higher security requirements
   - Speed: ~500 MB/s (typical)

3. **SHA-512**
   - Output: 64 bytes (512 bits)
   - Library: System.Security.Cryptography.SHA512
   - Use case: Maximum security, faster on 64-bit
   - Speed: ~600 MB/s (typical)

4. **Blake2b-256**
   - Output: 32 bytes (256 bits)
   - Library: Sodium.GenericHash
   - Use case: Fast hashing, modern alternative to SHA-256
   - Speed: ~1000 MB/s (typical)

5. **Blake2b-512**
   - Output: 64 bytes (512 bits)
   - Library: Sodium.GenericHash
   - Use case: Fast hashing with large output
   - Speed: ~1000 MB/s (typical)

**Double Hashing (Bitcoin-style):**
```csharp
// Used in transaction signing to prevent length extension attacks
hash1 = SHA256(data)
hash2 = SHA256(hash1)  // Double hash
```

**HMAC (Keyed Hashing):**
- Used for message authentication
- Prevents hash collision attacks
- Key can be any length, internally processed

### Extension Methods for Convenience

**Extensions/ByteArrayExtensions.cs**
```csharp
namespace Sorcha.Cryptography.Extensions;

/// <summary>
/// Extension methods for byte array operations.
/// </summary>
public static class ByteArrayExtensions
{
    /// <summary>
    /// Computes SHA-256 hash of the byte array.
    /// </summary>
    public static byte[] ToSHA256(this byte[] data)
        => SHA256.HashData(data);

    /// <summary>
    /// Computes double SHA-256 hash.
    /// </summary>
    public static byte[] ToDoubleSHA256(this byte[] data)
    {
        var hash1 = SHA256.HashData(data);
        return SHA256.HashData(hash1);
    }

    /// <summary>
    /// Converts byte array to hexadecimal string.
    /// </summary>
    public static string ToHex(this byte[] data)
        => Convert.ToHexString(data).ToLowerInvariant();

    /// <summary>
    /// Constant-time comparison of byte arrays.
    /// </summary>
    public static bool ConstantTimeEquals(this byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
            return false;

        uint diff = 0;
        for (int i = 0; i < a.Length; i++)
            diff |= (uint)(a[i] ^ b[i]);

        return diff == 0;
    }
}
```

### Constitutional Compliance

- ✅ Uses standard cryptographic hash functions
- ✅ Streaming support for large files
- ✅ HMAC support for authentication
- ✅ Complete XML documentation
- ✅ Async/await for I/O operations
- ✅ Constant-time comparison utility

## Testing Requirements

### Unit Tests (Unit/HashProviderTests.cs)

**Basic Hashing Tests:**
- [ ] ComputeHash for SHA-256
- [ ] ComputeHash for SHA-384
- [ ] ComputeHash for SHA-512
- [ ] ComputeHash for Blake2b-256
- [ ] ComputeHash for Blake2b-512
- [ ] Same input produces same output (determinism)
- [ ] Different inputs produce different outputs
- [ ] Empty input handling
- [ ] Null input validation

**Streaming Tests:**
- [ ] ComputeHashAsync for small stream (< 1 MB)
- [ ] ComputeHashAsync for large stream (> 100 MB)
- [ ] Stream and direct hash produce same result
- [ ] Cancellation token handling
- [ ] Stream position preservation/reset

**HMAC Tests:**
- [ ] ComputeHMAC for all hash types
- [ ] Different keys produce different HMACs
- [ ] Same key produces same HMAC (determinism)
- [ ] Key validation
- [ ] HMAC length matches hash length

**Double Hash Tests:**
- [ ] ComputeDoubleHash produces correct result
- [ ] Double hash != single hash
- [ ] Double hash is deterministic

**Utility Tests:**
- [ ] GetHashLength returns correct lengths
- [ ] ByteArrayExtensions.ConstantTimeEquals works correctly
- [ ] ToHex conversion
- [ ] ToSHA256 extension method

### Test Vectors (TestVectors/HashTestVectors.cs)

**Standard Test Vectors:**
- [ ] SHA-256 NIST test vectors
- [ ] SHA-384 NIST test vectors
- [ ] SHA-512 NIST test vectors
- [ ] Blake2b test vectors from RFC 7693
- [ ] HMAC-SHA256 RFC 4231 test vectors
- [ ] Bitcoin double SHA-256 test vectors

**Edge Case Vectors:**
- [ ] Empty input
- [ ] Single byte input
- [ ] Maximum length input
- [ ] Unicode/UTF-8 text

### Performance Tests (Performance/HashBenchmarks.cs)

**Benchmarks (BenchmarkDotNet):**
- [ ] SHA-256 throughput (target: >100 MB/s)
- [ ] SHA-384 throughput
- [ ] SHA-512 throughput
- [ ] Blake2b-256 throughput (target: >500 MB/s)
- [ ] Blake2b-512 throughput
- [ ] Small data (< 1 KB) hashing
- [ ] Large data (> 100 MB) hashing
- [ ] HMAC performance
- [ ] Stream vs direct hashing comparison

## Acceptance Criteria

- [ ] IHashProvider interface fully defined with XML docs
- [ ] HashProvider implementation complete
- [ ] All five hash algorithms working
- [ ] Streaming support working for large files
- [ ] HMAC support working
- [ ] Double hash utility working
- [ ] Constant-time comparison utility working
- [ ] All unit tests passing (>95% coverage)
- [ ] Test vectors validated
- [ ] Performance benchmarks meet targets
- [ ] Async cancellation working

## Implementation Notes

**Security Considerations:**
1. Use constant-time comparison for hash validation
2. HMAC provides authentication (better than simple hash)
3. Double hashing prevents length extension attacks
4. Always validate hash lengths

**Performance Considerations:**
- Blake2b is typically 2-3x faster than SHA-256
- SHA-512 can be faster than SHA-256 on 64-bit systems
- Streaming is essential for large files (memory efficiency)
- Hardware acceleration available for SHA-256 on modern CPUs

**Use Cases:**
- **SHA-256**: Transaction hashing, signatures (Bitcoin-compatible)
- **Blake2b**: Fast data integrity checks, non-Bitcoin use cases
- **Double SHA-256**: Transaction signing (prevents length extension)
- **HMAC**: Message authentication, API authentication

## Review Checklist

- [ ] Code follows constitutional principles
- [ ] All algorithms use standard implementations
- [ ] Constant-time comparison implemented
- [ ] Streaming properly handles large files
- [ ] Test vectors from standards passing
- [ ] Performance targets met
- [ ] No timing attack vulnerabilities

---

**Task Control**
- **Created By:** Claude Code
- **Reviewed By:** (Pending)
- **Approved By:** (Pending)
