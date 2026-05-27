// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Validator;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Administrator tool that starts, stops or restarts the validator for a register
/// (Feature 140 Wave 4 — node orchestration). Routes through the typed
/// <see cref="IValidatorServiceClient"/> so the caller's bearer is forwarded and the routes
/// are contract-pinned. "restart" is composed as a stop (persisting the mempool) followed by a
/// start, since the Validator Service exposes only start and stop.
/// </summary>
[McpServerToolType]
public sealed class ValidatorControlTool
{
    private const string ToolName = "sorcha_validator_control";
    private const string ServiceName = "Validator";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IValidatorServiceClient _validatorClient;
    private readonly ILogger<ValidatorControlTool> _logger;

    public ValidatorControlTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IValidatorServiceClient validatorClient,
        ILogger<ValidatorControlTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _validatorClient = validatorClient;
        _logger = logger;
    }

    /// <summary>
    /// Starts, stops or restarts the validator for a register.
    /// </summary>
    /// <param name="action">One of "start", "stop", or "restart".</param>
    /// <param name="registerId">The register whose validator to control.</param>
    /// <param name="persistMemPool">For "stop"/"restart", whether to persist the mempool before stopping (default true).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The control result.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Starts, stops or restarts the validator (docket-sealing engine) for one register, selected by the action argument (start | stop | restart). Call this when an operator needs to bring a register's validator online, take it offline for maintenance, or cycle it after a configuration change; restart is performed as a mempool-persisting stop followed by a start so in-flight transactions are not lost. Prefer this over sorcha_validator_status, which only reports state, when you actually need to change the validator's run state; stopping halts docket sealing for that register until it is started again.")]
    public async Task<ValidatorControlResult> InvokeAsync(
        [Description("One of 'start', 'stop', or 'restart'")] string action,
        [Description("The register ID whose validator to control")] string registerId,
        [Description("For 'stop'/'restart', persist the mempool before stopping (default true)")] bool persistMemPool = true,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new ValidatorControlResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        var normalised = action?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalised is not ("start" or "stop" or "restart"))
        {
            return new ValidatorControlResult
            {
                Status = "Error",
                Message = "Action must be 'start', 'stop', or 'restart'.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(registerId))
        {
            return new ValidatorControlResult
            {
                Status = "Error",
                Message = "Register ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new ValidatorControlResult
            {
                Status = "Unavailable",
                Message = "Validator service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Validator {Action} requested for register {RegisterId}", normalised, registerId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            bool ok;
            switch (normalised)
            {
                case "start":
                    ok = await _validatorClient.StartValidatorAsync(registerId, cancellationToken);
                    break;
                case "stop":
                    ok = await _validatorClient.StopValidatorAsync(registerId, persistMemPool, cancellationToken);
                    break;
                default: // restart — persist the mempool on the stop leg so in-flight work survives.
                    var stopped = await _validatorClient.StopValidatorAsync(registerId, persistMemPool: true, cancellationToken);
                    if (!stopped)
                    {
                        stopwatch.Stop();
                        _availabilityTracker.RecordFailure(ServiceName);
                        return new ValidatorControlResult
                        {
                            Status = "Error",
                            Message = $"Restart aborted: could not stop the validator for register '{registerId}'.",
                            CheckedAt = DateTimeOffset.UtcNow,
                            ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                            RegisterId = registerId,
                            Action = normalised
                        };
                    }
                    ok = await _validatorClient.StartValidatorAsync(registerId, cancellationToken);
                    break;
            }

            stopwatch.Stop();

            if (!ok)
            {
                _availabilityTracker.RecordFailure(ServiceName);
                return new ValidatorControlResult
                {
                    Status = "Error",
                    Message = $"Validator {normalised} for register '{registerId}' was not accepted by the service.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    RegisterId = registerId,
                    Action = normalised
                };
            }

            _availabilityTracker.RecordSuccess(ServiceName);
            return new ValidatorControlResult
            {
                Status = "Success",
                Message = $"Validator {normalised} for register '{registerId}' accepted.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                RegisterId = registerId,
                Action = normalised
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            _logger.LogWarning("Validator {Action} timed out for register {RegisterId}", normalised, registerId);
            return new ValidatorControlResult
            {
                Status = "Timeout",
                Message = "Request to validator service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogError(ex, "Failed validator {Action} for register {RegisterId}", normalised, registerId);
            return new ValidatorControlResult
            {
                Status = "Error",
                Message = $"Failed to {normalised} validator: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>Result of a validator start / stop / restart request.</summary>
public sealed record ValidatorControlResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The register whose validator was controlled (on success/failure with a known target).</summary>
    public string? RegisterId { get; init; }

    /// <summary>The action performed: start, stop, or restart.</summary>
    public string? Action { get; init; }
}
