// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Core.Events;

/// <summary>
/// Well-known Redis pub/sub channel names for register-related events.
/// Use these constants instead of inline string literals when publishing or subscribing.
/// </summary>
public static class RegisterEventChannels
{
    /// <summary>
    /// Published when a transaction has been confirmed and stored. Payload: <see cref="TransactionConfirmedEvent"/>.
    /// </summary>
    public const string TransactionConfirmed = "transaction:confirmed";

    /// <summary>
    /// Published when a register row is created locally — including the create-on-sync path when a
    /// register first replicates to this node (PR #829). Payload: <see cref="RegisterCreatedEvent"/>.
    /// Feature 137 (C2): Blueprint.Service subscribes to recover the new register's published
    /// blueprints immediately, rather than waiting for the next periodic recovery pass.
    /// </summary>
    public const string RegisterCreated = "register:created";

    /// <summary>
    /// Published when a docket has been sealed and written. Payload: <see cref="DocketConfirmedEvent"/>.
    /// </summary>
    public const string DocketConfirmed = "docket:confirmed";

    /// <summary>
    /// Published when a register's height advances. Payload: <see cref="RegisterHeightUpdatedEvent"/>.
    /// </summary>
    public const string RegisterHeightUpdated = "register:height-updated";

    /// <summary>
    /// Published when a register's sync state transitions. Payload: <see cref="RegisterSyncStateChangedEvent"/>.
    /// </summary>
    public const string RegisterSyncStateChanged = "register:sync-state-changed";

    /// <summary>
    /// Feature 108. Published when this node's derived role set for a register changes
    /// (control-transaction seal causing an add/remove from owner / admin / validator / etc.).
    /// Subscribers (notably Validator.Service) refresh their derived state on receipt.
    /// Payload: <see cref="RegisterRelationshipChangedEvent"/>.
    /// </summary>
    public const string RegisterRelationshipChanged = "register:relationship-changed";
}
