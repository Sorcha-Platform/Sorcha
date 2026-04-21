// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models.Enums;

/// <summary>
/// Typed lifecycle for a register's sync state on this installation (Feature 108).
/// Replaces the legacy free-text string that lived on <see cref="Register.SyncState"/>.
/// </summary>
/// <remarks>
/// Derived from local docket height, recent peer-advert high-water-mark, and advert freshness.
/// Transitions are governed by <c>IRegisterSyncStateResolver</c>.
/// </remarks>
public enum RegisterSyncState
{
    /// <summary>
    /// No recent peer evidence and no local authoritative claim — cannot determine health.
    /// Entered at startup before first advert, or when all adverts exceed the staleness window.
    /// </summary>
    Indeterminate = 0,

    /// <summary>
    /// Local docket height is strictly less than the network high-water-mark.
    /// Actively catching up via pull replication.
    /// </summary>
    Syncing = 1,

    /// <summary>
    /// Local docket height matches the high-water-mark with sufficient advert confidence.
    /// </summary>
    CaughtUp = 2,

    /// <summary>
    /// Pull pipeline has failed repeatedly; sync cannot proceed until operator intervention.
    /// </summary>
    Error = 3
}
