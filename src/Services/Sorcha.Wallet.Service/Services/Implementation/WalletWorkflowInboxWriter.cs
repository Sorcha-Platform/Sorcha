// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Inbox;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Phase 2 of the Snackbar retirement plan — bridges wallet-lifecycle events
/// (created, recovered, deleted, address registered) to the durable user
/// inbox owned by Tenant Service. Companion to
/// <see cref="IWalletInboxWriter"/> which covers credential events.
/// </summary>
/// <remarks>
/// <para>
/// These events currently surface only as transient toasts in the UI; routing
/// them through the inbox gives the wallet owner a durable record they can
/// review later from any device. Severity is <c>Info</c> for create/recover/
/// address; <c>Warning</c> for delete — the destructive operation warrants a
/// more prominent treatment in the bell drawer.
/// </para>
/// <para>
/// Fail-safe: every step short-circuits silently on <c>null</c> input or a
/// failed PlatformUserId resolution; inbox-write exceptions are caught so
/// wallet operations are never affected by an inbox-write failure. Matches
/// the established <see cref="WalletInboxWriter"/> contract.
/// </para>
/// </remarks>
public interface IWalletWorkflowInboxWriter
{
    /// <summary>Drop a "wallet created" entry for the owning user.</summary>
    Task WriteWalletCreatedAsync(
        string walletAddress,
        string walletName,
        Guid ownerUserIdentityId,
        CancellationToken ct = default);

    /// <summary>Drop a "wallet recovered" entry for the owning user.</summary>
    Task WriteWalletRecoveredAsync(
        string walletAddress,
        string walletName,
        Guid ownerUserIdentityId,
        CancellationToken ct = default);

    /// <summary>Drop a "wallet deleted" entry for the owning user. Severity is Warning.</summary>
    Task WriteWalletDeletedAsync(
        string walletAddress,
        string walletName,
        Guid ownerUserIdentityId,
        CancellationToken ct = default);

