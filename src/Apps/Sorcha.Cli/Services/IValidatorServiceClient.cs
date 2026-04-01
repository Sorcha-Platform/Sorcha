// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Refit;
using Sorcha.Cli.Models;

namespace Sorcha.Cli.Services;

/// <summary>
/// Refit client interface for the Validator Service API.
/// </summary>
public interface IValidatorServiceClient
{
    /// <summary>
    /// Starts the validator service.
    /// </summary>
    [Post("/api/admin/validators/start")]
    Task<ValidatorActionResponse> StartAsync([Header("Authorization")] string authorization);

    /// <summary>
    /// Stops the validator service.
    /// </summary>
    [Post("/api/admin/validators/stop")]
    Task<ValidatorActionResponse> StopAsync([Header("Authorization")] string authorization);

    /// <summary>
    /// Gets the validator status for a register.
    /// </summary>
    [Get("/api/admin/validators/{registerId}/status")]
    Task<ValidatorStatus> GetStatusAsync(string registerId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Triggers processing of pending transactions for a register.
    /// </summary>
    [Post("/api/admin/validators/{registerId}/process")]
    Task<ValidatorProcessResult> ProcessRegisterAsync(string registerId, [Header("Authorization")] string authorization);

    // --- Metrics ---

    /// <summary>
    /// Gets aggregated validator metrics.
    /// </summary>
    [Get("/api/metrics")]
    Task<HttpResponseMessage> GetAggregatedMetricsAsync([Header("Authorization")] string authorization);

    /// <summary>
    /// Gets validation-specific metrics.
    /// </summary>
    [Get("/api/metrics/validation")]
    Task<HttpResponseMessage> GetValidationMetricsAsync([Header("Authorization")] string authorization);

    /// <summary>
    /// Gets consensus metrics.
    /// </summary>
    [Get("/api/metrics/consensus")]
    Task<HttpResponseMessage> GetConsensusMetricsAsync([Header("Authorization")] string authorization);

    /// <summary>
    /// Gets pool metrics.
    /// </summary>
    [Get("/api/metrics/pools")]
    Task<HttpResponseMessage> GetPoolMetricsAsync([Header("Authorization")] string authorization);

    /// <summary>
    /// Gets cache metrics.
    /// </summary>
    [Get("/api/metrics/caches")]
    Task<HttpResponseMessage> GetCacheMetricsAsync([Header("Authorization")] string authorization);

    /// <summary>
    /// Gets configuration metrics.
    /// </summary>
    [Get("/api/metrics/config")]
    Task<HttpResponseMessage> GetConfigMetricsAsync([Header("Authorization")] string authorization);

    // --- Consent ---

    /// <summary>
    /// Gets pending validator registrations for a register.
    /// </summary>
    [Get("/api/validators/{registerId}/pending")]
    Task<HttpResponseMessage> GetPendingValidatorsAsync(string registerId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Approves a pending validator registration.
    /// </summary>
    [Post("/api/validators/{registerId}/{validatorId}/approve")]
    Task<HttpResponseMessage> ApproveValidatorAsync(string registerId, string validatorId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Rejects a pending validator registration.
    /// </summary>
    [Post("/api/validators/{registerId}/{validatorId}/reject")]
    Task<HttpResponseMessage> RejectValidatorAsync(string registerId, string validatorId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Refreshes the validator list for a register.
    /// </summary>
    [Post("/api/validators/{registerId}/refresh")]
    Task<HttpResponseMessage> RefreshValidatorsAsync(string registerId, [Header("Authorization")] string authorization);

    // --- Threshold ---

    /// <summary>
    /// Gets the threshold signing status for a register.
    /// </summary>
    [Get("/api/v1/validators/threshold/{registerId}/status")]
    Task<HttpResponseMessage> GetThresholdStatusAsync(string registerId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Sets up threshold signing for a register.
    /// </summary>
    [Post("/api/v1/validators/threshold/setup")]
    Task<HttpResponseMessage> SetupThresholdAsync([Body] object request, [Header("Authorization")] string authorization);
}
