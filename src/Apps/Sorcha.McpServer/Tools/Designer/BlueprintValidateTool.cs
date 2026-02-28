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

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for validating action data against a blueprint's schema.
/// </summary>
[McpServerToolType]
public sealed class BlueprintValidateTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BlueprintValidateTool> _logger;
    private readonly string _blueprintServiceEndpoint;

    public BlueprintValidateTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<BlueprintValidateTool> logger)
    {
        _sessionService = sessionService;
        _authService = authService;
        _errorHandler = errorHandler;
        _availabilityTracker = availabilityTracker;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _blueprintServiceEndpoint = configuration["ServiceClients:BlueprintService:Address"] ?? "http://localhost:5000";
    }

    /// <summary>
    /// Validates action data against a blueprint action's schema.
    /// </summary>
    /// <param name="blueprintId">The blueprint ID containing the action.</param>
    /// <param name="actionId">The action ID (sequence number) to validate against.</param>
    /// <param name="dataJson">The action data in JSON format to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result including any schema errors.</returns>
    [McpServerTool(Name = "sorcha_blueprint_validate")]
    [Description("Validate action data against a blueprint action's JSON Schema. Use this to check if data conforms to the required schema before submitting to a workflow. Returns validation errors with paths to invalid fields.")]
    public async Task<BlueprintValidateResult> ValidateActionDataAsync(
        [Description("Blueprint ID containing the action")] string blueprintId,
        [Description("Action ID (sequence number) to validate against")] string actionId,
        [Description("Action data in JSON format to validate")] string dataJson,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_blueprint_validate"))
        {
            return new BlueprintValidateResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:designer role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate inputs
        if (string.IsNullOrWhiteSpace(blueprintId))
        {
            return new BlueprintValidateResult
            {
                Status = "Error",
                Message = "Blueprint ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(actionId))
        {
            return new BlueprintValidateResult
            {
                Status = "Error",
                Message = "Action ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return new BlueprintValidateResult
            {
                Status = "Error",
                Message = "Data JSON is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Parse data JSON
        Dictionary<string, object>? data;
        try
        {
            data = JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson);
            if (data == null)
            {
                return new BlueprintValidateResult
                {
                    Status = "Error",
                    Message = "Data JSON must be a valid object.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }
        catch (JsonException ex)
        {
            return new BlueprintValidateResult
            {
                Status = "Error",
                Message = $"Invalid data JSON format: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new BlueprintValidateResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Validating action data for blueprint {BlueprintId}, action {ActionId}",
            blueprintId, actionId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var validationResult = await ValidateWithServiceAsync(client, blueprintId, actionId, dataJson, cancellationToken);

            stopwatch.Stop();

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            if (validationResult == null)
            {
                return new BlueprintValidateResult
                {
                    Status = "Error",
                    Message = "Failed to validate data. The service returned an unexpected response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            var status = validationResult.IsValid ? "Valid" : "Invalid";
            var message = validationResult.IsValid
                ? "Data is valid according to the action's schema."
                : $"Data validation failed with {validationResult.Errors.Count} error(s).";

            _logger.LogInformation(
                "Validation completed in {ElapsedMs}ms. IsValid: {IsValid}, Errors: {ErrorCount}",
                stopwatch.ElapsedMilliseconds, validationResult.IsValid, validationResult.Errors.Count);

            return new BlueprintValidateResult
            {
                Status = status,
                Message = message,
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                IsValid = validationResult.IsValid,
                Errors = validationResult.Errors
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            _logger.LogWarning("Validation request timed out");

            return new BlueprintValidateResult
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

            _logger.LogWarning(ex, "Failed to validate action data");

            return new BlueprintValidateResult
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

            return new BlueprintValidateResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while validating action data.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    private async Task<ValidationResultDto?> ValidateWithServiceAsync(
        HttpClient client,
        string blueprintId,
        string actionId,
        string dataJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/execution/validate";

            // Build request body - data needs to be an object, not a string
            var requestBody = new
            {
                blueprintId,
                actionId,
                data = JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson)
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await client.PostAsync(url, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Validation request failed: HTTP {StatusCode} - {Error}",
                    response.StatusCode, errorContent);

                // Try to parse error response
                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (errorResponse?.Error != null)
                    {
                        return new ValidationResultDto
                        {
                            IsValid = false,
                            Errors = [new ValidationError { Path = "", Message = errorResponse.Error }]
                        };
                    }
                }
                catch (JsonException)
                {
                    // Error response body was not valid JSON; fall through to return null
                }

                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<ValidationResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null) return null;

            return new ValidationResultDto
            {
                IsValid = result.IsValid,
                Errors = result.Errors?.Select(e => new ValidationError
                {
                    Path = e.Path ?? "",
                    Message = e.Message ?? "Validation error",
                    SchemaLocation = e.SchemaLocation,
                    Keyword = e.Keyword
                }).ToList() ?? []
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Error parsing validation response");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error validating action data");
            return null;
        }
    }

    // Internal response models
    private sealed class ValidationResponse
    {
        public bool IsValid { get; set; }
        public List<ValidationErrorDto>? Errors { get; set; }
    }

    private sealed class ValidationErrorDto
    {
        public string? Path { get; set; }
        public string? Message { get; set; }
        public string? SchemaLocation { get; set; }
        public string? Keyword { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
    }

    private sealed class ValidationResultDto
    {
        public bool IsValid { get; set; }
        public List<ValidationError> Errors { get; set; } = [];
    }
}

/// <summary>
/// Result of a blueprint validation operation.
/// </summary>
public sealed record BlueprintValidateResult
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
    /// When the validation was performed.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>
    /// Whether the data is valid according to the schema.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// List of validation errors (if any).
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; init; } = [];
}

/// <summary>
/// A validation error.
/// </summary>
public sealed record ValidationError
{
    /// <summary>
    /// JSON path to the invalid field (e.g., "/applicant/name").
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Description of the validation error.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Location in the schema that triggered the error.
    /// </summary>
    public string? SchemaLocation { get; init; }

    /// <summary>
    /// The JSON Schema keyword that failed (e.g., "required", "minLength").
    /// </summary>
    public string? Keyword { get; init; }
}
