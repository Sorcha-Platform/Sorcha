// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Exceptions;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;

namespace Sorcha.Wallet.Core.Services.Implementation;

/// <summary>
/// Implementation of delegation service for access control management
/// </summary>
public class DelegationService : IDelegationService
{
    private readonly IWalletRepository _repository;
    private readonly ILogger<DelegationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegationService"/> class.
    /// Manages wallet access control including granting, revoking, and checking permissions for wallet operations.
    /// </summary>
    /// <param name="repository">The wallet repository for data access.</param>
    /// <param name="logger">Logger for delegation operations and audit trails.</param>
    public DelegationService(
        IWalletRepository repository,
        ILogger<DelegationService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<WalletAccess> GrantAccessAsync(
        string walletAddress,
        string subject,
        AccessRight accessRight,
        string grantedBy,
        string? reason = null,
        DateTime? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new ArgumentException("Wallet address cannot be empty", nameof(walletAddress));
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject cannot be empty", nameof(subject));
        if (string.IsNullOrWhiteSpace(grantedBy))
            throw new ArgumentException("Granted by cannot be empty", nameof(grantedBy));

        try
        {
            // Check if wallet exists
            var wallet = await _repository.GetByAddressAsync(walletAddress, cancellationToken: cancellationToken);
            if (wallet == null)
                throw new WalletNotFoundException($"Wallet not found: {walletAddress}");

            // Check if access already exists
            var existingAccess = await _repository.GetAccessAsync(walletAddress, false, cancellationToken);
            var existing = existingAccess.FirstOrDefault(a => a.Subject == subject && a.IsActive);

            if (existing != null)
            {
                _logger.LogWarning("Access already exists for subject {Subject} on wallet {WalletAddress}",
                    subject, walletAddress);
                throw new WalletAccessAlreadyExistsException($"Active access already exists for subject: {subject}");
            }

            var access = new WalletAccess
            {
                ParentWalletAddress = walletAddress,
                Subject = subject,
                AccessRight = accessRight,
                GrantedBy = grantedBy,
                Reason = reason,
                GrantedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt
            };

            await _repository.AddAccessAsync(walletAddress, access, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Granted {AccessRight} access to subject {Subject} on wallet {WalletAddress}",
                accessRight, subject, walletAddress);

            return access;
        }
        catch (Exception ex) when (ex is not WalletNotFoundException and not WalletAccessAlreadyExistsException)
        {
            _logger.LogError(ex, "Failed to grant access to subject {Subject} on wallet {WalletAddress}",
                subject, walletAddress);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task RevokeAccessAsync(
        string walletAddress,
        string subject,
        string revokedBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new ArgumentException("Wallet address cannot be empty", nameof(walletAddress));
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject cannot be empty", nameof(subject));
        if (string.IsNullOrWhiteSpace(revokedBy))
            throw new ArgumentException("Revoked by cannot be empty", nameof(revokedBy));

        try
        {
            var existingAccess = await _repository.GetAccessAsync(walletAddress, true, cancellationToken);
            var access = existingAccess.FirstOrDefault(a => a.Subject == subject);

            if (access == null)
            {
                _logger.LogWarning("No active access found for subject {Subject} on wallet {WalletAddress}",
                    subject, walletAddress);
                throw new WalletNotFoundException($"No active access found for subject: {subject}");
            }

            access.RevokedAt = DateTime.UtcNow;
            access.RevokedBy = revokedBy;

            await _repository.UpdateAccessAsync(access, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Revoked access for subject {Subject} on wallet {WalletAddress}",
                subject, walletAddress);
        }
        catch (Exception ex) when (ex is not WalletNotFoundException)
        {
            _logger.LogError(ex, "Failed to revoke access for subject {Subject} on wallet {WalletAddress}",
                subject, walletAddress);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<WalletAccess>> GetActiveAccessAsync(
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new ArgumentException("Wallet address cannot be empty", nameof(walletAddress));

        try
        {
            var access = await _repository.GetAccessAsync(walletAddress, true, cancellationToken);
            return access;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active access for wallet {WalletAddress}", walletAddress);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> HasAccessAsync(
        string walletAddress,
        string subject,
        AccessRight requiredRight,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new ArgumentException("Wallet address cannot be empty", nameof(walletAddress));
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject cannot be empty", nameof(subject));

        try
        {
            var wallet = await _repository.GetByAddressAsync(walletAddress, includeDelegates: true, cancellationToken: cancellationToken);
            if (wallet == null)
                return false;

            // Owner has all rights
            if (wallet.Owner == subject)
                return true;

            // Check delegated access
            var access = wallet.Delegates.FirstOrDefault(a => a.Subject == subject && a.IsActive);
            if (access == null)
                return false;

            // Check if the access right is sufficient
            return access.AccessRight switch
            {
                AccessRight.Owner => true,
                AccessRight.ReadWrite => requiredRight != AccessRight.Owner,
                AccessRight.ReadOnly => requiredRight == AccessRight.ReadOnly,
                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check access for subject {Subject} on wallet {WalletAddress}",
                subject, walletAddress);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<WalletAccess> UpdateAccessAsync(
        Guid accessId,
        AccessRight? accessRight = null,
        DateTime? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var access = await _repository.GetAccessByIdAsync(accessId, cancellationToken);
            if (access == null)
            {
                throw new WalletNotFoundException($"Access grant not found: {accessId}");
            }

            if (!access.IsActive)
            {
                throw new InvalidOperationException($"Access grant {accessId} is no longer active");
            }

            if (accessRight.HasValue)
            {
                access.AccessRight = accessRight.Value;
            }

            if (expiresAt.HasValue)
            {
                access.ExpiresAt = expiresAt.Value;
            }

            await _repository.UpdateAccessAsync(access, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated access {AccessId} on wallet {WalletAddress}",
                accessId, access.ParentWalletAddress);

            return access;
        }
        catch (Exception ex) when (ex is not InvalidOperationException and not WalletNotFoundException)
        {
            _logger.LogError(ex, "Failed to update access {AccessId}", accessId);
            throw;
        }
    }
}
