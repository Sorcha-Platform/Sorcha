// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models.Observations;

namespace Sorcha.Register.Core.Observations;

/// <summary>
/// In-memory store of peer-height and validator-sealing observations used by
/// <c>IRegisterSyncStateResolver</c> to derive per-register sync state (Feature 108).
/// </summary>
/// <remarks>
/// Observations are ephemeral operational signals, not authoritative state — the store is
/// intentionally not persisted. Peer observations are upserted per (<c>RegisterId</c>,
/// <c>SourcePeerId</c>) so each distinct peer occupies one slot per register rather than
/// a history. Validator observations overwrite a single slot per register.
/// </remarks>
public interface IObservationStore
{
    /// <summary>
    /// Record a peer's height claim for a register. Replaces any previous claim from the same peer.
    /// </summary>
    void RecordPeerHeight(PeerHeightObservation observation);

    /// <summary>
    /// Record the local validator's sealing progress for a register. Overwrites any previous value.
    /// </summary>
    void RecordValidatorSealing(ValidatorSealingObservation observation);

    /// <summary>
    /// Return peer-height observations for the register whose <c>ObservedAt</c> is within the
    /// staleness window, most-recent first.
    /// </summary>
    IReadOnlyList<PeerHeightObservation> GetRecentPeerHeights(string registerId, TimeSpan stalenessWindow);

    /// <summary>
    /// Return the latest validator-sealing observation for the register, or <c>null</c> if none recorded.
    /// </summary>
    ValidatorSealingObservation? GetLatestValidatorSealing(string registerId);

    /// <summary>
    /// Return the set of register IDs that have at least one observation stored.
    /// Used by the pruner to decide what to sweep.
    /// </summary>
    IReadOnlyCollection<string> GetTrackedRegisterIds();

    /// <summary>
    /// Remove all observations for the given register. Used by the pruner and on register delete.
    /// </summary>
    void Evict(string registerId);
}
