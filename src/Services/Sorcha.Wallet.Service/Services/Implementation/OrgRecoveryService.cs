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
/// Handles organization-managed wallet recovery (Path 3).
/// Admin authenticates, verifies org recovery key, restores member wallets.
/// </summary>
public class OrgRecoveryService : IOrgRecoveryService
{
    private readonly WalletDbContext _dbContext;
    private readonly IRecoveryKeyService _recoveryKeyService;
    private readonly IKeyManagementService _keyManagementService;
    private readonly ILogger<OrgRecoveryService> _logger;

    public OrgRecoveryService(
        WalletDbContext dbContext,
        IRecoveryKeyService recoveryKeyService,
        IKeyManagementService keyManagementService,
        ILogger<OrgRecoveryService> logger)
    {
        _dbContext = dbContext;
        _recoveryKeyService = recoveryKeyService;
        _keyManagementService = keyManagementService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<RecoveryResult> RecoverAsync(
        string adminUserId,
        string targetUserId,
        string tenantId,
        string orgRecoveryKeySignature,
        bool skipDelegationRevocation = false,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Org recovery initiated by admin {AdminUserId} for user {TargetUserId} in tenant {TenantId}",
            adminUserId, targetUserId, tenantId);

        // Find all wallets owned by the target user in this tenant
        var wallets = await _dbContext.Wallets
            .Include(w => w.RecoveryKeyWraps)
            .Include(w => w.Delegates)
            .Where(w => w.Owner == targetUserId && w.Tenant == tenantId && w.RecoveryEnabled)
            .ToListAsync(cancellationToken);

        if (wallets.Count == 0)
        {
            _logger.LogWarning("No recoverable wallets found for user {TargetUserId} in tenant {TenantId}",
                targetUserId, tenantId);
            return new RecoveryResult();
        }

        var restoredAddresses = new List<string>();
        var allDelegationReviews = new List<DelegationReviewItem>();
        var totalDelegationsRevoked = 0;

        foreach (var wallet in wallets)
        {
            // Find the org-managed recovery wrap
            var wrap = wallet.RecoveryKeyWraps.FirstOrDefault(
                w => w.RecoveryPath == RecoveryPathType.OrgManaged && w.RevokedAt == null);

            if (wrap is null)
            {
                _logger.LogDebug("No org recovery wrap for wallet {Address}, skipping", wallet.Address);
                continue;
            }

            // Review M3b: org-managed recovery requires verifying the org recovery-key signature against
            // the org's recovery public key — that verification is NOT implemented (the prior code re-keyed
            // on the admin's authenticated session alone). Fail loud rather than silently authorising
            // recovery without real cryptographic proof, so enabling Features:WalletRecoveryEnabled cannot
            // open this path with broken verification. Full org-signature verification is a backlog item.
            throw new NotSupportedException(
                "Org-managed wallet recovery requires org recovery-key signature verification, which is " +
                "not yet implemented. Keep Features:WalletRecoveryEnabled disabled until it is.");

#pragma warning disable CS0162 // Unreachable code — retained for the deferred wallet-recovery feature build.
            // Re-encrypt the wallet's private key
            var (newEncryptedKey, newKeyId) = await _keyManagementService.EncryptPrivateKeyAsync(
                await _keyManagementService.DecryptPrivateKeyAsync(
                    wallet.EncryptedPrivateKey, wallet.EncryptionKeyId),
                wallet.EncryptionKeyId);

            wallet.EncryptedPrivateKey = newEncryptedKey;
            wallet.EncryptionKeyId = newKeyId;
            wallet.UpdatedAt = DateTime.UtcNow;
            wallet.Status = WalletStatus.Active;

            // Revoke delegations unless admin opts out
            if (!skipDelegationRevocation)
            {
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
                    delegation.RevokedBy = $"org-recovery:{adminUserId}";
                    totalDelegationsRevoked++;
                }
            }

            restoredAddresses.Add(wallet.Address);
#pragma warning restore CS0162
        }

        // Audit log
        var auditLog = new RecoveryAuditLog
        {
            UserId = targetUserId,
            TenantId = tenantId,
            RecoveryPath = RecoveryPathType.OrgManaged,
            InitiatedBy = adminUserId,
            WalletsRecovered = restoredAddresses.Count,
            DelegationsRevoked = totalDelegationsRevoked,
            DelegationsPreserved = 0,
            IpAddress = ipAddress,
            Timestamp = DateTimeOffset.UtcNow
        };

        _dbContext.RecoveryAuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Org recovery completed for user {TargetUserId} by admin {AdminUserId}: {WalletCount} wallets",
            targetUserId, adminUserId, restoredAddresses.Count);

        return new RecoveryResult
        {
            WalletsRecovered = restoredAddresses.Count,
            WalletAddresses = restoredAddresses.ToArray(),
            DelegationsRevoked = totalDelegationsRevoked,
            DelegationsPendingReview = allDelegationReviews
        };
    }
}
