// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Collections.Concurrent;
using Sorcha.Wallet.Core.Domain.Entities;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;
using Sorcha.Wallet.Core.Repositories.Interfaces;

namespace Sorcha.Wallet.Core.Repositories.Implementation;

/// <summary>
/// In-memory implementation of wallet repository for testing and development.
/// Thread-safe using ConcurrentDictionary.
/// </summary>
public class InMemoryWalletRepository : IWalletRepository
{
    private readonly ConcurrentDictionary<string, WalletEntity> _wallets = new();
    private readonly ConcurrentDictionary<string, List<WalletAddress>> _addresses = new();
    private readonly ConcurrentDictionary<string, List<WalletAccess>> _accessGrants = new();

    /// <inheritdoc/>
    public Task AddAsync(WalletEntity wallet, CancellationToken cancellationToken = default)
    {
        if (wallet == null)
            throw new ArgumentNullException(nameof(wallet));

        cancellationToken.ThrowIfCancellationRequested();

        if (!_wallets.TryAdd(wallet.Address, wallet))
        {
            throw new InvalidOperationException($"Wallet with address {wallet.Address} already exists");
        }

        // Store addresses and access grants separately for query performance
        if (wallet.Addresses.Any())
        {
            _addresses[wallet.Address] = wallet.Addresses.ToList();
        }

        if (wallet.Delegates.Any())
        {
            _accessGrants[wallet.Address] = wallet.Delegates.ToList();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateAsync(WalletEntity wallet, CancellationToken cancellationToken = default)
    {
        if (wallet == null)
            throw new ArgumentNullException(nameof(wallet));

        cancellationToken.ThrowIfCancellationRequested();

        if (!_wallets.ContainsKey(wallet.Address))
        {
            throw new InvalidOperationException($"Wallet with address {wallet.Address} not found");
        }

        wallet.UpdatedAt = DateTime.UtcNow;
        _wallets[wallet.Address] = wallet;

        // Update addresses and access grants
        if (wallet.Addresses.Any())
        {
            _addresses[wallet.Address] = wallet.Addresses.ToList();
        }
        else
        {
            _addresses.TryRemove(wallet.Address, out _);
        }

        if (wallet.Delegates.Any())
        {
            _accessGrants[wallet.Address] = wallet.Delegates.ToList();
        }
        else
        {
            _accessGrants.TryRemove(wallet.Address, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address cannot be empty", nameof(address));

        cancellationToken.ThrowIfCancellationRequested();

        _wallets.TryRemove(address, out _);
        _addresses.TryRemove(address, out _);
        _accessGrants.TryRemove(address, out _);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<WalletEntity?> GetByAddressAsync(
        string address,
        bool includeAddresses = false,
        bool includeDelegates = false,
        bool includeTransactions = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address cannot be empty", nameof(address));

        cancellationToken.ThrowIfCancellationRequested();

        if (!_wallets.TryGetValue(address, out var wallet))
        {
            return Task.FromResult<WalletEntity?>(null);
        }

        // Create a copy to avoid reference issues
        var result = CloneWallet(wallet);

        // Load related data if requested
        if (includeAddresses && _addresses.TryGetValue(address, out var addresses))
        {
            result.Addresses = addresses.Select(CloneAddress).ToList();
        }
        else
        {
            result.Addresses = new List<WalletAddress>();
        }

        if (includeDelegates && _accessGrants.TryGetValue(address, out var grants))
        {
            result.Delegates = grants.Select(CloneAccess).ToList();
        }
        else
        {
            result.Delegates = new List<WalletAccess>();
        }

        if (includeTransactions)
        {
            // Transactions are stored inline with wallet
            result.Transactions = wallet.Transactions.ToList();
        }
        else
        {
            result.Transactions = new List<WalletTransaction>();
        }
        return Task.FromResult<WalletEntity?>(result);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<WalletEntity>> GetByOwnerAsync(
        string owner,
        string tenant,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner cannot be empty", nameof(owner));
        if (string.IsNullOrWhiteSpace(tenant))
            throw new ArgumentException("Tenant cannot be empty", nameof(tenant));

        cancellationToken.ThrowIfCancellationRequested();

        var wallets = _wallets.Values
            .Where(w => w.Owner == owner && w.Tenant == tenant)
            .Select(CloneWallet)
            .ToList();

        return Task.FromResult<IEnumerable<WalletEntity>>(wallets);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<WalletEntity>> GetByTenantAsync(
        string tenant,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenant))
            throw new ArgumentException("Tenant cannot be empty", nameof(tenant));

        cancellationToken.ThrowIfCancellationRequested();

        var wallets = _wallets.Values
            .Where(w => w.Tenant == tenant)
            .OrderBy(w => w.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(CloneWallet)
            .ToList();

        return Task.FromResult<IEnumerable<WalletEntity>>(wallets);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<WalletEntity>> GetAllPagedAsync(
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var wallets = _wallets.Values
            .OrderBy(w => w.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(CloneWallet)
            .ToList();

        return Task.FromResult<IEnumerable<WalletEntity>>(wallets);
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address cannot be empty", nameof(address));

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_wallets.ContainsKey(address));
    }

    /// <inheritdoc/>
    public Task AddAddressAsync(
        string walletAddress,
        WalletAddress address,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new ArgumentException("Wallet address cannot be empty", nameof(walletAddress));
        if (address == null)
            throw new ArgumentNullException(nameof(address));

        cancellationToken.ThrowIfCancellationRequested();

        if (!_wallets.TryGetValue(walletAddress, out var wallet))
        {
            throw new InvalidOperationException($"Wallet {walletAddress} not found");
        }

        var addressList = _addresses.GetOrAdd(walletAddress, _ => new List<WalletAddress>());
        addressList.Add(address);

        // Also add to wallet's collection
        wallet.Addresses.Add(address);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IEnumerable<WalletAddress>> GetAddressesAsync(
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new ArgumentException("Wallet address cannot be empty", nameof(walletAddress));

        cancellationToken.ThrowIfCancellationRequested();

        if (_addresses.TryGetValue(walletAddress, out var addresses))
        {
            return Task.FromResult<IEnumerable<WalletAddress>>(addresses.Select(CloneAddress).ToList());
        }

        return Task.FromResult<IEnumerable<WalletAddress>>(Array.Empty<WalletAddress>());
    }

    /// <inheritdoc/>
    public Task AddAccessAsync(
        string walletAddress,
        WalletAccess access,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new ArgumentException("Wallet address cannot be empty", nameof(walletAddress));
        if (access == null)
            throw new ArgumentNullException(nameof(access));

        cancellationToken.ThrowIfCancellationRequested();

        if (!_wallets.TryGetValue(walletAddress, out var wallet))
        {
            throw new InvalidOperationException($"Wallet {walletAddress} not found");
        }

        var accessList = _accessGrants.GetOrAdd(walletAddress, _ => new List<WalletAccess>());
        accessList.Add(access);

        // Also add to wallet's collection
        wallet.Delegates.Add(access);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<WalletAccess?> GetAccessByIdAsync(Guid accessId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var access = _accessGrants.Values
            .SelectMany(list => list)
            .FirstOrDefault(a => a.Id == accessId);

        return Task.FromResult(access != null ? CloneAccess(access) : null);
    }

    /// <inheritdoc/>
    public Task UpdateAccessAsync(WalletAccess access, CancellationToken cancellationToken = default)
    {
        if (access == null)
            throw new ArgumentNullException(nameof(access));

        cancellationToken.ThrowIfCancellationRequested();

        if (!_accessGrants.TryGetValue(access.ParentWalletAddress, out var accessList))
        {
            throw new InvalidOperationException($"No access grants found for wallet {access.ParentWalletAddress}");
        }

        var existing = accessList.FirstOrDefault(a => a.Subject == access.Subject);
        if (existing != null)
        {
            accessList.Remove(existing);
        }

        accessList.Add(access);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IEnumerable<WalletAccess>> GetAccessAsync(
        string walletAddress,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new ArgumentException("Wallet address cannot be empty", nameof(walletAddress));

        cancellationToken.ThrowIfCancellationRequested();

        if (_accessGrants.TryGetValue(walletAddress, out var grants))
        {
            var query = grants.AsEnumerable();

            if (activeOnly)
            {
                query = query.Where(g => g.IsActive);
            }

            return Task.FromResult<IEnumerable<WalletAccess>>(query.Select(CloneAccess).ToList());
        }

        return Task.FromResult<IEnumerable<WalletAccess>>(Array.Empty<WalletAccess>());
    }

    /// <inheritdoc/>
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // No-op for in-memory implementation
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Clears all data (for testing).
    /// </summary>
    public void Clear()
    {
        _wallets.Clear();
        _addresses.Clear();
        _accessGrants.Clear();
    }

    /// <summary>
    /// Gets count of wallets (for testing).
    /// </summary>
    public int Count => _wallets.Count;

    private static WalletEntity CloneWallet(WalletEntity wallet)
    {
        return new WalletEntity
        {
            Address = wallet.Address,
            EncryptedPrivateKey = wallet.EncryptedPrivateKey,
            EncryptionKeyId = wallet.EncryptionKeyId,
            Algorithm = wallet.Algorithm,
            Owner = wallet.Owner,
            Tenant = wallet.Tenant,
            Name = wallet.Name,
            Description = wallet.Description,
            PublicKey = wallet.PublicKey,
            Metadata = new Dictionary<string, string>(wallet.Metadata),
            Tags = wallet.Tags != null ? new Dictionary<string, string>(wallet.Tags) : null,
            Status = wallet.Status,
            CreatedAt = wallet.CreatedAt,
            UpdatedAt = wallet.UpdatedAt,
            LastAccessedAt = wallet.LastAccessedAt,
            DeletedAt = wallet.DeletedAt,
            Version = wallet.Version,
            RowVersion = wallet.RowVersion,
            Addresses = new List<WalletAddress>(),
            Delegates = new List<WalletAccess>(),
            Transactions = new List<WalletTransaction>()
        };
    }

    private static WalletAddress CloneAddress(WalletAddress address)
    {
        return new WalletAddress
        {
            Id = address.Id,
            ParentWalletAddress = address.ParentWalletAddress,
            Address = address.Address,
            PublicKey = address.PublicKey,
            DerivationPath = address.DerivationPath,
            Index = address.Index,
            Account = address.Account,
            IsChange = address.IsChange,
            Label = address.Label,
            Notes = address.Notes,
            Tags = address.Tags,
            IsUsed = address.IsUsed,
            CreatedAt = address.CreatedAt,
            FirstUsedAt = address.FirstUsedAt,
            LastUsedAt = address.LastUsedAt,
            Metadata = address.Metadata != null ? new Dictionary<string, string>(address.Metadata) : new Dictionary<string, string>()
        };
    }

    private static WalletAccess CloneAccess(WalletAccess access)
    {
        return new WalletAccess
        {
            ParentWalletAddress = access.ParentWalletAddress,
            Subject = access.Subject,
            AccessRight = access.AccessRight,
            GrantedBy = access.GrantedBy,
            GrantedAt = access.GrantedAt,
            ExpiresAt = access.ExpiresAt,
            RevokedAt = access.RevokedAt,
            RevokedBy = access.RevokedBy
        };
    }
}
