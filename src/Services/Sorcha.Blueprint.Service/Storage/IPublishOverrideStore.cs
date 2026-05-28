// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// Feature 142 (D4/FR-032) — append-only audit storage for
/// <see cref="PublishOverride"/> records written when an authorised user
/// publishes despite no matching <see cref="RehearsalPass"/>. Records are never
/// overwritten or deleted.
/// </summary>
public interface IPublishOverrideStore
{
    /// <summary>
    /// Appends a publish-override audit record. Records are never mutated once written.
    /// </summary>
    /// <param name="publishOverride">The override audit record to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The recorded override.</returns>
    Task<PublishOverride> RecordAsync(PublishOverride publishOverride, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all publish-override audit records for a blueprint, most recent first.
    /// </summary>
    /// <param name="blueprintId">The draft/service identity of the blueprint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The override records for the blueprint, ordered newest first.</returns>
    Task<IReadOnlyList<PublishOverride>> GetByBlueprintAsync(
        string blueprintId,
        CancellationToken cancellationToken = default);
}