    /// <summary>Drop a "derived address registered" entry for the owning user.</summary>
    /// <param name="derivedAddress">The newly-registered BIP44 child address.</param>
    Task WriteAddressRegisteredAsync(
        string walletAddress,
        string derivedAddress,
        Guid ownerUserIdentityId,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class WalletWorkflowInboxWriter : IWalletWorkflowInboxWriter
{
    private readonly IPlatformInboxClient _inbox;
    private readonly ILogger<WalletWorkflowInboxWriter> _logger;

    /// <summary>Initialises a new <see cref="WalletWorkflowInboxWriter"/>.</summary>
    public WalletWorkflowInboxWriter(
        IPlatformInboxClient inbox,
        ILogger<WalletWorkflowInboxWriter> logger)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task WriteWalletCreatedAsync(
        string walletAddress, string walletName, Guid ownerUserIdentityId, CancellationToken ct = default)
        => WriteAsync(
            walletAddress: walletAddress,
            ownerUserIdentityId: ownerUserIdentityId,
            severity: "Info",
            sourceTag: "wallet-created",
            correlationKey: $"wallet:{walletAddress}",
            detailHref: $"/api/v1/wallets/{walletAddress}",
            title: BuildTitle("Wallet created", walletName),
            summary: $"Wallet address: {walletAddress}",
            iconKey: "wallet.created",
            ct: ct);

    /// <inheritdoc />
    public Task WriteWalletRecoveredAsync(
        string walletAddress, string walletName, Guid ownerUserIdentityId, CancellationToken ct = default)
        => WriteAsync(
            walletAddress: walletAddress,
            ownerUserIdentityId: ownerUserIdentityId,
            severity: "Info",
            sourceTag: "wallet-recovered",
            correlationKey: $"wallet:{walletAddress}:recovered",
            detailHref: $"/api/v1/wallets/{walletAddress}",
            title: BuildTitle("Wallet recovered", walletName),
            summary: "Recovered from mnemonic phrase. Review your wallet to confirm everything is in order.",
            iconKey: "wallet.recovered",
            ct: ct);

    /// <inheritdoc />
    public Task WriteWalletDeletedAsync(
        string walletAddress, string walletName, Guid ownerUserIdentityId, CancellationToken ct = default)
        => WriteAsync(
            walletAddress: walletAddress,
            ownerUserIdentityId: ownerUserIdentityId,
            severity: "Warning",
            sourceTag: "wallet-deleted",
            correlationKey: $"wallet:{walletAddress}:deleted",
            // The wallet itself is gone; navigating to its detail URL would 404.
            // Direct the user to the wallet listing instead so they can confirm
            // the deletion and see their remaining wallets.
            detailHref: "/api/v1/wallets",
            title: BuildTitle("Wallet deleted", walletName),
            summary: $"Wallet address: {walletAddress}. If this wasn't you, contact your administrator immediately.",
            iconKey: "wallet.deleted",
            ct: ct);

    /// <inheritdoc />
    public Task WriteAddressRegisteredAsync(
        string walletAddress, string derivedAddress, Guid ownerUserIdentityId, CancellationToken ct = default)
        => WriteAsync(
            walletAddress: walletAddress,
            ownerUserIdentityId: ownerUserIdentityId,
            severity: "Info",
            sourceTag: $"address-registered:{derivedAddress}",
            correlationKey: $"wallet:{walletAddress}:address-registered:{derivedAddress}",
            detailHref: $"/api/v1/wallets/{walletAddress}/addresses",
            title: "New derived address",
            summary: $"Address {derivedAddress} was registered under wallet {walletAddress}.",
            iconKey: "wallet.address.registered",
            ct: ct);

    private async Task WriteAsync(
        string walletAddress,
        Guid ownerUserIdentityId,
        string severity,
        string sourceTag,
        string correlationKey,
        string detailHref,
        string title,
        string? summary,
        string iconKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            return;
        }
        if (ownerUserIdentityId == Guid.Empty)
        {
            _logger.LogDebug(
                "Inbox skip — owner UserIdentity id is empty for {SourceTag} on wallet {Wallet}",
                sourceTag, walletAddress);
            return;
        }

        try
        {
            var platformUserId = await _inbox.ResolvePlatformUserIdAsync(ownerUserIdentityId, ct).ConfigureAwait(false);
            if (platformUserId is null)
            {
                _logger.LogDebug(
                    "Inbox skip — could not resolve PlatformUserId for UserIdentity {UserIdentityId} on {SourceTag}",
                    ownerUserIdentityId, sourceTag);
                return;
            }

            var sourceEventId = DeterministicSourceEventId($"sorcha.inbox.{sourceTag}:{walletAddress}");

            var payload = new InboxWritePayload(
                PlatformUserId: platformUserId.Value,
                Category: "Workflow",
                Severity: severity,
                CorrelationKey: correlationKey,
                DetailHref: detailHref,
                SourceEventId: sourceEventId,
                OccurredAt: DateTimeOffset.UtcNow,
                Title: title,
                Summary: summary,
                IconKey: iconKey);

            var outcome = await _inbox.WriteAsync(payload, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Inbox entry {Outcome} for {SourceTag} — Wallet={Wallet} EntryId={EntryId}",
                outcome.Idempotent ? "idempotent" : "created",
                sourceTag, walletAddress, outcome.EntryId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Inbox-write failed for {SourceTag} — Wallet={Wallet}",
                sourceTag, walletAddress);
        }
    }

    private static string BuildTitle(string action, string walletName)
        => string.IsNullOrWhiteSpace(walletName) ? action : $"{action}: {walletName}";

    /// <summary>
    /// Hash a stable input string to a deterministic Guid. Matches the format
    /// used by <see cref="WalletInboxWriter"/> so the (PlatformUserId, SourceEventId)
    /// idempotency key collapses retries to the same row.
    /// </summary>
    private static Guid DeterministicSourceEventId(string input)
    {
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}
