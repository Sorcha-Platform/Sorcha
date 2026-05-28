// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Blueprints;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Aggregates the Feature 142 Go-live system-info detail card (FR-026 / D6) for a chosen register
/// via a client-side fan-out over the existing register reads — there is no dedicated server
/// aggregate endpoint. Each sub-read degrades independently: a failed read leaves its field at a
/// default and flips its <c>*Available</c> flag, so a single failure never crashes the card.
/// </summary>
public interface IRegisterSystemInfoService
{
    /// <summary>
    /// Fans out over the register's own reads — ownership/relationship, validator roster + required
    /// signatures, visibility (advertise), sync state, developer-mode, published-service count — and
    /// derives the caller's governance role, returning a single aggregated view-model.
    /// </summary>
    /// <param name="registerId">The candidate Go-live register.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The aggregated system-info view-model (resilient to partial sub-read failures).</returns>
    Task<RegisterSystemInfoViewModel> GetSystemInfoAsync(
        string registerId,
        CancellationToken cancellationToken = default);
}
