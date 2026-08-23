// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Validator.Service.Services.Interfaces;

/// <summary>
/// Fetches blueprints from the Blueprint Service.
/// Used by BlueprintCache to retrieve blueprints when not in cache.
/// </summary>
public interface IBlueprintFetcher
{
    /// <summary>
    /// Fetches a blueprint by ID from the Blueprint Service.
    /// </summary>
    /// <param name="blueprintId">Blueprint ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Blueprint, or null if not found</returns>
    Task<BlueprintModel?> FetchBlueprintAsync(
        string blueprintId,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches one specific published definition by its executable-definition hash — the definition
    /// a running instance is pinned to (Feature 194).
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="FetchBlueprintAsync"/> on purpose. That method answers "the current
    /// definition"; this one answers "the definition this instance started on", and after a
    /// republish those are different blueprints. Substituting one for the other would resolve an
    /// in-flight instance against rules it never agreed to — silently.
    /// </remarks>
    /// <param name="blueprintId">Blueprint ID.</param>
    /// <param name="execDefHash">The executable-definition hash (the pin).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The pinned definition, or null when it cannot be resolved (a refusal).</returns>
    Task<BlueprintModel?> FetchBlueprintByHashAsync(
        string blueprintId,
        string execDefHash,
        CancellationToken ct = default);

    /// <summary>
    /// Validates a transaction payload against a blueprint action schema.
    /// </summary>
    /// <param name="blueprintId">Blueprint ID</param>
    /// <param name="actionId">Action ID</param>
    /// <param name="payloadJson">JSON payload to validate</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Validation result</returns>
    Task<BlueprintPayloadValidationResult> ValidatePayloadAsync(
        string blueprintId,
        string actionId,
        string payloadJson,
        CancellationToken ct = default);

    /// <summary>
    /// Gets statistics about blueprint fetching.
    /// </summary>
    /// <returns>Fetcher statistics</returns>
    BlueprintFetcherStats GetStats();
}

/// <summary>
/// Result of validating a payload against a blueprint action schema.
/// </summary>
public record BlueprintPayloadValidationResult
{
    /// <summary>Whether the payload is valid</summary>
    public bool IsValid { get; init; }

    /// <summary>Validation errors if invalid</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>Whether the blueprint was found</summary>
    public bool BlueprintFound { get; init; } = true;

    /// <summary>Whether the action was found</summary>
    public bool ActionFound { get; init; } = true;
}

/// <summary>
/// Statistics for blueprint fetching.
/// </summary>
public record BlueprintFetcherStats
{
    /// <summary>Total blueprints fetched</summary>
    public long TotalFetched { get; init; }

    /// <summary>Total fetch failures</summary>
    public long TotalFailures { get; init; }

    /// <summary>Total payload validations</summary>
    public long TotalValidations { get; init; }

    /// <summary>Average fetch time in milliseconds</summary>
    public double AverageFetchTimeMs { get; init; }

    /// <summary>Last fetch timestamp</summary>
    public DateTimeOffset? LastFetchedAt { get; init; }
}
