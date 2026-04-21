// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models.Enums;

namespace Sorcha.Register.Models.LocalRelationship;

/// <summary>
/// Operator-facing view of a register's sync state and the evidence that produced it
/// (Feature 108). Returned by <c>GET /api/registers/{registerId}/sync-state</c>.
/// </summary>
/// <param name="RegisterId">The register described.</param>
/// <param name="State">The derived sync state.</param>
/// <param name="LocalHeight">Latest docket height stored locally.</param>
/// <param name="NetworkHeightHighWaterMark">Highest height claimed by any peer within the staleness window; null when no recent adverts.</param>
/// <param name="DistinctPeerObservers">Count of distinct peers with observations inside the staleness window.</param>
/// <param name="LastAdvertAt">Timestamp of the freshest observation; null when none are fresh.</param>
/// <param name="SinglePeerMode">True when only one peer has been observed — sync confidence is reduced.</param>
/// <param name="LastErrorMessage">Populated when <see cref="State"/> is <see cref="RegisterSyncState.Error"/>.</param>
/// <param name="ValidatorSnapshot">Populated when the local node is the validator for this register.</param>
public sealed record RegisterSyncStateView(
    string RegisterId,
    RegisterSyncState State,
    long LocalHeight,
    long? NetworkHeightHighWaterMark,
    int DistinctPeerObservers,
    DateTimeOffset? LastAdvertAt,
    bool SinglePeerMode,
    string? LastErrorMessage,
    ValidatorSealingSnapshot? ValidatorSnapshot);

/// <summary>
/// Point-in-time snapshot of local-validator sealing progress for a register.
/// </summary>
/// <param name="LastSealedHeight">Latest docket height this validator has sealed locally.</param>
/// <param name="MempoolDepth">Current unverified-pool depth for this register.</param>
/// <param name="ObservedAt">When the snapshot was observed by the validator.</param>
public sealed record ValidatorSealingSnapshot(
    long LastSealedHeight,
    int MempoolDepth,
    DateTimeOffset ObservedAt);
