# NBitcoin Patterns Reference

## Contents
- Value Object Pattern
- Key Derivation Pattern
- BIP44 Path Parsing
- Security Patterns
- Anti-Patterns

---

## Value Object Pattern

Sorcha wraps NBitcoin types in domain value objects to enforce invariants and prevent API misuse.

### Mnemonic Value Object

```csharp
// src/Core/Sorcha.Wallet.Core/Domain/ValueObjects/Mnemonic.cs
public record Mnemonic
{
    private readonly NBitcoin.Mnemonic _mnemonic;

    public Mnemonic(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            throw new ArgumentException("Mnemonic phrase cannot be empty", nameof(phrase));

        try
        {
            _mnemonic = new NBitcoin.Mnemonic(phrase);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid mnemonic phrase", nameof(phrase), ex);
        }
    }

    public static Mnemonic Generate(int wordCount = 12)
    {
        var entropy = wordCount == 24 ? NBitcoin.WordCount.TwentyFour : NBitcoin.WordCount.Twelve;
        return new Mnemonic(new NBitcoin.Mnemonic(Wordlist.English, entropy));
    }

    public byte[] DeriveSeed(string? passphrase = null)
    {
        var extKey = _mnemonic.DeriveExtKey(passphrase);
        return extKey.PrivateKey.ToBytes();
    }

    // SECURITY: Never expose phrase in ToString()
    public override string ToString() => $"Mnemonic({WordCount} words)";
}
```

### DerivationPath Value Object

```csharp
// src/Core/Sorcha.Wallet.Core/Domain/ValueObjects/DerivationPath.cs
public record DerivationPath
{
    private readonly KeyPath _keyPath;

    public static DerivationPath CreateBip44(uint coinType = 0, uint account = 0, uint change = 0, uint addressIndex = 0)
    {
        // BIP44: m / purpose' / coin_type' / account' / change / address_index
        var path = $"m/44'/{coinType}'/{account}'/{change}/{addressIndex}";
        return new DerivationPath(path);
    }

    internal KeyPath KeyPath => _keyPath;  // Internal access only
}
```

---

## Key Derivation Pattern

### Master Key from Mnemonic

```csharp
// src/Core/Sorcha.Wallet.Core/Services/Implementation/KeyManagementService.cs:43-59
public Task<byte[]> DeriveMasterKeyAsync(Mnemonic mnemonic, string? passphrase = null)
{
    var seed = mnemonic.DeriveSeed(passphrase);  // Uses NBitcoin.Mnemonic.DeriveExtKey()
    return Task.FromResult(seed);
}
```

### Child Key at BIP44 Path

```csharp
// src/Core/Sorcha.Wallet.Core/Services/Implementation/KeyManagementService.cs:74-80
var extKey = ExtKey.CreateFromSeed(masterKey);       // NBitcoin: seed → ExtKey
var derived = extKey.Derive(derivationPath.KeyPath); // NBitcoin: BIP32 derivation
var privateKeyBytes = derived.PrivateKey.ToBytes();  // NBitcoin: extract raw bytes

// Then use Sorcha.Cryptography for algorithm-specific key generation
var keySetResult = await _cryptoModule.GenerateKeySetAsync(network, privateKeyBytes);
```

---

## BIP44 Path Parsing

### Extract Path Components

```csharp
// src/Core/Sorcha.Wallet.Core/Domain/ValueObjects/DerivationPath.cs:73-101
public static bool TryParseBip44(string path, out uint coinType, out uint account, out uint change, out uint addressIndex)
{
    coinType = account = change = addressIndex = 0;

    try
    {
        var keyPath = new KeyPath(path);
        var indices = keyPath.Indexes;

        if (indices.Length != 5) return false;

        const uint hardenedBit = 0x80000000;
        if (indices[0] != 44 + hardenedBit) return false;  // Verify purpose = 44'

        coinType = indices[1] & ~hardenedBit;
        account = indices[2] & ~hardenedBit;
        change = indices[3];  // NOT hardened
        addressIndex = indices[4];  // NOT hardened

        return true;
    }
    catch { return false; }
}
```

### Validate Change Value

```csharp
// BIP44: change must be 0 (receive) or 1 (change)
if (!DerivationPath.TryParseBip44(derivationPath, out _, out _, out uint change, out _))
    throw new ArgumentException($"Invalid BIP44 path: {derivationPath}");

if (change > 1)
    throw new ArgumentException($"Invalid change value: {change}. Must be 0 or 1.");
```

