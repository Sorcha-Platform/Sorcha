// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Inbox;
using Sorcha.ServiceClients.Participant;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Phase 5 follow-up of Feature 118 — bridges Blueprint Service workflow events
/// to the durable user inbox owned by Tenant Service.
/// </summary>
/// <remarks>
/// <para>
/// Resolves a wallet address through the participant service to a
/// <c>UserIdentity.Id</c>, then through Tenant's
/// <c>/api/internal/users/by-identity/{id}</c> to the cross-org
/// <c>PlatformUserId</c> the inbox is keyed on. Posts an inbox entry via
/// <see cref="IPlatformInboxClient"/>. Fail-safe: every step short-circuits
/// silently on <c>null</c> so an inbox-write failure cannot break the
/// originating notification path.
/// </para>
/// <para>
/// Idempotency on the inbox side is keyed on
/// <c>(PlatformUserId, SourceEventId)</c>. The <see cref="ActionAvailableInboxEntry"/>
/// helper builds a deterministic <c>SourceEventId</c> from
/// <c>(walletAddress, instanceId, actionId)</c> so retries within a workflow
/// collapse to the same inbox row.
/// </para>
/// </remarks>
public interface IBlueprintInboxWriter
{
    /// <summary>Write a "you have an action available" inbox entry for the wallet's owning user.</summary>
    Task WriteActionAvailableAsync(
        string walletAddress,
        string instanceId,
        string actionId,
        string? actionTitle = null,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class BlueprintInboxWriter : IBlueprintInboxWriter
{
    private readonly IParticipantServiceClient _participants;
    private readonly IPlatformInboxClient _inbox;
    private readonly ILogger<BlueprintInboxWriter> _logger;

    /// <summary>Initialises a new <see cref="BlueprintInboxWriter"/>.</summary>
    public BlueprintInboxWriter(
        IParticipantServiceClient participants,
        IPlatformInboxClient inbox,
        ILogger<BlueprintInboxWriter> logger)
    {
        _participants = participants ?? throw new ArgumentNullException(nameof(participants));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task WriteActionAvailableAsync(
        string walletAddress,
        string instanceId,
        string actionId,
        string? actionTitle = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(walletAddress) ||
            string.IsNullOrWhiteSpace(instanceId) ||
            string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        try
        {
            var participant = await _participants.GetByWalletAddressAsync(walletAddress, ct).ConfigureAwait(false);
            if (participant is null)
            {
                _logger.LogDebug(
                    "Inbox skip — no participant for wallet {Wallet}",
                    walletAddress);
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

            var sourceEventId = DeterministicSourceEventId(walletAddress, instanceId, actionId);
            var payload = new InboxWritePayload(
                PlatformUserId: platformUserId.Value,
                Category: "Action",
                Severity: "ActionRequired",
                CorrelationKey: $"action:{instanceId}:{actionId}",
                DetailHref: $"/api/instances/{instanceId}/actions/{actionId}",
                SourceEventId: sourceEventId,
                OccurredAt: DateTimeOffset.UtcNow,
                Title: string.IsNullOrWhiteSpace(actionTitle) ? "Action required" : actionTitle!,
                Summary: null,
                IconKey: "action.available");

            var outcome = await _inbox.WriteAsync(payload, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Inbox entry {Outcome} for action available — Wallet={Wallet} Instance={InstanceId} Action={ActionId} EntryId={EntryId}",
                outcome.Idempotent ? "idempotent" : "created",
                walletAddress, instanceId, actionId, outcome.EntryId);
        }
        catch (Exception ex)
        {
            // Inbox-write failures must not break the SignalR notification path.
            _logger.LogWarning(ex,
                "Inbox-write failed for action available — Wallet={Wallet} Instance={InstanceId} Action={ActionId}",
                walletAddress, instanceId, actionId);
        }
    }

    /// <summary>
    /// Builds a deterministic GUID from <c>(walletAddress, instanceId, actionId)</c> so
    /// duplicate writes for the same workflow event collapse on Tenant's idempotency
    /// unique index. Uses GUID v5-style namespacing — stable across restarts.
    /// </summary>
    private static Guid DeterministicSourceEventId(string walletAddress, string instanceId, string actionId)
    {
        // Namespace GUID is the SHA1 of the seed bytes truncated to 16 bytes — the
        // standard RFC 4122 v5 derivation. We reuse the .NET HashData helper for
        // brevity; the exact byte order need not match RFC 4122 because we never
        // claim cryptographic security from the GUID, only stability.
        var input = $"sorcha.inbox.action-available:{walletAddress}:{instanceId}:{actionId}";
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        // Set version (5) and variant bits per RFC 4122 to keep the value a valid GUID.
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}
