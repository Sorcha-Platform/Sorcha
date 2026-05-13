// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Multi-node audit CRITICAL #2 — applies issuer-driven credential lifecycle
/// transitions (Revoke / Suspend / Reinstate) to the holder's locally cached
/// credential row when a <c>TransactionType.CredentialStatusChange</c> register
/// transaction arrives via the inbound notification pipeline.
/// </summary>
/// <remarks>
/// Called from <c>NotificationDeliveryService.DeliverAsync</c> alongside the
/// Feature 106 credential detector. The implementation MUST never throw —
/// failure cases return <see cref="InboundCredentialStatusResult.Skipped"/> so a
/// malformed status-change tx never breaks notification delivery.
/// </remarks>
public interface IInboundCredentialStatusHandler
{
    /// <summary>
    /// Inspect the inbound transaction and apply a credential status change if
    /// it is a valid <c>CredentialStatusChange</c> tx addressed to the supplied
    /// wallet. Returns a result indicating whether the row was updated.
    /// </summary>
    Task<InboundCredentialStatusResult> TryApplyAsync(
        string walletAddress,
        string transactionId,
        string registerId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of <see cref="IInboundCredentialStatusHandler.TryApplyAsync"/>.
/// </summary>
public sealed record InboundCredentialStatusResult
{
    /// <summary>True when the local credential row was updated.</summary>
    public required bool Applied { get; init; }

    /// <summary>Credential whose status was updated (when <see cref="Applied"/> is true).</summary>
    public string? CredentialId { get; init; }

    /// <summary>New status applied to the local row.</summary>
    public string? NewStatus { get; init; }

    /// <summary>Sentinel result for the not-applicable path.</summary>
    public static InboundCredentialStatusResult Skipped { get; } = new() { Applied = false };
}
