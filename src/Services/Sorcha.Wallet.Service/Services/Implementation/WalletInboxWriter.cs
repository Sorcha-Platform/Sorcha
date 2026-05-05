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

    private static Guid DeterministicSourceEventId(string recipientWalletAddress, string credentialId)
    {
        var input = $"sorcha.inbox.credential-received:{recipientWalletAddress}:{credentialId}";
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}
