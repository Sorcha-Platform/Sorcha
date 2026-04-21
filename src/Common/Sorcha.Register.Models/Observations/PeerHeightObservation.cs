// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Register.Models.Observations;

/// <summary>
/// Observation pushed from Peer.Service into Register.Service each time a peer advert
/// is ingested (Feature 108). Upserted by <c>(RegisterId, SourcePeerId)</c> — each peer
/// occupies one slot per register, not a history. Feeds <c>RegisterSyncStateResolver</c>.
/// </summary>
/// <param name="RegisterId">Register being advertised. Must match the URL path.</param>
/// <param name="SourcePeerId">Identity of the advertising peer (typically gRPC hostname).</param>
/// <param name="NetworkHeight">Height the advertising peer claims for this register.</param>
/// <param name="ObservedAt">When Peer.Service received the advert. Validated within ±5 min of server clock.</param>
public sealed record PeerHeightObservation(
    [property: Required, StringLength(255, MinimumLength = 1)]
    string RegisterId,

    [property: Required, StringLength(255, MinimumLength = 1)]
    string SourcePeerId,

    [property: Range(0, long.MaxValue)]
    long NetworkHeight,

    DateTimeOffset ObservedAt);
