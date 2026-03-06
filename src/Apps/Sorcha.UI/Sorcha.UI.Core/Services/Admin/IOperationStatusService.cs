// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services.Admin;

/// <summary>
/// Service for retrieving encryption operation status.
/// </summary>
public interface IOperationStatusService
{
    /// <summary>
    /// Gets the current status of an encryption operation.
    /// </summary>
    Task<EncryptionOperationViewModel?> GetStatusAsync(string operationId, CancellationToken ct = default);
}
