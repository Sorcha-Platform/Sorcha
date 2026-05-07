// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Hubs;

/// <summary>
/// Typed client interface for <see cref="BlueprintHub"/>. Every method conforms
/// to the Feature 118 thin-signal contract — opaque IDs and timestamps only.
/// Clients fetch full detail through authenticated REST endpoints referenced
/// in each method's <c>&lt;see cref&gt;</c> doc.
/// </summary>
/// <remarks>
/// Encryption events live on <see cref="Sorcha.Wallet.Service.Hubs.IWalletHubClient"/>
/// — the wallet-domain hub became their canonical home in the encryption-pipeline
/// migration. Blueprint Service publishes encryption progress via the
/// <c>encryption:events</c> Redis channel; <c>EncryptionEventBridge</c> in
/// Wallet Service subscribes and emits on WalletHub.
/// </remarks>
public interface IBlueprintHubClient
{
    /// <summary>
    /// A new action is available for the recipient wallet. Carries opaque IDs
    /// only; clients fetch the full action via
    /// <c>GET /api/instances/{instanceId}/actions/{actionId}</c>.
    /// </summary>
    /// <param name="instanceId">Blueprint instance the action belongs to.</param>
    /// <param name="actionId">Action identifier within the instance.</param>
    /// <param name="occurredAt">Server timestamp at which the signal was emitted.</param>
    /// <param name="traceId">W3C trace-id for correlation.</param>
    Task ActionAvailable(string instanceId, string actionId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// An action was rejected by the validation pipeline. Carries opaque IDs only;
    /// clients fetch the rejection detail via
    /// <c>GET /api/instances/{instanceId}/actions/{actionId}</c>.
    /// </summary>
    /// <param name="instanceId">Blueprint instance the action belonged to.</param>
    /// <param name="actionId">Action identifier within the instance.</param>
    /// <param name="occurredAt">Server timestamp at which the signal was emitted.</param>
    /// <param name="traceId">W3C trace-id for correlation.</param>
    Task ActionRejected(string instanceId, string actionId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A workflow instance reached a terminal state. Clients fetch instance
    /// detail via <c>GET /api/instances/{instanceId}</c>.
    /// </summary>
    /// <param name="instanceId">Blueprint instance that completed.</param>
    /// <param name="occurredAt">Server timestamp at which the signal was emitted.</param>
    /// <param name="traceId">W3C trace-id for correlation.</param>
    Task WorkflowCompleted(string instanceId, DateTimeOffset occurredAt, string traceId);
}
