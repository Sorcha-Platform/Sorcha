// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Services.Interfaces;

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
    private readonly ICitizenInboxProjector _citizenProjector;
    private readonly ILogger<CredentialStore> _logger;

    /// <summary>Initialises a new instance.</summary>
    public CredentialStore(
        WalletDbContext db,
        ICitizenInboxProjector citizenProjector,
        ILogger<CredentialStore> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _citizenProjector = citizenProjector ?? throw new ArgumentNullException(nameof(citizenProjector));
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
        var matches = await _db.Credentials
            .Where(c => c.Id == credentialId)
            .ToListAsync(ct);

        if (matches.Count == 0)
            return null;

        // Feature 106: issuer and recipient can each hold a row with the same credential id
        // on single-node deployments. When that happens, prefer the Active copy so verification
        // sees a valid credential rather than the recipient's PendingAcceptance row. Callers
        // that know the wallet address should use GetByIdForWalletAsync instead.
        if (matches.Count > 1)
        {
            _logger.LogWarning(
                "GetByIdAsync: {Count} rows share credential id {CredentialId} across multiple wallets — " +
                "use GetByIdForWalletAsync when wallet context is available",
                matches.Count, credentialId);
        }

        var credential = matches.Count == 1
            ? matches[0]
            : matches.FirstOrDefault(c => c.Status == CredentialStatus.Active) ?? matches[0];

        await ExpireStaleCredentialsAsync([credential], ct);
        return credential;
    }

    /// <inheritdoc />
    public async Task<CredentialEntity?> GetByIdForWalletAsync(
        string credentialId, string walletAddress, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialId);
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        var credential = await _db.Credentials
            .FirstOrDefaultAsync(
                c => c.Id == credentialId && c.WalletAddress == walletAddress, ct);

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

        // Feature 106 fix — use composite (Id, WalletAddress) lookup. Two rows with
        // the same credential id are legitimate when the issuer audit row and the
        // recipient PendingAcceptance row live in the same DB (single-node dev or
        // co-located issuer/recipient wallets). Prior Id-only lookup would SetValues
        // over the issuer's row when storing the recipient's copy — silently
        // corrupting the audit trail.
        var existing = await _db.Credentials
            .FirstOrDefaultAsync(
                c => c.Id == credential.Id && c.WalletAddress == credential.WalletAddress,
                ct);

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
    public async Task<bool> DeleteAsync(string credentialId, string walletAddress, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        var credential = await _db.Credentials
            .FirstOrDefaultAsync(c => c.Id == credentialId && c.WalletAddress == walletAddress, ct);

        if (credential == null)
            return false;

        _db.Credentials.Remove(credential);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateStatusAsync(
        string credentialId, string walletAddress, CredentialStatus status, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        var credential = await _db.Credentials
            .FirstOrDefaultAsync(c => c.Id == credentialId && c.WalletAddress == walletAddress, ct);

        if (credential == null)
            return false;

        if (!IsValidTransition(credential.Status, status))
        {
            _logger.LogWarning(
                "Invalid status transition for {CredentialId} wallet {WalletAddress}: {CurrentStatus} -> {TargetStatus}",
                credentialId, walletAddress, credential.Status, status);
            return false;
        }

        var previousStatus = credential.Status;
        credential.Status = status;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Credential status changed: {CredentialId} wallet {WalletAddress} {PreviousStatus} -> {NewStatus}",
            credentialId, walletAddress, previousStatus, status);

        // Feature 114 US4 — project status change onto citizen sync surface (no-op
        // when the recipient is not a citizen-PWA holder).
        await _citizenProjector.OnCredentialStatusChangedAsync(credential, previousStatus, ct);

        return true;
    }

    /// <inheritdoc />
    public async Task<CredentialEntity?> PatchStatusAsync(
        string walletAddress,
        string credentialId,
        CredentialStatus newStatus,
        CancellationToken ct = default)
    {
        // Wallet-scoped lookup — credential IDs are not globally unique when a
        // credential exists on both the issuer's wallet (Active at issuance)
        // and the recipient's wallet (PendingAcceptance via
        // InboundCredentialDetector). Filtering by both keys at the EF query
        // is the only safe way to find the row a holder is operating on.
        var credential = await _db.Credentials
            .FirstOrDefaultAsync(
                c => c.Id == credentialId && c.WalletAddress == walletAddress, ct);

        if (credential is null)
            return null;

        // Idempotent no-op (data-model INV-2 discussion): patching to the same status
        // is a successful no-op, not an error.
        if (credential.Status == newStatus)
            return credential;

        if (!IsValidTransition(credential.Status, newStatus))
        {
            // Surface the exact transition so the 409 Conflict response can tell the
            // holder UI what went wrong (e.g. clicked Accept on an already-Declined row).
            throw new InvalidOperationException(
                $"invalid-transition: {credential.Status} → {newStatus}");
        }

        var previousStatus = credential.Status;
        credential.Status = newStatus;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Credential {CredentialId} status patched: {PreviousStatus} → {NewStatus}",
            credentialId, previousStatus, newStatus);

        // Feature 114 US4 — project status change onto citizen sync surface (no-op
        // when the recipient is not a citizen-PWA holder).
        await _citizenProjector.OnCredentialStatusChangedAsync(credential, previousStatus, ct);

        return credential;
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
        var matches = await _db.Credentials
            .Where(c => c.Id == credentialId)
            .ToListAsync(ct);

        // Feature 106: when issuer and recipient hold the same credential id, prefer the
        // Active copy so usage is recorded against a valid row. Log so operators are aware.
        if (matches.Count > 1)
        {
            _logger.LogWarning(
                "RecordPresentationAsync: {Count} rows share credential id {CredentialId} — " +
                "operating on first Active copy; use wallet-scoped access when wallet context is available",
                matches.Count, credentialId);
        }

        var credential = matches.Count == 1
            ? matches[0]
            : matches.FirstOrDefault(c => c.Status == CredentialStatus.Active);

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
