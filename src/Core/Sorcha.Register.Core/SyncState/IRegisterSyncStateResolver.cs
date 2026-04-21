// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models.Enums;
using Sorcha.Register.Models.LocalRelationship;
using Sorcha.Register.Models.Observations;

namespace Sorcha.Register.Core.SyncState;

/// <summary>
/// Pure-function sync state resolver (Feature 108). Composes local docket height, recent
/// peer-height observations, and validator sealing progress into a <see cref="RegisterSyncState"/>
/// + <see cref="RegisterSyncStateView"/>.
/// </summary>
public interface IRegisterSyncStateResolver
{
    /// <summary>Advert staleness window after which peer observations are ignored.</summary>
    TimeSpan StalenessWindow { get; }

    /// <summary>
    /// Resolve the current state and the inputs that produced it.
    /// </summary>
    RegisterSyncStateView Resolve(
        string registerId,
        uint localHeight,
        IReadOnlyList<PeerHeightObservation> observations,
        ValidatorSealingObservation? validatorSealing,
        RegisterSyncState? persistedState,
        string? lastErrorMessage);
}