---

## Security Patterns

### Never Log Mnemonics

```csharp
// GOOD: ToString() is safe for logging
_logger.LogDebug("Created wallet with {Mnemonic}", mnemonic.ToString());
// Output: "Created wallet with Mnemonic(12 words)"

// BAD: NEVER log the phrase
_logger.LogDebug("Mnemonic: {Phrase}", mnemonic.Phrase);  // SECURITY VIOLATION
```

### Encrypt Private Keys Before Storage

```csharp
// src/Core/Sorcha.Wallet.Core/Services/Implementation/KeyManagementService.cs:142-165
var (encryptedKey, keyId) = await _keyManagement.EncryptPrivateKeyAsync(privateKey, string.Empty);
// Store encryptedKey and keyId in database - NEVER store raw privateKey
```

### Client-Side Derivation

The server NEVER receives or stores mnemonics. Clients derive keys locally and register only public keys:

```csharp
// Server-side: Only accepts derived public key
public async Task<WalletAddress> RegisterDerivedAddressAsync(
    string walletAddress,
    string derivedPublicKey,    // Base64-encoded public key
    string derivedAddress,
    string derivationPath,
    ...)
```

---

## Anti-Patterns

### WARNING: Exposing Mnemonic in Logs or Exceptions

**The Problem:**

```csharp
// BAD - Mnemonic phrase exposed in logs
catch (Exception ex)
{
    _logger.LogError("Failed to process mnemonic: {Phrase}", mnemonic.Phrase);
    throw new Exception($"Mnemonic {mnemonic.Phrase} is invalid");
}
```

**Why This Breaks:**
1. Mnemonic phrases grant full wallet access
2. Logs are often stored unencrypted
3. Exception messages may appear in error responses

**The Fix:**

```csharp
// GOOD - Safe logging with redacted output
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to process {Mnemonic}", mnemonic.ToString());
    throw new ArgumentException("Invalid mnemonic phrase", nameof(phrase), ex);
}
```

### WARNING: Storing Mnemonics on Server

**The Problem:**

```csharp
// BAD - Server stores mnemonic
wallet.Mnemonic = mnemonic.Phrase;  // NEVER DO THIS
await _repository.SaveAsync(wallet);
```

**Why This Breaks:**
1. Database breaches expose all wallets
2. Violates key custody best practices
3. Creates single point of failure

**The Fix:**

```csharp
// GOOD - Derive keys, encrypt, store only encrypted private key
var (encryptedKey, keyId) = await _keyManagement.EncryptPrivateKeyAsync(privateKey, string.Empty);
wallet.EncryptedPrivateKey = encryptedKey;
wallet.EncryptionKeyId = keyId;
// Client retains mnemonic - their responsibility to backup
```

### WARNING: Using NBitcoin for Transaction Signing

**The Problem:**

```csharp
// BAD - NBitcoin transaction builder for non-Bitcoin signatures
var tx = Transaction.Create(Network.Main);
tx.Sign(privateKey, ...);  // Wrong! Sorcha uses ED25519/P-256/RSA
```

**Why This Breaks:**
1. NBitcoin signing is Bitcoin-specific (secp256k1)
2. Sorcha uses ED25519, NIST P-256, and RSA-4096
3. Incompatible signature formats

**The Fix:**

```csharp
// GOOD - Use Sorcha.Cryptography for signing
var signResult = await _cryptoModule.SignAsync(hash, (byte)network, privateKey);
```

---

## NBitcoin API Summary

| Method | Purpose | Location |
|--------|---------|----------|
| `new NBitcoin.Mnemonic(phrase)` | Validate/parse mnemonic | `Mnemonic.cs:24` |
| `new NBitcoin.Mnemonic(Wordlist, WordCount)` | Generate mnemonic | `Mnemonic.cs:48` |
| `Mnemonic.DeriveExtKey(passphrase)` | BIP39 seed derivation | `Mnemonic.cs:68` |
| `ExtKey.CreateFromSeed(bytes)` | Create master key | `KeyManagementService.cs:78` |
| `ExtKey.Derive(KeyPath)` | BIP32 child derivation | `KeyManagementService.cs:79` |
| `PrivateKey.ToBytes()` | Extract raw key bytes | `KeyManagementService.cs:80` |
| `new KeyPath(string)` | Parse derivation path | `DerivationPath.cs:23` |
| `KeyPath.Indexes` | Access path indices | `DerivationPath.cs:80` |