# NBitcoin Workflows Reference

## Contents
- Wallet Creation Workflow
- Wallet Recovery Workflow
- Address Derivation Workflow
- System Path Usage
- Testing Workflows

---

## Wallet Creation Workflow

### Complete Flow

```csharp
// src/Core/Sorcha.Wallet.Core/Services/Implementation/WalletManager.cs:53-134
public async Task<(WalletEntity Wallet, Mnemonic Mnemonic)> CreateWalletAsync(
    string name, string algorithm, string owner, string tenant,
    int wordCount = 12, string? passphrase = null, CancellationToken cancellationToken = default)
{
    // Step 1: Generate mnemonic (12 or 24 words)
    var mnemonic = Mnemonic.Generate(wordCount);

    // Step 2: Derive master key from mnemonic + optional passphrase
    var masterKey = await _keyManagement.DeriveMasterKeyAsync(mnemonic, passphrase);

    // Step 3: Derive first key at BIP44 path m/44'/0'/0'/0/0
    var path = DerivationPath.CreateBip44(0, 0, 0, 0);
    var (privateKey, publicKey) = await _keyManagement.DeriveKeyAtPathAsync(masterKey, path, algorithm);

    // Step 4: Generate wallet address
    var address = await _keyManagement.GenerateAddressAsync(publicKey, algorithm);

    // Step 5: Encrypt private key before storage
    var (encryptedKey, keyId) = await _keyManagement.EncryptPrivateKeyAsync(privateKey, string.Empty);

    // Step 6: Create wallet entity
    var wallet = new WalletEntity
    {
        Address = address,
        PublicKey = Convert.ToBase64String(publicKey),
        EncryptedPrivateKey = encryptedKey,
        EncryptionKeyId = keyId,
        Algorithm = algorithm,
        Metadata = new Dictionary<string, string>
        {
            ["WordCount"] = mnemonic.WordCount.ToString(),
            ["DerivationPath"] = path.Path
        }
    };

    await _repository.AddAsync(wallet, cancellationToken);
    return (wallet, mnemonic);  // Return mnemonic to user (ONCE, then forget)
}
```

### Checklist

Copy this checklist and track progress:
- [ ] Generate mnemonic with `Mnemonic.Generate(wordCount)`
- [ ] Derive master key with `DeriveMasterKeyAsync(mnemonic, passphrase)`
- [ ] Create BIP44 path with `DerivationPath.CreateBip44(...)`
- [ ] Derive keys with `DeriveKeyAtPathAsync(masterKey, path, algorithm)`
- [ ] Generate address with `GenerateAddressAsync(publicKey, algorithm)`
- [ ] Encrypt private key with `EncryptPrivateKeyAsync(privateKey, keyId)`
- [ ] Store wallet entity (NEVER store mnemonic)
- [ ] Return mnemonic to user exactly once

---

## Wallet Recovery Workflow

### Complete Flow

```csharp
// src/Core/Sorcha.Wallet.Core/Services/Implementation/WalletManager.cs:137-250
public async Task<WalletEntity> RecoverWalletAsync(
    Mnemonic mnemonic, string name, string algorithm, string owner, string tenant,
    string? passphrase = null, CancellationToken cancellationToken = default)
{
    // Step 1: Derive master key from provided mnemonic
    var masterKey = await _keyManagement.DeriveMasterKeyAsync(mnemonic, passphrase);

    // Step 2: Derive primary address at m/44'/0'/0'/0/0
    var primaryPath = DerivationPath.CreateBip44(0, 0, 0, 0);
    var (_, primaryPublicKey) = await _keyManagement.DeriveKeyAtPathAsync(masterKey, primaryPath, algorithm);
    var address = await _keyManagement.GenerateAddressAsync(primaryPublicKey, algorithm);

    // Step 3: Check if wallet already exists
    var existing = await _repository.GetByAddressAsync(address, false, false, false, cancellationToken);
    if (existing != null && existing.Status != WalletStatus.Deleted)
    {
        throw new InvalidOperationException($"Wallet {address} already exists");
    }

    // Step 4: Reactivate if soft-deleted, otherwise create new
    if (existing?.Status == WalletStatus.Deleted)
    {
        existing.Status = WalletStatus.Active;
        existing.Metadata["Recovered"] = "true";
        await _repository.UpdateAsync(existing, cancellationToken);
        return existing;
    }

    // Create new wallet entity...
}
```

### Validation Feedback Loop

1. Accept mnemonic phrase from user
2. Validate: `Mnemonic.IsValid(phrase)`
3. If invalid, prompt user to re-enter
4. Only proceed when validation passes

```csharp
// Validation pattern
public IActionResult RecoverWallet(string phrase)
{
    if (!Mnemonic.IsValid(phrase))
    {
        return BadRequest("Invalid mnemonic phrase. Please check and try again.");
    }
    
    var mnemonic = new Mnemonic(phrase);
    // Proceed with recovery...
}
```

---

## Address Derivation Workflow

### Client-Side Derivation Pattern

The server NEVER stores mnemonics. Clients derive keys locally and register only public keys.

