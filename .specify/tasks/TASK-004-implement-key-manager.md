# Task: Implement Key Manager with Mnemonics

**ID:** TASK-004
**Status:** Not Started
**Priority:** Critical
**Estimate:** 12 hours
**Assignee:** Unassigned
**Created:** 2025-11-12
**Updated:** 2025-11-12

## Context

Implement the Key Manager component responsible for generating, recovering, and managing cryptographic keys with BIP39-compatible mnemonic recovery phrases. This component provides the user-facing key management functionality.

**Related Specifications:**
- [Sorcha.Cryptography Rewrite Spec - FR-2](../specs/sorcha-cryptography-rewrite.md#fr-2-mnemonic-recovery-phrase)
- [Current KeyManager Implementation](../../src/Common/Sorcha.Cryptography/KeyManager.cs)

**Dependencies:**
- TASK-001 (Project setup)
- TASK-002 (Enums and models)
- TASK-003 (Core crypto module)

## Objective

Implement IKeyManager interface and KeyManager class with full support for BIP39 mnemonic generation/recovery, password-protected key storage, KeyRing management, and KeyChain encryption.

## Implementation Details

### Files to Create

1. **Interfaces/IKeyManager.cs** - Interface definition
2. **Core/KeyManager.cs** - Key manager implementation
3. **Models/KeyRing.cs** - Complete KeyRing model (expand from TASK-002)
4. **Models/KeyChain.cs** - Complete KeyChain model (expand from TASK-002)
5. **Utilities/MnemonicUtilities.cs** - BIP39 word list and utilities

### Technical Approach

**Interface: Interfaces/IKeyManager.cs**
```csharp
namespace Sorcha.Cryptography.Interfaces;

/// <summary>
/// Provides key management operations including mnemonic-based recovery.
/// </summary>
public interface IKeyManager
{
    /// <summary>
    /// Creates a master key ring with mnemonic recovery phrase.
    /// </summary>
    /// <param name="network">The wallet network/algorithm type.</param>
    /// <param name="password">Optional password for key protection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CryptoResult<KeyRing>> CreateMasterKeyRingAsync(
        WalletNetworks network,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers a key ring from a mnemonic recovery phrase.
    /// </summary>
    /// <param name="mnemonic">The 12-word mnemonic phrase.</param>
    /// <param name="password">Optional password used during creation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CryptoResult<KeyRing>> RecoverMasterKeyRingAsync(
        string mnemonic,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a mnemonic phrase checksum.
    /// </summary>
    /// <param name="mnemonic">The mnemonic phrase to validate.</param>
    bool ValidateMnemonic(string mnemonic);

    /// <summary>
    /// Generates a seed from mnemonic and optional password.
    /// </summary>
    /// <param name="mnemonic">The mnemonic phrase.</param>
    /// <param name="password">Optional password for additional entropy.</param>
    /// <param name="outputSize">Desired seed size in bytes.</param>
    Task<byte[]?> GenerateSeedAsync(
        string mnemonic,
        string? password = null,
        uint outputSize = 32);
}
```

**Core Implementation Features:**

1. **Mnemonic Generation:**
   - Generate 12-word phrases from BIP39 word list (2048 words)
   - Include 4-bit checksum for validation
   - Entropy source: cryptographically secure random
   - Word list stored as embedded resource

2. **Mnemonic Recovery:**
   - Parse 12-word phrase
   - Validate checksum
   - Derive seed using Argon2id password hashing
   - Reconstruct key pair from seed

3. **Seed Derivation:**
   - Use Argon2id13 with configurable parameters:
     - Iterations: 4 (balanced security/performance)
     - Memory: 256 MB
     - Parallelism: 1
   - Combine mnemonic + password + salt
   - Generate deterministic seed

4. **KeyRing Management:**
   ```csharp
   public sealed class KeyRing : IDisposable
   {
       public KeySet KeySet { get; private set; }
       public string Mnemonic { get; private set; }
       public string WalletAddress { get; private set; }
       public string WIFKey { get; private set; }
       public WalletNetworks Network { get; private set; }

       public string[] GetMnemonicWords() => Mnemonic.Split(' ');

       public void Dispose()
       {
           KeySet.Zeroize();
           // Clear mnemonic from memory
       }
   }
   ```

5. **KeyChain Management:**
   ```csharp
   public sealed class KeyChain
   {
       private Dictionary<string, KeyRing> _keyRings;

       public CryptoStatus AddKeyRing(string name, KeyRing keyRing);
       public CryptoResult<KeyRing> GetKeyRing(string name);
       public CryptoStatus RemoveKeyRing(string name);
       public int Count => _keyRings.Count;
       public IEnumerable<string> KeyRingNames => _keyRings.Keys;

       // Export with encryption (XChaCha20-Poly1305)
       public async Task<CryptoResult<byte[]>> ExportAsync(
           string password,
           CancellationToken cancellationToken = default);

       // Import with decryption
       public async Task<CryptoStatus> ImportAsync(
           byte[] encryptedData,
           string password,
           CancellationToken cancellationToken = default);
   }
   ```

6. **BIP39 Word List:**
   - Embed 2048-word English word list as resource
   - Implement word-to-index and index-to-word lookup
   - Support entropy-to-mnemonic conversion
   - Support mnemonic-to-entropy conversion

### Algorithm Implementation Points

**Mnemonic Generation (12 words = 128 bits + 4-bit checksum):**
```csharp
// 1. Generate 128 bits (16 bytes) of entropy
byte[] entropy = new byte[16];
RandomNumberGenerator.Fill(entropy);

// 2. Calculate SHA256 checksum
byte[] hash = SHA256.HashData(entropy);

// 3. Take first 4 bits of hash as checksum
byte checksum = (byte)(hash[0] >> 4);

// 4. Combine entropy (128 bits) + checksum (4 bits) = 132 bits
// 5. Split into 12 groups of 11 bits each
// 6. Map each 11-bit value to BIP39 word
```

**Seed Derivation with Argon2id:**
```csharp
var argon2 = new Argon2id(Encoding.UTF8.GetBytes(mnemonic))
{
    Salt = Encoding.UTF8.GetBytes("sorcha-wallet-seed" + password),
    DegreeOfParallelism = 1,
    MemorySize = 262144, // 256 MB
    Iterations = 4
};

byte[] seed = argon2.GetBytes((int)outputSize);
```

**Key Recovery Flow:**
```
Mnemonic (12 words)
    ↓
Validate Checksum
    ↓
Convert to Entropy (128 bits)
    ↓
Argon2id(entropy + password)
    ↓
Seed (32 bytes)
    ↓
CryptoModule.RecoverKeySet(network, seed, password)
    ↓
Generate WIF and Wallet Address
    ↓
Return KeyRing
```

**KeyChain Export Format:**
```
Structure:
- Magic Bytes: "SCKC" (4 bytes)
- Version: 1 (1 byte)
- Salt: 16 bytes (for Argon2id)
- IV: 24 bytes (XChaCha20)
- Encrypted Data:
    - KeyRing Count (4 bytes)
    - For each KeyRing:
        - Name Length (VL encoded)
        - Name (UTF-8)
        - Network Type (1 byte)
        - Mnemonic (encrypted)
        - Private Key (encrypted)
        - Public Key
        - Wallet Address
- Authentication Tag: 16 bytes (Poly1305)
```

### Constitutional Compliance

- ✅ Uses BIP39 standard for interoperability
- ✅ Strong password hashing (Argon2id)
- ✅ Authenticated encryption (XChaCha20-Poly1305)
- ✅ Proper key zeroization
- ✅ Complete XML documentation
- ✅ Async/await support

## Testing Requirements

### Unit Tests (Unit/KeyManagerTests.cs)

**Mnemonic Generation Tests:**
- [ ] Generate mnemonic produces 12 words
- [ ] All words are from BIP39 word list
- [ ] Checksum validation passes
- [ ] Multiple generations produce different mnemonics
- [ ] Entropy length validation

**Mnemonic Recovery Tests:**
- [ ] Recover key from mnemonic produces same key
- [ ] Recovery with password produces different key than without
- [ ] Invalid mnemonic detected (bad checksum)
- [ ] Invalid word in mnemonic detected
- [ ] Wrong number of words detected

**KeyRing Tests:**
- [ ] Create key ring for ED25519
- [ ] Create key ring for NIST P-256
- [ ] Create key ring for RSA-4096
- [ ] Key ring contains valid wallet address
- [ ] Key ring contains valid WIF key
- [ ] Dispose properly zeroizes keys

**KeyChain Tests:**
- [ ] Add key ring to chain
- [ ] Retrieve key ring by name
- [ ] Remove key ring from chain
- [ ] Duplicate name detection
- [ ] Export/import round trip
- [ ] Export with password protection
- [ ] Import with wrong password fails

**Seed Derivation Tests:**
- [ ] Same mnemonic produces same seed
- [ ] Different passwords produce different seeds
- [ ] Argon2id parameters are correct
- [ ] Seed length matches requested size

### Test Vectors (TestVectors/MnemonicTestVectors.cs)

- [ ] BIP39 test vectors from official specification
- [ ] Known mnemonic-to-seed conversions
- [ ] Cross-implementation compatibility (with other BIP39 libraries)

### Integration Tests (Integration/KeyManagerIntegrationTests.cs)

- [ ] Create key ring, derive wallet, recover from mnemonic
- [ ] Create keychain, add multiple rings, export, import
- [ ] Integration with CryptoModule for key operations

## Acceptance Criteria

- [ ] IKeyManager interface fully defined with XML docs
- [ ] KeyManager implementation complete
- [ ] BIP39 mnemonic generation working
- [ ] Mnemonic recovery working (12 words)
- [ ] Checksum validation working
- [ ] Argon2id seed derivation working
- [ ] KeyRing model complete with all properties
- [ ] KeyChain encryption/decryption working
- [ ] Export/import with password protection working
- [ ] All unit tests passing (>90% coverage)
- [ ] BIP39 test vectors validated
- [ ] Integration tests passing
- [ ] Performance: Key ring creation < 500ms (including Argon2id)
- [ ] Security: Proper key zeroization verified

## Implementation Notes

**Important Security Considerations:**
1. Mnemonics should be stored securely (never logged or cached)
2. Argon2id parameters balance security and UX
3. KeyChain export must use authenticated encryption
4. Key material must be zeroized on Dispose()
5. Password validation should be constant-time

**Performance Notes:**
- Argon2id is intentionally slow (defense against brute force)
- Consider caching KeyChain in-memory for performance
- Export/import are infrequent operations, security > speed

## Review Checklist

- [ ] Code follows constitutional principles
- [ ] BIP39 compatibility verified
- [ ] Argon2id parameters are secure
- [ ] Encryption uses authenticated modes
- [ ] Key zeroization is reliable
- [ ] No sensitive data in logs/exceptions
- [ ] Test vectors from BIP39 spec passing
- [ ] Performance targets met

---

**Task Control**
- **Created By:** Claude Code
- **Reviewed By:** (Pending)
- **Approved By:** (Pending)
- **Security Review:** (Required before merge)
