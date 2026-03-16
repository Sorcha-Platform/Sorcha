// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Core.Repositories;

/// <summary>
/// EF Core implementation of wallet repository using PostgreSQL
/// </summary>
public class EfCoreWalletRepository : IWalletRepository
{
    private readonly WalletDbContext _context;
    private readonly ILogger<EfCoreWalletRepository> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreWalletRepository"/> class.
    /// Provides data access for wallets, addresses, access control, and transactions using Entity Framework Core and PostgreSQL.
    /// </summary>
    /// <param name="context">The database context for wallet persistence.</param>
    /// <param name="logger">Logger for repository operations and diagnostics.</param>
    public EfCoreWalletRepository(WalletDbContext context, ILogger<EfCoreWalletRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task AddAsync(WalletEntity wallet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wallet);

        _logger.LogDebug("Adding wallet with address {Address} for owner {Owner}",
            wallet.Address, wallet.Owner);

        await _context.Wallets.AddAsync(wallet, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Added wallet {Address} for owner {Owner} in tenant {Tenant}",
            wallet.Address, wallet.Owner, wallet.Tenant);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(WalletEntity wallet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wallet);

        _logger.LogDebug("Updating wallet {Address}", wallet.Address);

        wallet.UpdatedAt = DateTime.UtcNow;
        _context.Wallets.Update(wallet);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated wallet {Address}", wallet.Address);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string address, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        _logger.LogDebug("Soft-deleting wallet {Address}", address);

        // Soft delete by setting DeletedAt
        var wallet = await _context.Wallets
            .IgnoreQueryFilters() // Include already soft-deleted for idempotency
            .FirstOrDefaultAsync(w => w.Address == address, cancellationToken);

        if (wallet != null)
        {
            wallet.DeletedAt = DateTime.UtcNow;
            wallet.UpdatedAt = DateTime.UtcNow;
            wallet.Status = Domain.WalletStatus.Deleted;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Soft-deleted wallet {Address}", address);
        }
        else
        {
            _logger.LogWarning("Wallet {Address} not found for deletion", address);
        }
    }

    /// <inheritdoc />
    public async Task<WalletEntity?> GetByAddressAsync(
        string address,
        bool includeAddresses = false,
        bool includeDelegates = false,
        bool includeTransactions = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        _logger.LogDebug("Getting wallet {Address} (addresses={IncludeAddresses}, delegates={IncludeDelegates}, transactions={IncludeTransactions})",
            address, includeAddresses, includeDelegates, includeTransactions);

        IQueryable<WalletEntity> query = _context.Wallets;

        if (includeAddresses)
        {
            query = query.Include(w => w.Addresses);
        }

        if (includeDelegates)
        {
            query = query.Include(w => w.Delegates);
        }

        if (includeTransactions)
        {
            query = query.Include(w => w.Transactions.OrderByDescending(t => t.CreatedAt));
        }

        var wallet = await query
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Address == address, cancellationToken);

        if (wallet != null)
        {
            // Update last accessed time in a separate operation to avoid tracking issues
            await UpdateLastAccessedAsync(address, cancellationToken);
        }

        return wallet;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<WalletEntity>> GetByOwnerAsync(
        string owner,
        string tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);

        _logger.LogDebug("Getting wallets for owner {Owner} in tenant {Tenant}", owner, tenant);

        var wallets = await _context.Wallets
            .AsNoTracking()
            .Where(w => w.Owner == owner && w.Tenant == tenant)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        _logger.LogDebug("Found {Count} wallets for owner {Owner}", wallets.Count, owner);

        return wallets;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<WalletEntity>> GetByTenantAsync(
        string tenant,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);

        _logger.LogDebug("Getting wallets for tenant {Tenant} (skip={Skip}, take={Take})",
            tenant, skip, take);

        var wallets = await _context.Wallets
            .AsNoTracking()
            .Where(w => w.Tenant == tenant)
            .OrderByDescending(w => w.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return wallets;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<WalletEntity>> GetAllPagedAsync(
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting all wallets across tenants (skip={Skip}, take={Take})", skip, take);

        var wallets = await _context.Wallets
            .AsNoTracking()
            .OrderByDescending(w => w.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return wallets;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string address, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        return await _context.Wallets
            .AnyAsync(w => w.Address == address, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAddressAsync(
        string walletAddress,
        WalletAddress address,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);
        ArgumentNullException.ThrowIfNull(address);

        _logger.LogDebug("Adding derived address {Address} to wallet {WalletAddress}",
            address.Address, walletAddress);

        // Ensure the parent wallet address is set correctly
        address.ParentWalletAddress = walletAddress;

        await _context.WalletAddresses.AddAsync(address, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Added derived address {Address} (path: {Path}) to wallet {WalletAddress}",
            address.Address, address.DerivationPath, walletAddress);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<WalletAddress>> GetAddressesAsync(
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        var addresses = await _context.WalletAddresses
            .AsNoTracking()
            .Where(a => a.ParentWalletAddress == walletAddress)
            .OrderBy(a => a.Account)
            .ThenBy(a => a.IsChange)
            .ThenBy(a => a.Index)
            .ToListAsync(cancellationToken);

        return addresses;
    }

    /// <inheritdoc />
    public async Task AddAccessAsync(
        string walletAddress,
        WalletAccess access,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);
        ArgumentNullException.ThrowIfNull(access);

        _logger.LogDebug("Adding access for subject {Subject} to wallet {WalletAddress}",
            access.Subject, walletAddress);

        // Ensure the parent wallet address is set correctly
        access.ParentWalletAddress = walletAddress;

        await _context.WalletAccess.AddAsync(access, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Added {AccessRight} access for subject {Subject} to wallet {WalletAddress}",
            access.AccessRight, access.Subject, walletAddress);
    }

    /// <inheritdoc />
    public async Task<WalletAccess?> GetAccessByIdAsync(Guid accessId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting access grant by ID {AccessId}", accessId);

        return await _context.WalletAccess
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accessId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAccessAsync(WalletAccess access, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);

        _logger.LogDebug("Updating access {Id} for wallet {WalletAddress}",
            access.Id, access.ParentWalletAddress);

        _context.WalletAccess.Update(access);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated access {Id} for wallet {WalletAddress}",
            access.Id, access.ParentWalletAddress);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<WalletAccess>> GetAccessAsync(
        string walletAddress,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        var query = _context.WalletAccess
            .AsNoTracking()
            .Where(a => a.ParentWalletAddress == walletAddress);

        if (activeOnly)
        {
            var now = DateTime.UtcNow;
            query = query.Where(a =>
                a.RevokedAt == null &&
                (a.ExpiresAt == null || a.ExpiresAt > now));
        }

        var access = await query
            .OrderBy(a => a.GrantedAt)
            .ToListAsync(cancellationToken);

        return access;
    }

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Updates the last accessed timestamp for a wallet
    /// </summary>
    private async Task UpdateLastAccessedAsync(string address, CancellationToken cancellationToken)
    {
        try
        {
            // Use ExecuteUpdateAsync for efficient single-column update without loading entity
            await _context.Wallets
                .Where(w => w.Address == address)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(w => w.LastAccessedAt, DateTime.UtcNow),
                    cancellationToken);
        }
        catch (Exception ex)
        {
            // Log but don't fail the main operation
            _logger.LogWarning(ex, "Failed to update last accessed time for wallet {Address}", address);
        }
    }
}
