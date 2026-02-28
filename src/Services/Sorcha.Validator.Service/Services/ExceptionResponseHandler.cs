// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Handles failed validations by creating and delivering exception responses
/// </summary>
public class ExceptionResponseHandler : IExceptionResponseHandler
{
    private readonly ValidatorConfiguration _config;
    private readonly IHubContext<Hub>? _hubContext;
    private readonly ILogger<ExceptionResponseHandler> _logger;

    // Statistics
    private long _totalCreated;
    private long _totalDelivered;
    private readonly ConcurrentDictionary<ExceptionCode, long> _byCode = new();
    private readonly ConcurrentDictionary<ExceptionDeliveryMethod, long> _byDeliveryMethod = new();
    private readonly ConcurrentQueue<double> _deliveryLatencies = new();
    private readonly object _statsLock = new();

    public ExceptionResponseHandler(
        IOptions<ValidatorConfiguration> config,
        ILogger<ExceptionResponseHandler> logger,
        IHubContext<Hub>? hubContext = null)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hubContext = hubContext;
    }

    /// <inheritdoc/>
    public ExceptionResponse CreateResponse(
        ValidationEngineResult validationResult,
        Transaction originalTransaction)
    {
        ArgumentNullException.ThrowIfNull(validationResult);
        ArgumentNullException.ThrowIfNull(originalTransaction);

        if (validationResult.IsValid)
        {
            throw new ArgumentException("Cannot create exception response for valid transaction", nameof(validationResult));
        }

        var exceptionId = GenerateExceptionId();
        var code = DetermineExceptionCode(validationResult.Errors);
        var summary = GenerateSummary(validationResult.Errors);
        var details = validationResult.Errors.Select(CreateExceptionDetail).ToList();

        var response = new ExceptionResponse
        {
            ExceptionId = exceptionId,
            TransactionId = validationResult.TransactionId,
            RegisterId = validationResult.RegisterId,
            BlueprintId = originalTransaction.BlueprintId,
            Code = code,
            Summary = summary,
            Details = details,
            ValidationDuration = validationResult.ValidationDuration,
            ValidatorId = _config.ValidatorId
        };

        RecordCreated(code);

        _logger.LogDebug(
            "Created exception response {ExceptionId} for transaction {TransactionId}: {Summary}",
            exceptionId, validationResult.TransactionId, summary);

        return response;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ExceptionResponse> CreateResponses(
        IReadOnlyList<ValidationEngineResult> validationResults,
        IReadOnlyList<Models.Transaction> originalTransactions)
    {
        ArgumentNullException.ThrowIfNull(validationResults);
        ArgumentNullException.ThrowIfNull(originalTransactions);

        if (validationResults.Count != originalTransactions.Count)
        {
            throw new ArgumentException(
                "Validation results and transactions must have the same count",
                nameof(originalTransactions));
        }

        var responses = new List<ExceptionResponse>();

        for (int i = 0; i < validationResults.Count; i++)
        {
            var result = validationResults[i];
            if (!result.IsValid)
            {
                responses.Add(CreateResponse(result, originalTransactions[i]));
            }
        }

        return responses;
    }

    /// <inheritdoc/>
    public async Task<bool> DeliverViaSignalRAsync(
        ExceptionResponse response,
        string? connectionId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (_hubContext == null)
        {
            _logger.LogWarning(
                "Cannot deliver exception {ExceptionId} via SignalR - hub not configured",
                response.ExceptionId);
            return false;
        }

        try
        {
            var notification = new ExceptionNotification
            {
                ExceptionId = response.ExceptionId,
                TransactionId = response.TransactionId,
                RegisterId = response.RegisterId,
                BlueprintId = response.BlueprintId,
                Code = response.Code.ToString(),
                Summary = response.Summary,
                ErrorCount = response.Details.Count,
                CreatedAt = response.CreatedAt
            };

            if (!string.IsNullOrEmpty(connectionId))
            {
                // Send to specific connection
                await _hubContext.Clients.Client(connectionId)
                    .SendAsync("ValidationException", notification, ct);
            }
            else
            {
                // Broadcast to register group
                await _hubContext.Clients.Group($"register:{response.RegisterId}")
                    .SendAsync("ValidationException", notification, ct);
            }

            RecordDelivered(ExceptionDeliveryMethod.SignalR, response.CreatedAt);

            _logger.LogDebug(
                "Delivered exception {ExceptionId} via SignalR to {Target}",
                response.ExceptionId,
                connectionId ?? $"register:{response.RegisterId}");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to deliver exception {ExceptionId} via SignalR",
                response.ExceptionId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task RecordToAuditLogAsync(
        ExceptionResponse response,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        // Log the exception details for audit purposes
        _logger.LogInformation(
            "Validation exception {ExceptionId}: Transaction={TransactionId}, Register={RegisterId}, " +
            "Code={Code}, Summary={Summary}, Errors={ErrorCount}, Duration={Duration}ms",
            response.ExceptionId,
            response.TransactionId,
            response.RegisterId,
            response.Code,
            response.Summary,
            response.Details.Count,
            response.ValidationDuration.TotalMilliseconds);

        // Log each detail at debug level
        foreach (var detail in response.Details)
        {
            _logger.LogDebug(
                "  [{Code}] {Category}: {Message} (Field: {Field}, Fatal: {IsFatal})",
                detail.Code,
                detail.Category,
                detail.Message,
                detail.Field ?? "N/A",
                detail.IsFatal);
        }

        RecordDelivered(ExceptionDeliveryMethod.AuditLogOnly, response.CreatedAt);

        await Task.CompletedTask; // Placeholder for async audit log persistence
    }

    /// <inheritdoc/>
    public ExceptionResponseStats GetStats()
    {
        double avgLatency = 0;
        lock (_statsLock)
        {
            if (_deliveryLatencies.Count > 0)
            {
                avgLatency = _deliveryLatencies.Average();
            }
        }

        return new ExceptionResponseStats
        {
            TotalCreated = Interlocked.Read(ref _totalCreated),
            TotalDelivered = Interlocked.Read(ref _totalDelivered),
            ByCode = new Dictionary<ExceptionCode, long>(_byCode),
            ByDeliveryMethod = new Dictionary<ExceptionDeliveryMethod, long>(_byDeliveryMethod),
            AverageDeliveryLatency = TimeSpan.FromMilliseconds(avgLatency)
        };
    }


    private static string GenerateExceptionId()
    {
        return $"exc_{Guid.NewGuid():N}";
    }

    private static ExceptionCode DetermineExceptionCode(IReadOnlyList<ValidationEngineError> errors)
    {
        if (errors.Count == 0)
            return ExceptionCode.InternalError;

        if (errors.Count > 1 && errors.Select(e => e.Category).Distinct().Count() > 1)
            return ExceptionCode.MultipleFailures;

        // Use the first error's category to determine the code
        var primaryCategory = errors[0].Category;
        return primaryCategory switch
        {
            ValidationErrorCategory.Structure => ExceptionCode.InvalidStructure,
            ValidationErrorCategory.Schema => ExceptionCode.SchemaViolation,
            ValidationErrorCategory.Cryptographic => ExceptionCode.CryptographicFailure,
            ValidationErrorCategory.Chain => ExceptionCode.ChainViolation,
            ValidationErrorCategory.Blueprint => ExceptionCode.BlueprintError,
            ValidationErrorCategory.Permission => ExceptionCode.PermissionDenied,
            ValidationErrorCategory.Timing => ExceptionCode.TimingViolation,
            ValidationErrorCategory.Internal => ExceptionCode.InternalError,
            _ => ExceptionCode.InternalError
        };
    }

    private static string GenerateSummary(IReadOnlyList<ValidationEngineError> errors)
    {
        if (errors.Count == 0)
            return "Validation failed with no specific errors";

        if (errors.Count == 1)
            return errors[0].Message;

        // Find fatal errors first
        var fatalErrors = errors.Where(e => e.IsFatal).ToList();
        if (fatalErrors.Count > 0)
        {
            return fatalErrors.Count == 1
                ? fatalErrors[0].Message
                : $"Validation failed with {fatalErrors.Count} fatal errors: {fatalErrors[0].Message}";
        }

        // Summarize by category
        var categories = errors.Select(e => e.Category).Distinct().ToList();
        if (categories.Count == 1)
        {
            return $"Validation failed with {errors.Count} {categories[0]} errors";
        }

        return $"Validation failed with {errors.Count} errors across {categories.Count} categories";
    }

    private static ExceptionDetail CreateExceptionDetail(ValidationEngineError error)
    {
        return new ExceptionDetail
        {
            Code = error.Code,
            Message = error.Message,
            Category = error.Category,
            Field = error.Field,
            IsFatal = error.IsFatal,
            Remediation = GetRemediationAdvice(error)
        };
    }

    private static string? GetRemediationAdvice(ValidationEngineError error)
    {
        return error.Category switch
        {
            ValidationErrorCategory.Structure =>
                "Ensure all required fields are present and properly formatted",
            ValidationErrorCategory.Schema =>
                "Verify the payload matches the blueprint's action schema",
            ValidationErrorCategory.Cryptographic =>
                "Regenerate signatures using a valid wallet key",
            ValidationErrorCategory.Chain =>
                "Check the previousId reference points to a valid transaction",
            ValidationErrorCategory.Blueprint =>
                "Verify the blueprint ID and action ID are correct",
            ValidationErrorCategory.Permission =>
                "Confirm the signing wallet is authorized for this action",
            ValidationErrorCategory.Timing =>
                "Ensure the transaction timestamp is current and not expired",
            _ => null
        };
    }

    private void RecordCreated(ExceptionCode code)
    {
        Interlocked.Increment(ref _totalCreated);
        _byCode.AddOrUpdate(code, 1, (_, count) => count + 1);
    }

    private void RecordDelivered(ExceptionDeliveryMethod method, DateTimeOffset createdAt)
    {
        Interlocked.Increment(ref _totalDelivered);
        _byDeliveryMethod.AddOrUpdate(method, 1, (_, count) => count + 1);

        // Track delivery latency
        var latency = (DateTimeOffset.UtcNow - createdAt).TotalMilliseconds;
        lock (_statsLock)
        {
            _deliveryLatencies.Enqueue(latency);
            while (_deliveryLatencies.Count > 1000)
            {
                _deliveryLatencies.TryDequeue(out _);
            }
        }
    }

}

/// <summary>
/// SignalR notification for validation exceptions
/// </summary>
public record ExceptionNotification
{
    /// <summary>Exception ID</summary>
    public required string ExceptionId { get; init; }

    /// <summary>Original transaction ID</summary>
    public required string TransactionId { get; init; }

    /// <summary>Register ID</summary>
    public required string RegisterId { get; init; }

    /// <summary>Blueprint ID (if known)</summary>
    public string? BlueprintId { get; init; }

    /// <summary>Exception code</summary>
    public required string Code { get; init; }

    /// <summary>Summary message</summary>
    public required string Summary { get; init; }

    /// <summary>Number of validation errors</summary>
    public int ErrorCount { get; init; }

    /// <summary>When the exception was created</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
