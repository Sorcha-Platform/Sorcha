// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.Hubs;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Default implementation of <see cref="ICitizenInboxProjector"/>.
/// </summary>
/// <remarks>
/// <para>
/// Resolves the recipient via <see cref="IHolderAddressLookup"/>; if the wallet
/// address is not a citizen holder the call returns early without touching the
/// event log or the hub — the org-credential pipeline is preserved unchanged.
/// </para>
/// <para>
/// <c>Seq</c> is allocated as <c>MAX(Seq) + 1</c> for the citizen and persisted
/// via the unique <c>(PlatformUserId, Seq)</c> index. Concurrent writes for the
/// same citizen race on the index — losers retry once. This is cheaper than
/// holding a SERIALIZABLE transaction and matches the existing pattern in the
/// rest of the wallet service.
/// </para>
/// <para>
/// Hub emission is best-effort: a SignalR backplane failure is logged but does
/// not break the upstream credential write. The thin-signal contract means the
/// wallet always re-syncs via REST on next open, so a missed push is a UX
/// optimisation regression, not a correctness one.
/// </para>
/// </remarks>
public sealed class CitizenInboxProjector : ICitizenInboxProjector
{
    private const int SeqRetryAttempts = 3;

    private readonly WalletDbContext _db;
    private readonly IHolderAddressLookup _holderLookup;
    private readonly IWalletRepository _walletRepository;
    private readonly IHubContext<WalletHub, IWalletHubClient> _hub;
    private readonly ILogger<CitizenInboxProjector> _logger;