```csharp
// src/Core/Sorcha.Wallet.Core/Services/Implementation/WalletManager.cs:437-547
public async Task<WalletAddress> RegisterDerivedAddressAsync(
    string walletAddress,
    string derivedPublicKey,    // Base64-encoded - client derived this
    string derivedAddress,
    string derivationPath,      // e.g., "m/44'/0'/0'/0/1"
    ...)
{
    // Step 1: Validate BIP44 path
    if (!DerivationPath.TryParseBip44(derivationPath, out uint coinType, out uint account, out uint change, out uint addressIndex))
    {
        throw new ArgumentException($"Invalid BIP44 path: {derivationPath}");
    }

    // Step 2: Validate change value (0=receive, 1=change)
    if (change > 1)
    {
        throw new ArgumentException($"Change must be 0 or 1, got: {change}");
    }

    // Step 3: Enforce BIP44 gap limit (max 20 unused addresses)
    var unusedCount = wallet.Addresses
        .Where(a => !a.IsUsed && a.Account == account && a.IsChange == (change == 1))
        .Count();

    if (unusedCount >= 20)
    {
        throw new InvalidOperationException(
            $"Gap limit exceeded: {unusedCount} unused addresses for account {account}");
    }

    // Step 4: Register the derived address
    var walletAddressEntity = new WalletAddress
    {
        Address = derivedAddress,
        PublicKey = derivedPublicKey,
        DerivationPath = derivationPath,
        Index = (int)addressIndex,
        Account = account,
        IsChange = change == 1
    };

    wallet.Addresses.Add(walletAddressEntity);
    await _repository.UpdateAsync(wallet, cancellationToken);
    return walletAddressEntity;
}
```

### Gap Limit Enforcement

BIP44 recommends max 20 unused addresses per account/change chain:

```csharp
// Check before generating new address
var unusedCount = wallet.Addresses
    .Where(a => !a.IsUsed && a.Account == account && a.IsChange == isChange)
    .Count();

if (unusedCount >= 20)
{
    _logger.LogWarning("Gap limit reached for account {Account}", account);
    throw new InvalidOperationException("Gap limit exceeded");
}
```

---

## System Path Usage

### Sorcha System Paths

```csharp
// src/Common/Sorcha.Wallet.Contracts/Constants/SorchaDerivationPaths.cs

// Defined system paths
public const string RegisterAttestation = "sorcha:register-attestation";   // → m/44'/0'/0'/0/100
public const string RegisterControl = "sorcha:register-control";           // → m/44'/0'/0'/0/101
public const string DocketSigning = "sorcha:docket-signing";               // → m/44'/0'/0'/0/102
```

### Resolving System Paths

```csharp
// In transaction signing flow
var resolvedPath = SorchaDerivationPaths.IsSystemPath(derivationPath)
    ? SorchaDerivationPaths.ResolvePath(derivationPath)
    : derivationPath;

var parsedPath = new DerivationPath(resolvedPath);
var (derivedPrivateKey, derivedPublicKey) = await _keyManagement.DeriveKeyAtPathAsync(
    masterKey, parsedPath, wallet.Algorithm);
```

### When to Use Each Path

| System Path | Use Case |
|-------------|----------|
| `sorcha:register-attestation` | Owners/admins signing register creation attestations |
| `sorcha:register-control` | Validator service signing control records |
| `sorcha:docket-signing` | Validator service signing dockets |

---

## Testing Workflows

### Unit Test: Key Derivation Consistency

```csharp
// tests/Sorcha.Wallet.Service.Tests/Services/KeyManagementServiceTests.cs
[Fact]
public async Task DeriveMasterKeyAsync_ShouldDeriveSameKey_ForSameMnemonic()
{
    var mnemonic = Mnemonic.Generate(12);
    
    var masterKey1 = await _keyManagement.DeriveMasterKeyAsync(mnemonic);
    var masterKey2 = await _keyManagement.DeriveMasterKeyAsync(mnemonic);
    
    masterKey1.Should().BeEquivalentTo(masterKey2);
}

[Fact]
public async Task DeriveMasterKeyAsync_ShouldDeriveDifferentKeys_ForDifferentMnemonics()
{
    var mnemonic1 = Mnemonic.Generate(12);
    var mnemonic2 = Mnemonic.Generate(12);
    
    var masterKey1 = await _keyManagement.DeriveMasterKeyAsync(mnemonic1);
    var masterKey2 = await _keyManagement.DeriveMasterKeyAsync(mnemonic2);
    
    masterKey1.Should().NotBeEquivalentTo(masterKey2);
}
```

### Integration Test: Complete HD Wallet Workflow

```csharp
// tests/Sorcha.Wallet.Service.IntegrationTests/HDWalletAddressManagementTests.cs
[Fact]
public async Task CompleteHDWalletWorkflow_ShouldDemonstrateAllFeatures()
{
    // Step 1: Create wallet (returns mnemonic)
    var createResponse = await _client.PostAsJsonAsync("/api/v1/wallets", new { name = "Test", algorithm = "ED25519" });
    var mnemonic = (await createResponse.Content.ReadAsJsonAsync<CreateWalletResponse>()).MnemonicWords;

    // Step 2: Register derived address at m/44'/0'/0'/0/1
    var address1Request = new RegisterDerivedAddressRequest
    {
        DerivedPublicKey = Convert.ToBase64String(new byte[32]),
        DerivedAddress = $"ws1q{Guid.NewGuid():N}",
        DerivationPath = "m/44'/0'/0'/0/1",
        Label = "Payment Address 1"
    };
    
    // Step 3: Verify gap limit enforcement
    // ... register 20 addresses, then verify 21st fails
}
```

### Test Checklist

Copy this checklist for comprehensive HD wallet testing:
- [ ] Same mnemonic produces same master key
- [ ] Different mnemonics produce different keys
- [ ] Passphrase changes derived keys
- [ ] BIP44 path parsing extracts correct components
- [ ] Invalid paths are rejected
- [ ] Gap limit (20) is enforced
- [ ] System paths resolve correctly
- [ ] Mnemonic.ToString() doesn't expose phrase