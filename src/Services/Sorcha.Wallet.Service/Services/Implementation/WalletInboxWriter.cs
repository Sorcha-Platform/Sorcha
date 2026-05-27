// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Inbox;
using Sorcha.ServiceClients.Participant;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Phase 5 follow-up #2 of Feature 118 — bridges Wallet Service credential
/// issuance events to the durable user inbox owned by Tenant Service.
/// Symmetric to <c>BlueprintInboxWriter</c> in Blueprint Service.
/// </summary>
/// <remarks>
/// <para>
/// Resolves the recipient wallet through the participant service to a
/// <c>UserIdentity.Id</c>, then through Tenant's
/// <c>/api/internal/users/by-identity/{id}</c> to the cross-org
/// <c>PlatformUserId</c> the inbox is keyed on. Posts an inbox entry via
/// <see cref="IPlatformInboxClient"/>. Fail-safe: every step short-circuits
/// silently on <c>null</c>, and inbox-write exceptions are caught so
/// credential issuance is never affected by an inbox-write failure.
/// </para>
/// <para>
/// Idempotency is keyed on <c>(PlatformUserId, SourceEventId)</c> at Tenant.
/// The <c>SourceEventId</c> is built deterministically from
/// <c>(recipientWallet, credentialId)</c> so duplicate-issuance retries (or
/// the rare double-fire path) collapse to the same row.
/// </para>
/// </remarks>
public interface IWalletInboxWriter
{
    /// <summary>Write a "you received a new credential" inbox entry for the recipient wallet's owning user.</summary>
    Task WriteCredentialReceivedAsync(
        string recipientWalletAddress,
        string credentialId,
        string credentialType,
        string? issuerOrgName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Write a "credential declined" inbox entry. Fires when the holder
    /// explicitly rejects a credential they were offered (Feature 106 holder
    /// PATCH path → Declined). Severity is Info — the action was deliberate.
    /// </summary>
    Task WriteCredentialDeclinedAsync(
        string walletAddress,
        string credentialId,
        string credentialType,
        CancellationToken ct = default);

    /// <summary>
    /// Write a "credential deleted" inbox entry. Fires when the holder removes
    /// a credential from their wallet. Severity is Warning — the action is
    /// destructive and the user may want a durable record of when it happened.
    /// </summary>
    Task WriteCredentialDeletedAsync(
        string walletAddress,
        string credentialId,
        string credentialType,
        CancellationToken ct = default);

    /// <summary>
    /// Write a "presentation submitted" inbox entry. Fires when the holder
    /// responds to a verifier's request by sharing a credential. Severity is
    /// Info — gives the holder a durable trail of who they've shared what with.
    /// </summary>
    Task WritePresentationSubmittedAsync(
        string walletAddress,
        string credentialId,
        string credentialType,
        string presentationRequestId,
        string? verifierIdentity = null,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class WalletInboxWriter : IWalletInboxWriter
{
    private readonly IParticipantServiceClient _participants;
    private readonly IPlatformInboxClient _inbox;
    private readonly ILogger<WalletInboxWriter> _logger;

    /// <summary>Initialises a new <see cref="WalletInboxWriter"/>.</summary>
    public WalletInboxWriter(
        IParticipantServiceClient participants,
        IPlatformInboxClient inbox,
        ILogger<WalletInboxWriter> logger)
    {
        _participants = participants ?? throw new ArgumentNullException(nameof(participants));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task WriteCredentialReceivedAsync(
        string recipientWalletAddress,
        string credentialId,
        string credentialType,
        string? issuerOrgName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recipientWalletAddress) ||
            string.IsNullOrWhiteSpace(credentialId) ||
            string.IsNullOrWhiteSpace(credentialType))
        {
            return;
        }

        try
        {
            var participant = await _participants.GetByWalletAddressAsync(recipientWalletAddress, ct).ConfigureAwait(false);
            if (participant is null)
            {
                _logger.LogDebug(
                    "Inbox skip — no participant for recipient wallet {Wallet}",
                    recipientWalletAddress);
                return;
            }

            var platformUserId = await _inbox.ResolvePlatformUserIdAsync(participant.UserId, ct).ConfigureAwait(false);
            if (platformUserId is null)
            {
                _logger.LogDebug(
                    "Inbox skip — could not resolve PlatformUserId for UserIdentity {UserIdentityId}",
                    participant.UserId);
                return;
            }

            var sourceEventId = DeterministicSourceEventId(recipientWalletAddress, credentialId);
            var displayTitle = string.IsNullOrWhiteSpace(issuerOrgName)
                ? $"New credential: {credentialType}"
                : $"{issuerOrgName} issued you a {credentialType}";

            var payload = new InboxWritePayload(
                PlatformUserId: platformUserId.Value,
                Category: "Credential",
                Severity: "Info",
                CorrelationKey: $"credential:{recipientWalletAddress}:{credentialId}",
                DetailHref: $"/api/v1/wallets/{recipientWalletAddress}/credentials/{credentialId}",
                SourceEventId: sourceEventId,
                OccurredAt: DateTimeOffset.UtcNow,
                Title: displayTitle,
                Summary: null,
                IconKey: "credential.received");

            var outcome = await _inbox.WriteAsync(payload, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Inbox entry {Outcome} for credential received — RecipientWallet={Wallet} CredentialId={CredentialId} Type={Type} EntryId={EntryId}",
                outcome.Idempotent ? "idempotent" : "created",
                recipientWalletAddress, credentialId, credentialType, outcome.EntryId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Inbox-write failed for credential received — RecipientWallet={Wallet} CredentialId={CredentialId}",
                recipientWalletAddress, credentialId);
        }
    }

    /// <inheritdoc />
    public Task WriteCredentialDeclinedAsync(
        string walletAddress, string credentialId, string credentialType, CancellationToken ct = default)
        => WriteCredentialEventAsync(
            walletAddress: walletAddress,
            credentialId: credentialId,
            credentialType: credentialType,
            severity: "Info",
            sourceTag: "credential-declined",
            correlationKey: $"credential:{walletAddress}:{credentialId}:declined",
            detailHref: $"/api/v1/wallets/{walletAddress}/credentials/{credentialId}",
            titleBuilder: t => $"Declined credential: {t}",
            summary: "You chose not to accept this credential.",
            iconKey: "credential.declined",
            ct: ct);

    /// <inheritdoc />
    public Task WriteCredentialDeletedAsync(
        string walletAddress, string credentialId, string credentialType, CancellationToken ct = default)
        => WriteCredentialEventAsync(
            walletAddress: walletAddress,
            credentialId: credentialId,
            credentialType: credentialType,
            severity: "Warning",
            sourceTag: "credential-deleted",
            correlationKey: $"credential:{walletAddress}:{credentialId}:deleted",
            // The credential resource is gone; point to the wallet's credential
            // listing so the user can confirm their remaining credentials.
            detailHref: $"/api/v1/wallets/{walletAddress}/credentials",
            titleBuilder: t => $"Deleted credential: {t}",
            summary: "If this wasn't you, restore from a backup immediately.",
            iconKey: "credential.deleted",
            ct: ct);

    /// <inheritdoc />
    public Task WritePresentationSubmittedAsync(
        string walletAddress,
        string credentialId,
        string credentialType,
        string presentationRequestId,
        string? verifierIdentity = null,
        CancellationToken ct = default)
        => WriteCredentialEventAsync(
            walletAddress: walletAddress,
            credentialId: credentialId,
            credentialType: credentialType,
            severity: "Info",
            sourceTag: $"presentation-submitted:{presentationRequestId}",
            correlationKey: $"presentation:{walletAddress}:{presentationRequestId}",
            // Link to the credential used in the presentation so the holder can
            // see what they shared. The presentation-request resource is owned
            // by the verifier, not the wallet, so it's the wrong destination.
            detailHref: $"/api/v1/wallets/{walletAddress}/credentials/{credentialId}",
            titleBuilder: _ => string.IsNullOrWhiteSpace(verifierIdentity)
                ? $"Shared credential: {credentialType}"
                : $"Shared {credentialType} with {verifierIdentity}",
            summary: string.IsNullOrWhiteSpace(verifierIdentity)
                ? null
                : $"Presented to {verifierIdentity}. You can review which claims were disclosed in the credential detail.",
            iconKey: "credential.presented",
            ct: ct);

    /// <summary>
    /// Shared helper for credential-keyed inbox entries (declined, deleted,
    /// presentation-submitted). Resolves the wallet's owning user via the
    /// participant service, then writes through <see cref="IPlatformInboxClient"/>
    /// with the supplied per-event copy. Same fail-safe semantics as
    /// <see cref="WriteCredentialReceivedAsync"/>: empty inputs short-circuit,
    /// missing participant or PlatformUserId logs + skips, exceptions are
    /// caught.
    /// </summary>
    private async Task WriteCredentialEventAsync(
        string walletAddress,
        string credentialId,
        string credentialType,
        string severity,
        string sourceTag,
        string correlationKey,
        string detailHref,
        Func<string, string> titleBuilder,
        string? summary,
        string iconKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(walletAddress) ||
            string.IsNullOrWhiteSpace(credentialId) ||
            string.IsNullOrWhiteSpace(credentialType))
        {
            return;
        }

        try
        {
            var participant = await _participants.GetByWalletAddressAsync(walletAddress, ct).ConfigureAwait(false);
            if (participant is null)
            {
                _logger.LogDebug(
                    "Inbox skip — no participant for wallet {Wallet} on {SourceTag}",
                    walletAddress, sourceTag);
                return;
            }

            var platformUserId = await _inbox.ResolvePlatformUserIdAsync(participant.UserId, ct).ConfigureAwait(false);
            if (platformUserId is null)
            {
                _logger.LogDebug(
                    "Inbox skip — could not resolve PlatformUserId for UserIdentity {UserIdentityId} on {SourceTag}",
                    participant.UserId, sourceTag);
                return;
            }

            var sourceEventId = DeterministicSourceEventIdFromInput($"sorcha.inbox.{sourceTag}:{walletAddress}:{credentialId}");

            var payload = new InboxWritePayload(
                PlatformUserId: platformUserId.Value,
                Category: "Credential",
                Severity: severity,
                CorrelationKey: correlationKey,
                DetailHref: detailHref,
                SourceEventId: sourceEventId,
                OccurredAt: DateTimeOffset.UtcNow,
                Title: titleBuilder(credentialType),
                Summary: summary,
                IconKey: iconKey);

            var outcome = await _inbox.WriteAsync(payload, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Inbox entry {Outcome} for {SourceTag} — Wallet={Wallet} CredentialId={CredentialId} EntryId={EntryId}",
                outcome.Idempotent ? "idempotent" : "created",
                sourceTag, walletAddress, credentialId, outcome.EntryId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Inbox-write failed for {SourceTag} — Wallet={Wallet} CredentialId={CredentialId}",
                sourceTag, walletAddress, credentialId);
        }
    }

    private static Guid DeterministicSourceEventId(string recipientWalletAddress, string credentialId)
        => DeterministicSourceEventIdFromInput($"sorcha.inbox.credential-received:{recipientWalletAddress}:{credentialId}");

    private static Guid DeterministicSourceEventIdFromInput(string input)
    {
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}
