// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Credentials;

namespace Sorcha.UI.Core.Services.Credentials;

/// <summary>
/// Service interface for verifier-side presentation request management.
/// </summary>
public interface IPresentationAdminService
{
    /// <summary>
    /// Creates a new presentation request with QR code URL.
    /// </summary>
    Task<PresentationRequestResultViewModel?> CreatePresentationRequestAsync(
        CreatePresentationRequestViewModel request, CancellationToken ct = default);

    /// <summary>
    /// Gets the result of a presentation request by ID.
    /// Returns null if not found, status "Expired" if gone.
    /// </summary>
    Task<PresentationRequestResultViewModel?> GetPresentationResultAsync(
        string requestId, CancellationToken ct = default);
}
