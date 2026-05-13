// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Participants;

namespace Sorcha.UI.Core.Services.Participants;

/// <summary>
/// Service for publishing participant records to registers.
/// </summary>
public interface IParticipantPublishingService
{
    /// <summary>
    /// Publishes a participant record to a register.
    /// </summary>
    Task<ParticipantPublishResultViewModel> PublishAsync(
        Guid organizationId,
        PublishParticipantViewModel request,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a previously published participant record.
    /// </summary>
    Task<ParticipantPublishResultViewModel> UpdatePublishedAsync(
        Guid organizationId,
        Guid participantId,
        PublishParticipantViewModel request,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes a published participant record.
    /// </summary>
    Task<bool> RevokeAsync(
        Guid organizationId,
        Guid participantId,
        CancellationToken ct = default);
}
