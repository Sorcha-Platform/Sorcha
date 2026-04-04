// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Blueprint;

/// <summary>
/// Unified client interface for Blueprint Service operations
/// </summary>
public interface IBlueprintServiceClient
{
    /// <summary>
    /// Gets a blueprint by ID
    /// </summary>
    /// <param name="blueprintId">Blueprint ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Blueprint definition, or null if not found</returns>
    Task<string?> GetBlueprintAsync(
        string blueprintId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a transaction payload against blueprint schema
    /// </summary>
    /// <param name="blueprintId">Blueprint ID</param>
    /// <param name="actionId">Action ID within blueprint</param>
    /// <param name="payload">Payload to validate (JSON)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if payload is valid</returns>
    Task<bool> ValidatePayloadAsync(
        string blueprintId,
        string actionId,
        string payload,
        CancellationToken cancellationToken = default);
}
