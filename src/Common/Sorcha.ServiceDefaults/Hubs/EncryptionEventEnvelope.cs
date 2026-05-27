// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceDefaults.Hubs;

/// <summary>
/// Cross-service envelope for encryption-pipeline events. Blueprint Service
/// publishes these to the Redis <see cref="ChannelName"/> channel; Wallet
/// Service subscribes and re-emits them via the WalletHub typed client.
/// </summary>
/// <remarks>
/// <para>
/// The encryption pipeline lives in Blueprint Service today, but the wire-level
/// home for encryption signals is the wallet-domain hub per the Feature 118
/// contract. Direct emit via <c>IHubContext</c> can't cross service boundaries,
/// so the in-process emit path was replaced by Redis pub/sub: NotificationService
/// publishes; <c>EncryptionEventBridge</c> in Wallet Service subscribes.
/// </para>
/// <para>
/// On-the-wire payload conforms to the thin-signal contract — the bridge
/// reduces the envelope to <c>(operationId, occurredAt, traceId)</c> when calling
/// the typed client method, never forwarding the kind or wallet address to
/// browser clients.
/// </para>
/// </remarks>
/// <param name="WalletAddress">Wallet group target (e.g. <c>WalletHubGroups.Wallet(walletAddress)</c>).</param>
/// <param name="OperationId">Encryption operation identifier.</param>
/// <param name="Kind">Discriminator: "progress", "complete", or "failed".</param>
/// <param name="OccurredAt">Server timestamp at which the event was published.</param>
/// <param name="TraceId">W3C trace-id for correlation across the bridge.</param>
public sealed record EncryptionEventEnvelope(
    string WalletAddress,
    string OperationId,
    string Kind,
    DateTimeOffset OccurredAt,
    string TraceId)
{
    /// <summary>Redis pub/sub channel name. Stable wire contract.</summary>
    public const string ChannelName = "encryption:events";

    /// <summary>Discriminator value for in-progress events.</summary>
    public const string KindProgress = "progress";

    /// <summary>Discriminator value for successful completion events.</summary>
    public const string KindComplete = "complete";

    /// <summary>Discriminator value for failure events.</summary>
    public const string KindFailed = "failed";
}
