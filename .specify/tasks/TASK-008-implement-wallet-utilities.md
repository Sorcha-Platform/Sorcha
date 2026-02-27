# Task: Implement Wallet Utilities

**ID:** TASK-008
**Status:** Not Started
**Priority:** High
**Estimate:** 8 hours
**Assignee:** Unassigned
**Created:** 2025-11-12
**Updated:** 2025-11-12

## Context

Implement wallet utilities for converting between public keys, wallet addresses, and WIF (Wallet Import Format) private keys. These utilities bridge cryptographic keys and user-friendly wallet addresses.

**Related Specifications:**
- [Sorcha.Cryptography Rewrite Spec - FR-7, FR-8](../specs/sorcha-cryptography-rewrite.md#fr-7-wallet-address-encoding)
- [Current WalletUtils](../../src/Common/Sorcha.Cryptography/WalletUtils.cs)

**Dependencies:**
- TASK-001 (Project setup)
- TASK-002 (Enums and models)
- TASK-003 (Core crypto module)
- TASK-006 (Hash provider)
- TASK-007 (Encoding utilities)

## Objective

Implement IWalletUtilities interface for wallet address generation, validation, and WIF key encoding/decoding.

## Implementation Details

### Files to Create

1. **Interfaces/IWalletUtilities.cs** - Interface definition
2. **Utilities/WalletUtilities.cs** - Implementation

### Technical Approach

**Interface: Interfaces/IWalletUtilities.cs**
```csharp
namespace Sorcha.Cryptography.Interfaces;

/// <summary>
/// Provides wallet address and WIF key utilities.
/// </summary>
public interface IWalletUtilities
{
    // Wallet Address Operations
    string? PublicKeyToWallet(byte[] publicKey, byte network);
    (byte Network, byte[] PublicKey)? WalletToPublicKey(string walletAddress);
    bool ValidateWalletAddress(string walletAddress);
    (bool[] Valid, CryptoKey[] ValidWallets) ValidateWallets(string[] walletAddresses);

    // WIF Operations
    string? PrivateKeyToWIF(byte[] privateKey, byte network);
    (byte Network, byte[] PrivateKey)? WIFToPrivateKey(string wif);
    bool ValidateWIF(string wif);
}
```

### Wallet Address Format

**Sorcha Wallet Address Structure:**
```
Bech32 Format: ws1<encoded_data>
- HRP: "ws1" (Wallet Sorcha version 1)
- Network byte: 1 byte (ED25519=0x00, NISTP256=0x01, RSA4096=0x02)
- Public key: Variable length
- Checksum: Bech32 checksum (6 chars)
```

**Generation Process:**
```csharp
public string? PublicKeyToWallet(byte[] publicKey, byte network)
{
    // 1. Combine network byte + public key
    var data = new byte[] { network }.Concat(publicKey).ToArray();

    // 2. Encode in Bech32 with "ws1" HRP
    return Bech32Utilities.Encode("ws1", data);
}
```

**Example Addresses:**
```
ED25519:  ws1qyqszqgpqyqszqgpqyqszqgpqyqszqgpqyqszqgpqyqszqgpqyqsz6wq5xa
NISTP256: ws1pqpszqgpqyqszqgpqyqszqgpqyqszqgpqyqszqgpqyqszqgpqyqszqgpqyqs3hx7mk
RSA4096:  ws1zqpszqgpqyqszqgpqyqszqgp... (longer due to key size)
```

### WIF Format

**WIF (Wallet Import Format):**
```
Base58Check Format:
- Network byte: 1 byte
- Private key: 32-64 bytes (depending on algorithm)
- Checksum: 4 bytes (double SHA-256)
```

**Encoding Process:**
```csharp
public string? PrivateKeyToWIF(byte[] privateKey, byte network)
{
    // 1. Combine network byte + private key
    var data = new byte[] { network }.Concat(privateKey).ToArray();

    // 2. Double SHA-256 for checksum
    var hash = SHA256.HashData(SHA256.HashData(data));
    var checksum = hash.Take(4).ToArray();

    // 3. Combine data + checksum
    var fullData = data.Concat(checksum).ToArray();

    // 4. Encode in Base58
    return Base58Utilities.Encode(fullData);
}
```

### Implementation

**Utilities/WalletUtilities.cs**
```csharp
namespace Sorcha.Cryptography.Utilities;

public sealed class WalletUtilities : IWalletUtilities
{
    private const string WalletHRP = "ws1";
    private readonly IEncodingProvider _encoding;
    private readonly IHashProvider _hash;

    public WalletUtilities(IEncodingProvider encoding, IHashProvider hash)
    {
        _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
        _hash = hash ?? throw new ArgumentNullException(nameof(hash));
    }

    public string? PublicKeyToWallet(byte[] publicKey, byte network)
    {
        if (publicKey == null || publicKey.Length == 0)
            return null;

        // Combine network byte + public key
        var data = new byte[] { network }.Concat(publicKey).ToArray();

        // Encode in Bech32
        return _encoding.EncodeBech32(WalletHRP, data);
    }

    public (byte Network, byte[] PublicKey)? WalletToPublicKey(string walletAddress)
    {
        if (string.IsNullOrEmpty(walletAddress))
            return null;

        // Decode Bech32
        var (hrp, data) = _encoding.DecodeBech32(walletAddress);

        if (hrp != WalletHRP || data == null || data.Length < 2)
            return null;

        byte network = data[0];
        byte[] publicKey = data.Skip(1).ToArray();

        return (network, publicKey);
    }

    public bool ValidateWalletAddress(string walletAddress)
    {
        var result = WalletToPublicKey(walletAddress);
        if (result == null)
            return false;

        var (network, publicKey) = result.Value;

        // Validate network type
        if (!Enum.IsDefined(typeof(WalletNetworks), network))
            return false;

        // Validate key length
        var expectedLength = ((WalletNetworks)network).GetPublicKeySize();
        return publicKey.Length == expectedLength;
    }

    public (bool[] Valid, CryptoKey[] ValidWallets) ValidateWallets(string[] walletAddresses)
    {
        var valid = new bool[walletAddresses.Length];
        var validWallets = new List<CryptoKey>();

        for (int i = 0; i < walletAddresses.Length; i++)
        {
            var result = WalletToPublicKey(walletAddresses[i]);
            valid[i] = result != null;

            if (result != null)
            {
                var (network, publicKey) = result.Value;
                validWallets.Add(new CryptoKey((WalletNetworks)network, publicKey));
            }
        }

        return (valid, validWallets.ToArray());
    }

    public string? PrivateKeyToWIF(byte[] privateKey, byte network)
    {
        if (privateKey == null || privateKey.Length == 0)
            return null;

        // Combine network + private key
        var data = new byte[] { network }.Concat(privateKey).ToArray();

        // Use Base58Check encoding (includes checksum)
        return _encoding.EncodeBase58Check(data);
    }

    public (byte Network, byte[] PrivateKey)? WIFToPrivateKey(string wif)
    {
        if (string.IsNullOrEmpty(wif))
            return null;

        // Decode Base58Check (validates checksum)
        var data = _encoding.DecodeBase58Check(wif);

        if (data == null || data.Length < 2)
            return null;

        byte network = data[0];
        byte[] privateKey = data.Skip(1).ToArray();

        return (network, privateKey);
    }

    public bool ValidateWIF(string wif)
    {
        var result = WIFToPrivateKey(wif);
        if (result == null)
            return false;

        var (network, privateKey) = result.Value;

        // Validate network type
        if (!Enum.IsDefined(typeof(WalletNetworks), network))
            return false;

        // Validate key length
        var expectedLength = ((WalletNetworks)network).GetPrivateKeySize();
        return privateKey.Length == expectedLength;
    }
}
```

### Integration with Transaction Creation

**Usage in Transaction Signing:**
```csharp
// 1. Parse WIF private key
var wifResult = walletUtils.WIFToPrivateKey(wifPrivateKey);
byte network = wifResult.Value.Network;
byte[] privateKey = wifResult.Value.PrivateKey;

// 2. Sign transaction
var signature = await cryptoModule.SignAsync(txHash, network, privateKey);

// 3. Calculate sender wallet address
var publicKey = await cryptoModule.CalculatePublicKeyAsync(network, privateKey);
string senderWallet = walletUtils.PublicKeyToWallet(publicKey.Value, network);
```

### Constitutional Compliance

- ✅ Standard Bech32 encoding (human-readable)
- ✅ Bitcoin-compatible WIF format
- ✅ Checksum validation for error detection
- ✅ Complete XML documentation
- ✅ Batch validation support

## Testing Requirements

### Unit Tests (Unit/WalletUtilitiesTests.cs)

**Wallet Address Tests:**
- [ ] PublicKeyToWallet for ED25519
- [ ] PublicKeyToWallet for NISTP256
- [ ] PublicKeyToWallet for RSA4096
- [ ] WalletToPublicKey round trip
- [ ] ValidateWalletAddress for valid addresses
- [ ] ValidateWalletAddress rejects invalid addresses
- [ ] ValidateWallets batch validation
- [ ] Invalid network byte handling
- [ ] Invalid key length handling
- [ ] HRP validation ("ws1" required)

**WIF Tests:**
- [ ] PrivateKeyToWIF for ED25519
- [ ] PrivateKeyToWIF for NISTP256
- [ ] PrivateKeyToWIF for RSA4096
- [ ] WIFToPrivateKey round trip
- [ ] ValidateWIF for valid keys
- [ ] ValidateWIF rejects invalid keys
- [ ] Checksum validation
- [ ] Invalid network byte handling
- [ ] Invalid key length handling

**Integration Tests:**
- [ ] Generate key → derive wallet → validate
- [ ] Generate key → export WIF → import WIF → sign
- [ ] Cross-algorithm wallet generation

### Test Vectors (TestVectors/WalletUtilitiesTestVectors.cs)

- [ ] Known ED25519 key → wallet address
- [ ] Known NISTP256 key → wallet address
- [ ] Known ED25519 key → WIF
- [ ] Known NISTP256 key → WIF
- [ ] Bitcoin WIF compatibility test

## Acceptance Criteria

- [ ] IWalletUtilities interface fully defined
- [ ] WalletUtilities implementation complete
- [ ] Wallet address generation working (Bech32)
- [ ] Wallet address validation working
- [ ] WIF encoding/decoding working
- [ ] WIF validation working
- [ ] Batch validation working
- [ ] All unit tests passing (>95% coverage)
- [ ] Test vectors validated
- [ ] Bitcoin WIF compatibility verified

## Implementation Notes

**Address Format Rationale:**
- Bech32: Error-detecting, case-insensitive, QR-friendly
- "ws1" HRP: Identifies Sorcha wallets version 1
- Network byte: Identifies key algorithm type

**Security Considerations:**
- WIF keys contain private keys → handle securely
- Always validate checksums
- Zeroize private keys after use
- Never log or display WIF keys

## Review Checklist

- [ ] Code follows constitutional principles
- [ ] Bech32 properly implemented
- [ ] Base58Check properly implemented
- [ ] Checksums validated
- [ ] Test vectors passing
- [ ] Bitcoin compatibility verified

---

**Task Control**
- **Created By:** Claude Code
- **Reviewed By:** (Pending)
- **Approved By:** (Pending)
