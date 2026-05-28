// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// Feature 142 (D4/FR-032) — storage for <see cref="RehearsalPass"/> records.
/// Records are created on a successful full rehearsal and never mutated; the
/// publish soft gate checks for the latest pass matching the publishing
/// version's executable-definition hash.
/// </summary>
public interface IRehearsalPassStore
{
    /// <summary>
    /// Records a successful rehearsal pass. Records are never mutated once written.
    /// </summary>
    /// <param name="pass">The rehearsal pass to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The recorded rehearsal pass.</returns>
    Task<RehearsalPass> RecordAsync(RehearsalPass pass, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent rehearsal pass matching the given blueprint and
    /// executable-definition hash, or <c>null</c> when none exists. This is what
    /// the publish soft gate checks.
    /// </summary>
    /// <param name="blueprintId">The draft/service identity of the blueprint.</param>
    /// <param name="execDefHash">Canonical hash of the executable definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The most recent matching pass, or <c>null</c>.</returns>
    Task<RehearsalPass?> GetLatestAsync(
        string blueprintId,
        string execDefHash,
        CancellationToken cancellationToken = default);
}
