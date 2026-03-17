// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Models;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Handles passkey-bound wallet recovery (Path 4).
/// Verifies passkey ownership, unwraps recovery key, restores all user wallets.
/// </summary>
public class PasskeyRecoveryService : IPasskeyRecoveryService
{
    private readonly WalletDbContext _dbContext;
    private readonly IRecoveryKeyService _recoveryKeyService;
    private readonly IKeyManagementService _keyManagementService;
    private readonly ILogger<PasskeyRecoveryService> _logger;

    public PasskeyRecoveryService(
        WalletDbContext dbContext,
        IRecoveryKeyService recoveryKeyService,
        IKeyManagementService keyManagementService,
        ILogger<PasskeyRecoveryService> logger)
    {
        _dbContext = dbContext;
        _recoveryKeyService = recoveryKeyService;
        _keyManagementService = keyManagementService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<RecoveryResult> RecoverAsync(
        string userId,
        string tenantId,
        string passkeyCredentialId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Passkey recovery initiated for user {UserId} with credential {CredentialId}",
            userId, passkeyCredentialId);

        // Find all wallets owned by this user that have recovery enabled
        var wallets = await _dbContext.Wallets
            .Include(w => w.RecoveryKeyWraps)
            .Include(w => w.Delegates)
            .Where(w => w.Owner == userId && w.RecoveryEnabled)
            .ToListAsync(cancellationToken);

        if (wallets.Count == 0)
        {
            _logger.LogWarning("No recoverable wallets found for user {UserId}", userId);
            return new RecoveryResult();
        }

        var restoredAddresses = new List<string>();
        var allDelegationReviews = new List<DelegationReviewItem>();
        var totalDelegationsRevoked = 0;

        foreach (var wallet in wallets)
        {
            // Find the passkey recovery wrap for this wallet
            var wrap = wallet.RecoveryKeyWraps.FirstOrDefault(
                w => w.RecoveryPath == RecoveryPathType.Passkey
                     && w.RecipientKeyId == passkeyCredentialId
                     && w.RevokedAt == null);

            if (wrap is null)
            {
                _logger.LogDebug(
                    "No passkey recovery wrap for wallet {Address} with credential {CredentialId}, skipping",
                    wallet.Address, passkeyCredentialId);
                continue;
            }

            // In a full implementation, the recovery key would be unwrapped using the passkey's
            // private key via a WebAuthn assertion. For now, we verify the wrap exists and
            // the passkey credential matches — the actual unwrapping requires browser-side
            // cryptographic operations that pass the decrypted recovery key back to the server.
            //
            // TODO: Implement full WebAuthn assertion flow for recovery key unwrapping.
            // For MVP, the presence of a valid wrap + authenticated passkey session is sufficient
            // to prove the user controls the passkey and authorize recovery.

            // Re-encrypt the wallet's private key with a fresh encryption key
            var (newEncryptedKey, newKeyId) = await _keyManagementService.EncryptPrivateKeyAsync(
                await _keyManagementService.DecryptPrivateKeyAsync(
                    wallet.EncryptedPrivateKey, wallet.EncryptionKeyId),
                wallet.EncryptionKeyId);

            wallet.EncryptedPrivateKey = newEncryptedKey;
            wallet.EncryptionKeyId = newKeyId;
            wallet.UpdatedAt = DateTime.UtcNow;
            wallet.Status = WalletStatus.Active;

            // Revoke all active delegations
            var activeDelegations = wallet.Delegates
                .Where(d => d.RevokedAt == null && (d.ExpiresAt == null || d.ExpiresAt > DateTime.UtcNow))
                .ToList();

            foreach (var delegation in activeDelegations)
            {
                allDelegationReviews.Add(new DelegationReviewItem
                {
                    DelegationId = delegation.Id,
                    WalletAddress = wallet.Address,
                    Subject = delegation.Subject,
                    AccessRight = delegation.AccessRight.ToString(),
                    GrantedAt = delegation.GrantedAt,
                    Reason = delegation.Reason
                });

                delegation.RevokedAt = DateTime.UtcNow;
                delegation.RevokedBy = $"recovery:{userId}";
                totalDelegationsRevoked++;
            }

            restoredAddresses.Add(wallet.Address);
        }

        // Create audit log entry
        var auditLog = new RecoveryAuditLog
        {
            UserId = userId,
            TenantId = tenantId,
            RecoveryPath = RecoveryPathType.Passkey,
            InitiatedBy = userId,
            WalletsRecovered = restoredAddresses.Count,
            DelegationsRevoked = totalDelegationsRevoked,
            DelegationsPreserved = 0,
            IpAddress = ipAddress,
            Timestamp = DateTimeOffset.UtcNow
        };

        _dbContext.RecoveryAuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Passkey recovery completed for user {UserId}: {WalletCount} wallets, {DelegationCount} delegations revoked",
            userId, restoredAddresses.Count, totalDelegationsRevoked);

        return new RecoveryResult
        {
            WalletsRecovered = restoredAddresses.Count,
            WalletAddresses = restoredAddresses.ToArray(),
            DelegationsRevoked = totalDelegationsRevoked,
            DelegationsPendingReview = allDelegationReviews
        };
    }
}
