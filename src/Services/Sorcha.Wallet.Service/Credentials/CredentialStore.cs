// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Entities;

namespace Sorcha.Wallet.Service.Credentials;

/// <summary>
/// EF Core-backed credential store for wallet verifiable credentials.
/// </summary>
public class CredentialStore : ICredentialStore
{
    private static readonly Dictionary<CredentialStatus, HashSet<CredentialStatus>> ValidTransitions = new()
    {
        [CredentialStatus.Active] = new() { CredentialStatus.Suspended, CredentialStatus.Revoked, CredentialStatus.Consumed, CredentialStatus.Expired },
        [CredentialStatus.Suspended] = new() { CredentialStatus.Active, CredentialStatus.Revoked },
        // Feature 106: inbound register-native credentials start in PendingAcceptance and
        // transition to Active (holder accept), Declined (holder decline), or Expired (lazy).
        [CredentialStatus.PendingAcceptance] = new() { CredentialStatus.Active, CredentialStatus.Declined, CredentialStatus.Expired },
    };

    private readonly WalletDbContext _db;
    private readonly ILogger<CredentialStore> _logger;

    public CredentialStore(WalletDbContext db, ILogger<CredentialStore> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CredentialEntity>> GetByWalletAsync(
        string walletAddress, CancellationToken ct = default)
    {
        var credentials = await _db.Credentials
            .Where(c => c.WalletAddress == walletAddress)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        await ExpireStaleCredentialsAsync(credentials, ct);

        return credentials;
    }

    /// <inheritdoc />
    public async Task<CredentialEntity?> GetByIdAsync(string credentialId, CancellationToken ct = default)
    {
        var credential = await _db.Credentials
            .FirstOrDefaultAsync(c => c.Id == credentialId, ct);

        if (credential != null)
        {
            await ExpireStaleCredentialsAsync([credential], ct);
        }

        return credential;
    }

    /// <inheritdoc />
    public async Task StoreAsync(CredentialEntity credential, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        var existing = await _db.Credentials
            .FirstOrDefaultAsync(c => c.Id == credential.Id, ct);

        if (existing != null)
        {
            _db.Entry(existing).CurrentValues.SetValues(credential);
            _logger.LogInformation("Credential updated: {CredentialId} Type={Type} Wallet={Wallet}",
                credential.Id, credential.Type, credential.WalletAddress);
        }
        else
        {
            _db.Credentials.Add(credential);
            _logger.LogInformation("Credential stored: {CredentialId} Type={Type} Issuer={Issuer} Wallet={Wallet}",
                credential.Id, credential.Type, credential.IssuerDid, credential.WalletAddress);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string credentialId, CancellationToken ct = default)
    {
        var credential = await _db.Credentials
            .FirstOrDefaultAsync(c => c.Id == credentialId, ct);

        if (credential == null)
            return false;

        _db.Credentials.Remove(credential);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateStatusAsync(string credentialId, CredentialStatus status, CancellationToken ct = default)
    {
        var credential = await _db.Credentials
            .FirstOrDefaultAsync(c => c.Id == credentialId, ct);

        if (credential == null)
            return false;

        if (!IsValidTransition(credential.Status, status))
        {
            _logger.LogWarning("Invalid status transition for {CredentialId}: {CurrentStatus} -> {TargetStatus}",
                credentialId, credential.Status, status);
            return false;
        }

        var previousStatus = credential.Status;
        credential.Status = status;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Credential status changed: {CredentialId} {PreviousStatus} -> {NewStatus}",
            credentialId, previousStatus, status);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CredentialEntity>> MatchAsync(
        string walletAddress,
        string? type = null,
        IEnumerable<string>? acceptedIssuers = null,
        CancellationToken ct = default)
    {
        var query = _db.Credentials
            .Where(c => c.WalletAddress == walletAddress && c.Status == CredentialStatus.Active);

        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(c => c.Type == type);
        }

        var issuerList = acceptedIssuers?.ToList();
        if (issuerList is { Count: > 0 })
        {
            query = query.Where(c => issuerList.Contains(c.IssuerDid));
        }

        // Exclude expired credentials
        var now = DateTimeOffset.UtcNow;
        query = query.Where(c => c.ExpiresAt == null || c.ExpiresAt > now);

        return await query
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> RecordPresentationAsync(string credentialId, CancellationToken ct = default)
    {
        var credential = await _db.Credentials
            .FirstOrDefaultAsync(c => c.Id == credentialId, ct);

        if (credential == null || credential.Status != CredentialStatus.Active)
            return false;

        credential.PresentationCount++;

        bool consumed = credential.UsagePolicy switch
        {
            "SingleUse" => true,
            "LimitedUse" => credential.MaxPresentations.HasValue
                            && credential.PresentationCount >= credential.MaxPresentations.Value,
            _ => false,
        };

        if (consumed)
        {
            credential.Status = CredentialStatus.Consumed;
            _logger.LogInformation("Credential consumed after presentation: {CredentialId} Policy={UsagePolicy} Count={Count}",
                credentialId, credential.UsagePolicy, credential.PresentationCount);
        }
        else
        {
            _logger.LogInformation("Credential presented: {CredentialId} Count={Count}/{Max}",
                credentialId, credential.PresentationCount,
                credential.MaxPresentations?.ToString() ?? "unlimited");
        }

        await _db.SaveChangesAsync(ct);
        return consumed;
    }

    private static bool IsValidTransition(CredentialStatus currentStatus, CredentialStatus targetStatus)
    {
        if (currentStatus == targetStatus)
        {
            // Idempotent writes are permitted as no-ops (data-model.md §2 INV-2 discussion).
            return true;
        }

        if (ValidTransitions.TryGetValue(currentStatus, out var allowed))
        {
            return allowed.Contains(targetStatus);
        }

        return false;
    }

    /// <summary>
    /// Detects credentials that are Active but past their expiry date,
    /// and lazily transitions them to Expired.
    /// </summary>
    private async Task ExpireStaleCredentialsAsync(
        IReadOnlyList<CredentialEntity> credentials, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        bool changed = false;

        foreach (var credential in credentials)
        {
            if ((credential.Status == CredentialStatus.Active || credential.Status == CredentialStatus.PendingAcceptance)
                && credential.ExpiresAt.HasValue
                && credential.ExpiresAt.Value < now)
            {
                credential.Status = CredentialStatus.Expired;
                changed = true;
            }
        }

        if (changed)
        {
            await _db.SaveChangesAsync(ct);
        }
    }
}