    /// <summary>Initialises a new instance.</summary>
    public CitizenInboxProjector(
        WalletDbContext db,
        IHolderAddressLookup holderLookup,
        IWalletRepository walletRepository,
        IHubContext<WalletHub, IWalletHubClient> hub,
        ILogger<CitizenInboxProjector> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _holderLookup = holderLookup ?? throw new ArgumentNullException(nameof(holderLookup));
        _walletRepository = walletRepository ?? throw new ArgumentNullException(nameof(walletRepository));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task OnCredentialAddedAsync(CredentialEntity credential, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        var platformUserId = await _holderLookup.ResolvePlatformUserIdAsync(credential.WalletAddress, ct);
        if (platformUserId is null)
        {
            // Fast-path lookup missed. F114's PWA enrolment endpoint is the canonical
            // population point for CitizenHolderIndex, but walkthrough automation and
            // any flow that creates a citizen wallet without going through the device-
            // enrolment ceremony will hit this branch — and silently dropping the event
            // means GET /api/v1/wallet/credentials returns [] even though the credential
            // is on disk (the symptom diagnosed live on n1 after PR #871).
            //
            // Discriminator for "this is a citizen-holder pattern, not an org wallet":
            //   SubjectDid == WalletAddress — the credential's subject is the wallet's
            //   own holder. The issuer-side ledger row stored on the analyst's wallet
            //   has SubjectDid pointing at the recipient, so it does NOT match — that
            //   row stays excluded, exactly as before. Only the recipient-side row
            //   (Subject = Wallet) triggers the lazy population path.
            if (!string.Equals(credential.SubjectDid, credential.WalletAddress, StringComparison.Ordinal))
            {
                return;
            }

            var wallet = await _walletRepository.GetByAddressAsync(
                credential.WalletAddress, cancellationToken: ct);
            if (wallet is null || !Guid.TryParse(wallet.Owner, out var owner))
            {
                // Owner not a parseable platform-user GUID (or wallet not found) — bail.
                return;
            }

            // Lazily populate the index so subsequent reads hit the fast path. RegisterAsync
            // is idempotent on (WalletAddress) and tolerates concurrent first-write races.
            await _holderLookup.RegisterAsync(credential.WalletAddress, owner, ct);
            platformUserId = owner;
            _logger.LogInformation(
                "CitizenHolderIndex lazily populated for wallet {Wallet} → platformUserId {PlatformUserId} on first inbound credential",
                credential.WalletAddress, owner);
        }

        await AppendEventAsync(platformUserId.Value, CitizenCredentialEventKindValues.Added, credential.Id, ct);
        await EmitCredentialAvailableAsync(platformUserId.Value, credential.Id, ct);
    }

    /// <inheritdoc />
    public async Task OnCredentialStatusChangedAsync(
        CredentialEntity credential,
        CredentialStatus previousStatus,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        // Wallet PWA only surfaces "the credential just became unusable" transitions;
        // intermediate states (Active → Suspended) are not part of the citizen sync
        // contract today.
        var newStatus = credential.Status;
        var isRevoked = newStatus is CredentialStatus.Revoked
                                  or CredentialStatus.Declined;
        if (!isRevoked || previousStatus == newStatus)
        {
            return;
        }

        var platformUserId = await _holderLookup.ResolvePlatformUserIdAsync(credential.WalletAddress, ct);
        if (platformUserId is null)
        {
            return;
        }

        await AppendEventAsync(platformUserId.Value, CitizenCredentialEventKindValues.Revoked, credential.Id, ct);
        await EmitCredentialAvailableAsync(platformUserId.Value, credential.Id, ct);
    }

    private async Task AppendEventAsync(Guid platformUserId, int kind, string credentialId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < SeqRetryAttempts; attempt++)
        {
            var nextSeq = await _db.CitizenCredentialEventLog
                .Where(e => e.PlatformUserId == platformUserId)
                .Select(e => (long?)e.Seq)
                .MaxAsync(ct) ?? 0L;
            nextSeq++;

            _db.CitizenCredentialEventLog.Add(new CitizenCredentialEventLog
            {
                PlatformUserId = platformUserId,
                Seq = nextSeq,
                Kind = kind,
                CredentialId = credentialId,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            try
            {
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "CitizenCredentialEventLog appended: platformUserId={PlatformUserId} seq={Seq} kind={Kind} credentialId={CredentialId}",
                    platformUserId, nextSeq, kind, credentialId);
                return;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex) && attempt + 1 < SeqRetryAttempts)
            {
                // Lost a Seq race with a concurrent write — recompute MAX(Seq) and retry.
                _db.ChangeTracker.Clear();
                _logger.LogDebug(
                    "CitizenCredentialEventLog seq race for {PlatformUserId} (attempt {Attempt}) — retrying",
                    platformUserId, attempt + 1);
            }
        }

        // All retries exhausted — surface as exception so the upstream caller can decide.
        // The credential write has already succeeded; the pull-on-open path will still
        // surface the credential, so this is a degraded-but-recoverable state.
        throw new InvalidOperationException(
            $"Failed to append CitizenCredentialEventLog for platformUser {platformUserId} after {SeqRetryAttempts} attempts.");
    }

    private async Task EmitCredentialAvailableAsync(Guid platformUserId, string credentialId, CancellationToken ct)
    {
        try
        {
            await _hub.Clients
                .Group(WalletHub.GroupNameFor(platformUserId))
                .CredentialAvailable(credentialId);

            _logger.LogDebug(
                "WalletHub.CredentialAvailable emitted: platformUserId={PlatformUserId} credentialId={CredentialId}",
                platformUserId, credentialId);
        }
        catch (Exception ex)
        {
            // Hub emission is an optimisation, not the source of truth. A failed push
            // becomes a missed badge update; the next /sync call still surfaces the
            // credential. Log loudly so operators notice but do NOT propagate.
            _logger.LogError(ex,
                "WalletHub.CredentialAvailable emit failed for platformUserId={PlatformUserId} credentialId={CredentialId} — wallet will pick up on next sync",
                platformUserId, credentialId);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("23505", StringComparison.Ordinal)
            || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
            || message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Numeric values matching <c>Sorcha.Wallet.Service.Services.Interfaces.CitizenCredentialEventKind</c>.
/// Persisted as <c>integer</c> in <c>CitizenCredentialEventLog.Kind</c>.
/// </summary>
internal static class CitizenCredentialEventKindValues
{
    public const int Added = 0;
    public const int Revoked = 1;
    public const int Replaced = 2;
}
