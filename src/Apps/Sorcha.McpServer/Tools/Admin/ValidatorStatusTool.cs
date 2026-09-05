// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Configuration;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Administrator tool for checking validator consensus status.
/// </summary>
[McpServerToolType]
public sealed class ValidatorStatusTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ValidatorStatusTool> _logger;
    private readonly string _validatorServiceEndpoint;

    public ValidatorStatusTool(
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ValidatorStatusTool> logger)
    {
        _authService = authService;
        _errorHandler = errorHandler;
        _availabilityTracker = availabilityTracker;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _validatorServiceEndpoint = SorchaServiceAddresses.TryResolve(configuration, SorchaService.Validator) ?? "http://localhost:5004";
    }

    /// <summary>
    /// Queries the validator consensus status.
    /// </summary>
    /// <param name="registerId">Optional: Register ID to get specific validator status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validator status including consensus state, docket processing, and memory pool info.</returns>
    [McpServerTool(Name = "sorcha_validator_status")]
    [Description("Returns validator-service consensus status — quorum state, docket processing throughput, and memory-pool depth — either platform-wide or for a single register when registerId is supplied. Call this when transactions are accepted but not sealed into dockets, or after sorcha_health_check flags the Validator service as Degraded; prefer this over sorcha_peer_status when the concern is consensus and docket sealing rather than cross-node state replication, and prefer it over sorcha_register_stats when you need to know whether new transactions are advancing chain height rather than what is already on the ledger.")]
    public async Task<ValidatorStatusResult> GetValidatorStatusAsync(
        [Description("Optional register ID to query specific validator status")] string? registerId = null,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_validator_status"))
        {
            return new ValidatorStatusResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Validator"))
        {
            return new ValidatorStatusResult
            {
                Status = "Unavailable",
                Message = "Validator service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Querying validator status{RegisterInfo}",
            string.IsNullOrEmpty(registerId) ? "" : $" for register {registerId}");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            // First check health
            var healthStatus = await CheckServiceHealthAsync(client, cancellationToken);

            // If a specific register is requested, get its validator status
            RegisterValidatorInfo? registerInfo = null;
            if (!string.IsNullOrEmpty(registerId) && healthStatus?.IsHealthy == true)
            {
                registerInfo = await GetRegisterValidatorStatusAsync(client, registerId, cancellationToken);
            }

            stopwatch.Stop();

            // Record success
            _availabilityTracker.RecordSuccess("Validator");

            // Determine overall status
            string status;
            string message;

            if (healthStatus?.IsHealthy != true)
            {
                status = "Unhealthy";
                message = "Validator service is not healthy.";
            }
            else if (registerInfo != null && !registerInfo.HasQuorum)
            {
                status = "Degraded";
                message = $"Register {registerId} does not have quorum ({registerInfo.ActiveValidators}/{registerInfo.MinValidators} validators).";
            }
            else if (registerInfo != null)
            {
                status = "Healthy";
                message = $"Register {registerId} validator status is healthy with {registerInfo.ActiveValidators} active validators.";
            }
            else
            {
                status = "Healthy";
                message = "Validator service is operational.";
            }

            _logger.LogInformation(
                "Validator status query completed in {ElapsedMs}ms. Status: {Status}",
                stopwatch.ElapsedMilliseconds, status);

            return new ValidatorStatusResult
            {
                Status = status,
                Message = message,
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                ServiceHealth = healthStatus,
                RegisterInfo = registerInfo
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Validator");

            _logger.LogWarning("Validator status query timed out");

            return new ValidatorStatusResult
            {
                Status = "Timeout",
                Message = "Request to validator service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Validator", ex);

            _logger.LogWarning(ex, "Failed to query validator status");

            return new ValidatorStatusResult
            {
                Status = "Error",
                Message = $"Failed to connect to validator service: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Validator", ex);

            _logger.LogError(ex, "Unexpected error querying validator status");

            return new ValidatorStatusResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while querying validator status.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    private async Task<ValidatorServiceHealth?> CheckServiceHealthAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_validatorServiceEndpoint.TrimEnd('/')}/health";
            var response = await client.GetAsync(url, cancellationToken);

            var isHealthy = response.IsSuccessStatusCode;
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            return new ValidatorServiceHealth
            {
                IsHealthy = isHealthy,
                HealthMessage = content.Trim()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking validator service health");
            return new ValidatorServiceHealth
            {
                IsHealthy = false,
                HealthMessage = ex.Message
            };
        }
    }

    private async Task<RegisterValidatorInfo?> GetRegisterValidatorStatusAsync(
        HttpClient client,
        string registerId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get validator pipeline status
            var statusUrl = $"{_validatorServiceEndpoint.TrimEnd('/')}/api/admin/validators/{registerId}/status";
            var statusResponse = await client.GetAsync(statusUrl, cancellationToken);

            ValidatorPipelineStatus? pipelineStatus = null;
            if (statusResponse.IsSuccessStatusCode)
            {
                var statusContent = await statusResponse.Content.ReadAsStringAsync(cancellationToken);
                pipelineStatus = JsonSerializer.Deserialize<ValidatorPipelineStatus>(statusContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }

            // Get validator count/quorum info
            var countUrl = $"{_validatorServiceEndpoint.TrimEnd('/')}/api/validators/{registerId}/count";
            var countResponse = await client.GetAsync(countUrl, cancellationToken);

            ValidatorCountInfo? countInfo = null;
            if (countResponse.IsSuccessStatusCode)
            {
                var countContent = await countResponse.Content.ReadAsStringAsync(cancellationToken);
                countInfo = JsonSerializer.Deserialize<ValidatorCountInfo>(countContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }

            // Return null if we couldn't get any useful information
            if (pipelineStatus == null && countInfo == null)
            {
                _logger.LogDebug("No validator information available for register {RegisterId}", registerId);
                return null;
            }

            return new RegisterValidatorInfo
            {
                RegisterId = registerId,
                IsActive = pipelineStatus?.IsActive ?? false,
                TransactionsInMemPool = pipelineStatus?.TransactionsInMemPool ?? 0,
                DocketsProposed = pipelineStatus?.DocketsProposed ?? 0,
                DocketsConfirmed = pipelineStatus?.DocketsConfirmed ?? 0,
                DocketsRejected = pipelineStatus?.DocketsRejected ?? 0,
                StartedAt = pipelineStatus?.StartedAt,
                LastDocketBuildAt = pipelineStatus?.LastDocketBuildAt,
                ActiveValidators = countInfo?.ActiveCount ?? 0,
                MinValidators = countInfo?.MinValidators ?? 0,
                MaxValidators = countInfo?.MaxValidators ?? 0,
                HasQuorum = countInfo?.HasQuorum ?? false
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting register validator status for {RegisterId}", registerId);
            return null;
        }
    }

    // Internal response models for deserialization
    private sealed class ValidatorPipelineStatus
    {
        public string RegisterId { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int TransactionsInMemPool { get; set; }
        public long DocketsProposed { get; set; }
        public long DocketsConfirmed { get; set; }
        public long DocketsRejected { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? LastDocketBuildAt { get; set; }
    }

    private sealed class ValidatorCountInfo
    {
        public string RegisterId { get; set; } = string.Empty;
        public int ActiveCount { get; set; }
        public int MinValidators { get; set; }
        public int MaxValidators { get; set; }
        public bool HasQuorum { get; set; }
    }
}

/// <summary>
/// Result of a validator status query.
/// </summary>
public sealed record ValidatorStatusResult
{
    /// <summary>
    /// Overall status: Healthy, Degraded, Unhealthy, Unavailable, Timeout, Error, or Unauthorized.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable message about the validator status.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// When the status check was performed.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>
    /// Validator service health status.
    /// </summary>
    public ValidatorServiceHealth? ServiceHealth { get; init; }

    /// <summary>
    /// Register-specific validator information (if registerId was provided).
    /// </summary>
    public RegisterValidatorInfo? RegisterInfo { get; init; }
}

/// <summary>
/// Validator service health status.
/// </summary>
public sealed record ValidatorServiceHealth
{
    /// <summary>
    /// Whether the service is healthy.
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// Health check message from the service.
    /// </summary>
    public string? HealthMessage { get; init; }
}

/// <summary>
/// Validator information for a specific register.
/// </summary>
public sealed record RegisterValidatorInfo
{
    /// <summary>
    /// Register ID.
    /// </summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// Whether the validator pipeline is active.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Number of transactions in the memory pool.
    /// </summary>
    public int TransactionsInMemPool { get; init; }

    /// <summary>
    /// Total dockets proposed.
    /// </summary>
    public long DocketsProposed { get; init; }

    /// <summary>
    /// Total dockets confirmed.
    /// </summary>
    public long DocketsConfirmed { get; init; }

    /// <summary>
    /// Total dockets rejected.
    /// </summary>
    public long DocketsRejected { get; init; }

    /// <summary>
    /// When the validator was started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// When the last docket was built.
    /// </summary>
    public DateTimeOffset? LastDocketBuildAt { get; init; }

    /// <summary>
    /// Number of active validators for this register.
    /// </summary>
    public int ActiveValidators { get; init; }

    /// <summary>
    /// Minimum validators required for consensus.
    /// </summary>
    public int MinValidators { get; init; }

    /// <summary>
    /// Maximum validators allowed.
    /// </summary>
    public int MaxValidators { get; init; }

    /// <summary>
    /// Whether the register has enough validators for consensus.
    /// </summary>
    public bool HasQuorum { get; init; }
}
