// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Provenance;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Reads the provenance surfaces: a register's docket spine, and one docket's evidence trail.
/// </summary>
/// <remarks>
/// <para>
/// <b>Named provenance, not audit, and the distinction is load-bearing.</b>
/// <c>IAuditService</c> in this same folder <i>writes</i> a log of administrative actions. This
/// <i>reads</i> evidence and reports what can be proven about it. One word covering both is the
/// collision class Feature 187 spent its length untangling (research R-007).
/// </para>
/// <para>
/// <b>Two methods, because there are two endpoints, because verification is expensive.</b>
/// <see cref="GetSpineAsync"/> runs no checks on the server. <see cref="GetTrailAsync"/> verifies
/// exactly one docket. Do not add a convenience method that trails every docket on a page.
/// </para>
/// </remarks>
public interface IProvenanceService
{
    /// <summary>
    /// A page of the register's dockets from genesis. Runs no verification.
    /// </summary>
    /// <returns>The page, or null when the register is not held on this node or is not readable.</returns>
    Task<RegisterSpinePage?> GetSpineAsync(
        string registerId,
        ulong? fromDocket = null,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies one docket and returns its evidence trail.
    /// </summary>
    /// <returns>The trail, or null when the docket is not held on this node or is not readable.</returns>
    Task<ProvenanceTrailView?> GetTrailAsync(
        string registerId,
        ulong docketNumber,
        CancellationToken cancellationToken = default);
}
