// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Configuration;

namespace Sorcha.McpServer.Tools.Participant;

/// <summary>
/// Participant tool for validating action data before submission.
/// </summary>
[McpServerToolType]
public sealed class ActionValidateTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ActionValidateTool> _logger;
    private readonly string _blueprintServiceEndpoint;

    public ActionValidateTool(
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ActionValidateTool> logger)
    {
        _authService = authService;
        _errorHandler = errorHandler;
        _availabilityTracker = availabilityTracker;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _blueprintServiceEndpoint = SorchaServiceAddresses.TryResolve(configuration, SorchaService.Blueprint) ?? "http://localhost:5000";
    }

    /// <summary>
    /// Validates action data before submitting.
    /// </summary>
    /// <param name="actionInstanceId">The action instance ID.</param>
    /// <param name="dataJson">The action data in JSON format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result.</returns>
    [McpServerTool(Name = "sorcha_action_validate")]
    [Description("Validate a candidate JSON payload against the input schema of a specific action without committing anything to the register. Returns a list of schema violations or a clean pass, leaving the action in its current pending state. Call this when an agent has drafted submission data and wants to confirm it will pass server-side schema validation; use this rather than sorcha_action_submit when you do not yet intend to advance the workflow, and call sorcha_action_details first when you need to see the schema itself before drafting input.")]
    public async Task<ActionValidateResult> ValidateActionDataAsync(
        [Description("The action instance ID")] string actionInstanceId,
        [Description("The action data in JSON format")] string dataJson,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_action_validate"))
        {
            return new ActionValidateResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:participant role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate inputs
        if (string.IsNullOrWhiteSpace(actionInstanceId))
        {
            return new ActionValidateResult
            {
                Status = "Error",
                Message = "Action instance ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return new ActionValidateResult
            {
                Status = "Error",
                Message = "Data JSON is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Parse data JSON
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson);
            if (data == null)
            {
                return new ActionValidateResult
                {
                    Status = "Error",
                    Message = "Data JSON must be a valid object.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }
        catch (JsonException ex)
        {
            return new ActionValidateResult
            {
                Status = "Error",
                Message = $"Invalid data JSON format: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new ActionValidateResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Validating action data for {ActionInstanceId}", actionInstanceId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var url = $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/actions/{actionInstanceId}/validate";

            var content = new StringContent(dataJson, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Action validation request failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Blueprint");

                return new ActionValidateResult
                {
                    Status = "Error",
                    Message = "Validation request failed.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<ValidateResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new ActionValidateResult
                {
                    Status = "Error",
                    Message = "Failed to parse validation response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            var errorCount = result.Errors?.Count ?? 0;

            _logger.LogInformation(
                "Action validation completed in {ElapsedMs}ms. Valid: {IsValid}, Errors: {ErrorCount}",
                stopwatch.ElapsedMilliseconds, result.IsValid, errorCount);

            return new ActionValidateResult
            {
                Status = result.IsValid ? "Valid" : "Invalid",
                Message = result.IsValid
                    ? "Data is valid for submission."
                    : $"Data has {errorCount} validation error(s).",
                IsValid = result.IsValid,
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Errors = result.Errors?.Select(e => new ValidationError
                {
                    Path = e.Path ?? "",
                    Message = e.Message ?? "Unknown error"
                }).ToList() ?? []
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            return new ActionValidateResult
            {
                Status = "Timeout",
                Message = "Request to blueprint service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);

            return new ActionValidateResult
            {
                Status = "Error",
                Message = $"Failed to connect to blueprint service: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);

            _logger.LogError(ex, "Unexpected error validating action data");

            return new ActionValidateResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while validating action data.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class ValidateResponse
    {
        public bool IsValid { get; set; }
        public List<ValidationErrorDto>? Errors { get; set; }
    }

    private sealed class ValidationErrorDto
    {
        public string? Path { get; set; }
        public string? Message { get; set; }
    }
}

/// <summary>
/// Result of validating action data.
/// </summary>
public sealed record ActionValidateResult
{
    /// <summary>
    /// Operation status: Valid, Invalid, Error, Unavailable, Timeout, or Unauthorized.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable message about the validation result.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Whether the data is valid for submission.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// When the validation was performed.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>
    /// List of validation errors if the data is invalid.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; init; } = [];
}

/// <summary>
/// A validation error.
/// </summary>
public sealed record ValidationError
{
    /// <summary>
    /// JSON path of the invalid field.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Error message.
    /// </summary>
    public required string Message { get; init; }
}
