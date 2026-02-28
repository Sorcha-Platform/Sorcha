// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Domain.Entities;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;
using Sorcha.Wallet.Core.Domain.Events;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Events.Interfaces;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;

namespace Sorcha.Wallet.Core.Services.Implementation;

/// <summary>
/// Main wallet service implementation that orchestrates all wallet operations.
/// </summary>
public class WalletManager : IWalletService
{
    private readonly IKeyManagementService _keyManagement;
    private readonly ITransactionService _transactionService;
    private readonly IDelegationService _delegationService;
    private readonly IWalletRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<WalletManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WalletManager"/> class.
    /// The main orchestrator for all wallet operations including creation, recovery, address generation,
    /// transaction signing, and access control. Coordinates between key management, transaction services,
    /// delegation, and event publishing.
    /// </summary>
    /// <param name="keyManagement">Service for cryptographic key operations and HD wallet management.</param>
    /// <param name="transactionService">Service for transaction signing and verification.</param>
    /// <param name="delegationService">Service for access control and permission management.</param>
    /// <param name="repository">Repository for wallet data persistence.</param>
    /// <param name="eventPublisher">Publisher for wallet domain events (audit and notification).</param>
    /// <param name="logger">Logger for wallet operations and error tracking.</param>
    public WalletManager(
        IKeyManagementService keyManagement,
        ITransactionService transactionService,
        IDelegationService delegationService,
        IWalletRepository repository,
        IEventPublisher eventPublisher,
        ILogger<WalletManager> logger)
    {
        _keyManagement = keyManagement ?? throw new ArgumentNullException(nameof(keyManagement));
        _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
        _delegationService = delegationService ?? throw new ArgumentNullException(nameof(delegationService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<(WalletEntity Wallet, Mnemonic Mnemonic)> CreateWalletAsync(
        string name,
        string algorithm,
        string owner,
        string tenant,
        int wordCount = 12,
        string? passphrase = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(algorithm))
            throw new ArgumentException("Algorithm cannot be empty", nameof(algorithm));
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner cannot be empty", nameof(owner));
        if (string.IsNullOrWhiteSpace(tenant))
            throw new ArgumentException("Tenant cannot be empty", nameof(tenant));

        try
        {
            _logger.LogInformation("Creating wallet for owner {Owner} using {Algorithm}", owner, algorithm);

            // Generate mnemonic
            var mnemonic = Mnemonic.Generate(wordCount);

            // Derive master key from mnemonic
            var masterKey = await _keyManagement.DeriveMasterKeyAsync(mnemonic, passphrase);

            // Derive first key at BIP44 path m/44'/0'/0'/0/0
            var path = DerivationPath.CreateBip44(0, 0, 0, 0);
            var (privateKey, publicKey) = await _keyManagement.DeriveKeyAtPathAsync(
                masterKey, path, algorithm);

            // Generate wallet address
            var address = await _keyManagement.GenerateAddressAsync(publicKey, algorithm);

            // Encrypt private key
            var (encryptedKey, keyId) = await _keyManagement.EncryptPrivateKeyAsync(
                privateKey, string.Empty);

            // Create wallet entity
            var wallet = new WalletEntity
            {
                Address = address,
                PublicKey = Convert.ToBase64String(publicKey),
                EncryptedPrivateKey = encryptedKey,
                EncryptionKeyId = keyId,
                Algorithm = algorithm,
                Owner = owner,
                Tenant = tenant,
                Name = name,
                Status = WalletStatus.Active,
                CreatedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, string>
                {
                    ["WordCount"] = mnemonic.WordCount.ToString(),
                    ["DerivationPath"] = path.Path
                }
            };

            // Save to repository
            await _repository.AddAsync(wallet, cancellationToken);

            // Publish event
            await _eventPublisher.PublishAsync(new WalletCreatedEvent
            {
                WalletAddress = wallet.Address,
                OccurredAt = wallet.CreatedAt,
                Owner = wallet.Owner,
                Tenant = wallet.Tenant,
                Algorithm = wallet.Algorithm,
                Name = wallet.Name
            }, cancellationToken);

            _logger.LogInformation("Created wallet {Address} for owner {Owner}", wallet.Address, owner);
            return (wallet, mnemonic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create wallet for owner {Owner}", owner);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<WalletEntity> RecoverWalletAsync(
        Mnemonic mnemonic,
        string name,
        string algorithm,
        string owner,
        string tenant,
        string? passphrase = null,
        CancellationToken cancellationToken = default)
    {
        if (mnemonic == null)
            throw new ArgumentNullException(nameof(mnemonic));
        if (string.IsNullOrWhiteSpace(algorithm))
            throw new ArgumentException("Algorithm cannot be empty", nameof(algorithm));
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner cannot be empty", nameof(owner));
        if (string.IsNullOrWhiteSpace(tenant))
            throw new ArgumentException("Tenant cannot be empty", nameof(tenant));

        try
        {
            _logger.LogInformation("Recovering wallet for owner {Owner} using {Algorithm}", owner, algorithm);

            // Derive master key
            var masterKey = await _keyManagement.DeriveMasterKeyAsync(mnemonic, passphrase);

            // Derive first address to use as primary wallet address
            var primaryPath = DerivationPath.CreateBip44(0, 0, 0, 0);
            var (primaryPrivateKey, primaryPublicKey) = await _keyManagement.DeriveKeyAtPathAsync(
                masterKey, primaryPath, algorithm);

            var address = await _keyManagement.GenerateAddressAsync(primaryPublicKey, algorithm);

            // Check if wallet already exists
            var existing = await _repository.GetByAddressAsync(address, false, false, false, cancellationToken);
            if (existing != null && existing.Status != WalletStatus.Deleted)
            {
                throw new InvalidOperationException($"Wallet {address} already exists and is not deleted");
            }

            // If wallet was soft-deleted, reactivate it instead of creating a new one
            if (existing != null && existing.Status == WalletStatus.Deleted)
            {
                existing.Status = WalletStatus.Active;
                existing.Name = name;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.LastAccessedAt = DateTime.UtcNow;
                existing.DeletedAt = null;
                existing.Metadata["Recovered"] = "true";
                existing.Metadata["RecoveredAt"] = DateTime.UtcNow.ToString("O");

                await _repository.UpdateAsync(existing, cancellationToken);

                // Publish reactivation event
                await _eventPublisher.PublishAsync(new WalletRecoveredEvent
                {
                    WalletAddress = existing.Address,
                    OccurredAt = DateTime.UtcNow,
                    Owner = existing.Owner,
                    Tenant = existing.Tenant,
                    Algorithm = existing.Algorithm
                }, cancellationToken);

                _logger.LogInformation("Reactivated soft-deleted wallet {Address} for owner {Owner}",
                    existing.Address, owner);
                return existing;
            }

            // Encrypt private key
            var (encryptedKey, keyId) = await _keyManagement.EncryptPrivateKeyAsync(
                primaryPrivateKey, string.Empty);

            // Create wallet entity
            var wallet = new WalletEntity
            {
                Address = address,
                PublicKey = Convert.ToBase64String(primaryPublicKey),
                EncryptedPrivateKey = encryptedKey,
                EncryptionKeyId = keyId,
                Algorithm = algorithm,
                Owner = owner,
                Tenant = tenant,
                Name = name,
                Status = WalletStatus.Active,
                CreatedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, string>
                {
                    ["WordCount"] = mnemonic.WordCount.ToString(),
                    ["Recovered"] = "true"
                }
            };

            // Save to repository
            await _repository.AddAsync(wallet, cancellationToken);

            // Publish event
            await _eventPublisher.PublishAsync(new WalletRecoveredEvent
            {
                WalletAddress = wallet.Address,
                OccurredAt = wallet.CreatedAt,
                Owner = wallet.Owner,
                Tenant = wallet.Tenant,
                Algorithm = wallet.Algorithm
            }, cancellationToken);

            _logger.LogInformation("Recovered wallet {Address} for owner {Owner}", wallet.Address, owner);
            return wallet;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover wallet for owner {Owner}", owner);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<WalletEntity?> GetWalletAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address cannot be empty", nameof(address));

        try
        {
            // Include addresses by default for address management features
            var wallet = await _repository.GetByAddressAsync(address, includeAddresses: true, false, false, cancellationToken);

            if (wallet != null)
            {
                // Update last accessed time
                wallet.LastAccessedAt = DateTime.UtcNow;
                await _repository.UpdateAsync(wallet, cancellationToken);
            }

            return wallet;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get wallet {Address}", address);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<WalletEntity>> GetWalletsByOwnerAsync(
        string owner,
        string tenant,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner cannot be empty", nameof(owner));
        if (string.IsNullOrWhiteSpace(tenant))
            throw new ArgumentException("Tenant cannot be empty", nameof(tenant));

        try
        {
            var wallets = await _repository.GetByOwnerAsync(owner, tenant, cancellationToken);
            return wallets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get wallets for owner {Owner}", owner);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<WalletEntity> UpdateWalletAsync(
        string address,
        string? name = null,
        string? description = null,
        Dictionary<string, string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address cannot be empty", nameof(address));

        try
        {
            var wallet = await _repository.GetByAddressAsync(address, false, false, false, cancellationToken);
            if (wallet == null)
            {
                throw new InvalidOperationException($"Wallet {address} not found");
            }

            // Update fields
            if (name != null)
                wallet.Name = name;

            if (description != null)
                wallet.Description = description;

            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    wallet.Metadata[tag.Key] = tag.Value;
                }
            }

            wallet.LastAccessedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(wallet, cancellationToken);

            _logger.LogDebug("Updated wallet {Address}", wallet.Address);
            return wallet;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update wallet {Address}", address);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteWalletAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address cannot be empty", nameof(address));

        try
        {
            var wallet = await _repository.GetByAddressAsync(address, false, false, false, cancellationToken);
            if (wallet == null)
            {
                throw new InvalidOperationException($"Wallet {address} not found");
            }

            // Soft delete by changing status
            var previousStatus = wallet.Status;
            wallet.Status = WalletStatus.Deleted;
            wallet.UpdatedAt = DateTime.UtcNow;
            wallet.LastAccessedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(wallet, cancellationToken);

            await _eventPublisher.PublishAsync(new WalletStatusChangedEvent
            {
                WalletAddress = wallet.Address,
                OldStatus = previousStatus,
                NewStatus = WalletStatus.Deleted
            }, cancellationToken);

            _logger.LogInformation("Deleted wallet {Address}", address);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete wallet {Address}", address);
            throw;
        }
    }

    /// <inheritdoc/>
    [Obsolete("Use RegisterDerivedAddressAsync for client-side derivation instead", true)]
    public Task<WalletAddress> GenerateAddressAsync(
        string walletAddress,
        int index,
        bool isChange = false,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Server-side address generation is not supported. Use RegisterDerivedAddressAsync instead.");
    }

    /// <summary>
    /// Registers a client-derived HD wallet address.
    /// The client derives the address locally and provides the public key and derivation path.
    /// This maintains security by never storing the mnemonic on the server.
    /// </summary>
    /// <param name="walletAddress">Parent wallet address</param>
    /// <param name="derivedPublicKey">Public key derived by client (base64 encoded)</param>
    /// <param name="derivedAddress">The derived wallet address</param>
    /// <param name="derivationPath">BIP44 derivation path (e.g., m/44'/0'/0'/0/1)</param>
    /// <param name="label">Optional label for the address</param>
    /// <param name="notes">Optional notes</param>
    /// <param name="tags">Optional tags for categorization</param>
    /// <param name="metadata">Optional metadata dictionary</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created WalletAddress entity</returns>
    /// <exception cref="ArgumentException">If parameters are invalid</exception>
    /// <exception cref="InvalidOperationException">If wallet not found or validation fails</exception>
    public async Task<WalletAddress> RegisterDerivedAddressAsync(
        string walletAddress,
        string derivedPublicKey,
        string derivedAddress,
        string derivationPath,
        string? label = null,
        string? notes = null,
        string? tags = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new ArgumentException("Wallet address cannot be empty", nameof(walletAddress));
        if (string.IsNullOrWhiteSpace(derivedPublicKey))
            throw new ArgumentException("Derived public key cannot be empty", nameof(derivedPublicKey));
        if (string.IsNullOrWhiteSpace(derivedAddress))
            throw new ArgumentException("Derived address cannot be empty", nameof(derivedAddress));
        if (string.IsNullOrWhiteSpace(derivationPath))
            throw new ArgumentException("Derivation path cannot be empty", nameof(derivationPath));

        try
        {
            // Get parent wallet
            var wallet = await _repository.GetByAddressAsync(walletAddress, includeAddresses: true, false, false, cancellationToken);
            if (wallet == null)
            {
                throw new InvalidOperationException($"Wallet {walletAddress} not found");
            }

            // Parse and validate BIP44 derivation path
            if (!DerivationPath.TryParseBip44(derivationPath, out uint coinType, out uint account, out uint change, out uint addressIndex))
            {
                throw new ArgumentException($"Invalid BIP44 derivation path: {derivationPath}", nameof(derivationPath));
            }

            // Validate change value (must be 0 or 1)
            if (change > 1)
            {
                throw new ArgumentException($"Invalid change value in path: {change}. Must be 0 (receive) or 1 (change).", nameof(derivationPath));
            }

            bool isChange = change == 1;

            // Check for duplicate address
            if (wallet.Addresses.Any(a => a.Address == derivedAddress))
            {
                throw new InvalidOperationException($"Address {derivedAddress} already exists for this wallet");
            }

            // Check for duplicate derivation path
            if (wallet.Addresses.Any(a => a.DerivationPath == derivationPath))
            {
                throw new InvalidOperationException($"Derivation path {derivationPath} already exists for this wallet");
            }

            // Gap limit check (BIP44 recommends max 20 unused addresses)
            var unusedCount = wallet.Addresses
                .Where(a => !a.IsUsed && a.Account == account && a.IsChange == isChange)
                .Count();

            if (unusedCount >= 20)
            {
                _logger.LogWarning(
                    "Wallet {WalletAddress} has {UnusedCount} unused addresses for account {Account}, change={IsChange}. " +
                    "BIP44 recommends max 20 gap. Consider marking addresses as used.",
                    walletAddress, unusedCount, account, isChange);

                throw new InvalidOperationException(
                    $"Gap limit exceeded: {unusedCount} unused addresses already exist for account {account}, change={isChange}. " +
                    "Maximum recommended gap is 20 (BIP44). Mark existing addresses as used before generating more.");
            }

            // Create new address entity
            var walletAddressEntity = new WalletAddress
            {
                Id = Guid.NewGuid(),
                ParentWalletAddress = walletAddress,
                Address = derivedAddress,
                PublicKey = derivedPublicKey,
                DerivationPath = derivationPath,
                Index = (int)addressIndex,
                Account = account,
                IsChange = isChange,
                Label = label,
                Notes = notes,
                Tags = tags,
                IsUsed = false,
                CreatedAt = DateTime.UtcNow,
                Metadata = metadata ?? new Dictionary<string, string>()
            };

            // Add to wallet's address collection
            wallet.Addresses.Add(walletAddressEntity);
            wallet.UpdatedAt = DateTime.UtcNow;

            // Save to repository
            await _repository.UpdateAsync(wallet, cancellationToken);

            _logger.LogInformation(
                "Registered derived address {DerivedAddress} for wallet {WalletAddress} at path {Path}",
                derivedAddress, walletAddress, derivationPath);

            return walletAddressEntity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register derived address for wallet {Address}", walletAddress);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<(byte[] Signature, byte[] PublicKey)> SignTransactionAsync(
        string walletAddress,
        byte[] transactionData,
        string? derivationPath = null,
        bool isPreHashed = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new ArgumentException("Wallet address cannot be empty", nameof(walletAddress));
        if (transactionData == null || transactionData.Length == 0)
            throw new ArgumentException("Transaction data cannot be empty", nameof(transactionData));

        try
        {
            var wallet = await _repository.GetByAddressAsync(walletAddress, false, false, false, cancellationToken);
            if (wallet == null)
            {
                throw new InvalidOperationException($"Wallet {walletAddress} not found");
            }

            // Decrypt master private key
            var masterKey = await _keyManagement.DecryptPrivateKeyAsync(
                wallet.EncryptedPrivateKey,
                wallet.EncryptionKeyId);

            byte[] signingKey;
            byte[] publicKey;

            // If derivation path provided, derive child key
            if (!string.IsNullOrWhiteSpace(derivationPath))
            {
                // Resolve Sorcha system paths (e.g., "sorcha:register-attestation") to BIP44 paths
                var resolvedPath = Constants.SorchaDerivationPaths.IsSystemPath(derivationPath)
                    ? Constants.SorchaDerivationPaths.ResolvePath(derivationPath)
                    : derivationPath;

                var parsedPath = new DerivationPath(resolvedPath);
                var (derivedPrivateKey, derivedPublicKey) = await _keyManagement.DeriveKeyAtPathAsync(
                    masterKey,
                    parsedPath,
                    wallet.Algorithm);

                signingKey = derivedPrivateKey;
                publicKey = derivedPublicKey;

                _logger.LogInformation(
                    "Signing transaction for wallet {Address} with derived key at path {Path} (resolved: {ResolvedPath})",
                    walletAddress, derivationPath, resolvedPath);
            }
            else
            {
                signingKey = masterKey;
                publicKey = Convert.FromBase64String(wallet.PublicKey!);

                _logger.LogInformation(
                    "Signing transaction for wallet {Address} with master key",
                    walletAddress);
            }

            // Sign transaction
            var signature = await _transactionService.SignTransactionAsync(
                transactionData, signingKey, wallet.Algorithm, isPreHashed);

            // Publish event
            await _eventPublisher.PublishAsync(new TransactionSignedEvent
            {
                WalletAddress = walletAddress,
                TransactionId = Convert.ToBase64String(signature[..Math.Min(32, signature.Length)]),
                SignedBy = walletAddress
            }, cancellationToken);

            _logger.LogInformation("Signed transaction for wallet {Address}", walletAddress);
            return (signature, publicKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sign transaction for wallet {Address}", walletAddress);
            throw;
        }
    }

    /// <summary>
    /// Decrypts a payload using the wallet's private key
    /// </summary>
    /// <param name="walletAddress">Wallet address</param>
    /// <param name="encryptedPayload">Encrypted payload bytes</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Decrypted payload</returns>
    public async Task<byte[]> DecryptPayloadAsync(
        string walletAddress,
        byte[] encryptedPayload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new ArgumentException("Wallet address cannot be empty", nameof(walletAddress));
        if (encryptedPayload == null || encryptedPayload.Length == 0)
            throw new ArgumentException("Encrypted payload cannot be empty", nameof(encryptedPayload));

        try
        {
            var wallet = await _repository.GetByAddressAsync(walletAddress, false, false, false, cancellationToken);
            if (wallet == null)
            {
                throw new InvalidOperationException($"Wallet {walletAddress} not found");
            }

            // Decrypt private key
            var privateKey = await _keyManagement.DecryptPrivateKeyAsync(
                wallet.EncryptedPrivateKey,
                wallet.EncryptionKeyId);

            // Decrypt payload
            var decryptedPayload = await _transactionService.DecryptPayloadAsync(
                encryptedPayload, privateKey, wallet.Algorithm);

            _logger.LogInformation("Decrypted payload for wallet {Address}", walletAddress);
            return decryptedPayload;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt payload for wallet {Address}", walletAddress);
            throw;
        }
    }

    /// <summary>
    /// Encrypts a payload for a recipient wallet
    /// </summary>
    /// <param name="recipientAddress">Recipient wallet address</param>
    /// <param name="payload">Payload bytes to encrypt</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Encrypted payload</returns>
    public async Task<byte[]> EncryptPayloadAsync(
        string recipientAddress,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipientAddress))
            throw new ArgumentException("Recipient address cannot be empty", nameof(recipientAddress));
        if (payload == null || payload.Length == 0)
            throw new ArgumentException("Payload cannot be empty", nameof(payload));

        try
        {
            var recipientWallet = await _repository.GetByAddressAsync(recipientAddress, false, false, false, cancellationToken);
            if (recipientWallet == null)
            {
                throw new InvalidOperationException($"Recipient wallet {recipientAddress} not found");
            }

            if (string.IsNullOrEmpty(recipientWallet.PublicKey))
            {
                throw new InvalidOperationException($"Recipient wallet {recipientAddress} has no public key");
            }

            // Get recipient's public key (stored as base64)
            var publicKey = Convert.FromBase64String(recipientWallet.PublicKey);

            // Encrypt payload
            var encryptedPayload = await _transactionService.EncryptPayloadAsync(
                payload, publicKey, recipientWallet.Algorithm);

            _logger.LogInformation("Encrypted payload for recipient {Address}", recipientAddress);
            return encryptedPayload;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt payload for recipient {Address}", recipientAddress);
            throw;
        }
    }
}
