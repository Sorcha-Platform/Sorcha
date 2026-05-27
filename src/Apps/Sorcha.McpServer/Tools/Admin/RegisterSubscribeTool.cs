// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Peer;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Administrator tool that subscribes the local node to a register for peer replication.
/// Routes through the typed <see cref="IPeerServiceClient"/> (spec 139 US4) so the caller's
/// bearer is forwarded and the route is contract-pinned.
/// </summary>
[McpServerToolType]
public sealed class RegisterSubscribeTool
{
    private const string ToolName = "sorcha_register_subscribe";
    private const string ServiceName = "Peer";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IPeerServiceClient _peerClient;
    private readonly ILogger<RegisterSubscribeTool> _logger;

    public RegisterSubscribeTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IPeerServiceClient peerClient,
        ILogger<RegisterSubscribeTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _peerClient = peerClient;
        _logger = logger;
    }

    /// <summary>
    /// Subscribes the local node to a register so it replicates that register's dockets from peers.
    /// </summary>
    /// <param name="registerId">The register to subscribe to.</param>
    /// <param name="mode">Replication mode: "full-replica" (default) or "forward-only".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Subscription result with the register ID and requested mode.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Subscribes this Sorcha node to a register owned elsewhere on the peer network so it begins replicating that register's dockets. Call this when an operator needs a local replica of a federated register (for read queries, validation, or offline verification bundles) before any cross-node read will return data; use mode \"full-replica\" to store the entire ledger or \"forward-only\" to relay without retaining. This is the federation counterpart to sorcha_register_unsubscribe — it changes node membership, not register governance, so prefer it over governance tools when the goal is simply to start or stop following a register.")]
    public async Task<RegisterSubscribeResult> SubscribeAsync(
        [Description("The register ID to subscribe to")] string registerId,
        [Description("Replication mode: 'full-replica' (default) or 'forward-only'")] string mode = "full-replica",
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new RegisterSubscribeResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(registerId))
        {
            return new RegisterSubscribeResult
            {
                Status = "Error",
                Message = "Register ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        var normalisedMode = string.IsNullOrWhiteSpace(mode) ? "full-replica" : mode.Trim();
        if (normalisedMode is not ("full-replica" or "forward-only"))
        {
            return new RegisterSubscribeResult
            {
                Status = "Error",
                Message = "Mode must be 'full-replica' or 'forward-only'.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new RegisterSubscribeResult
            {
                Status = "Unavailable",
                Message = "Peer service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Subscribing to register {RegisterId} (mode {Mode})", registerId, normalisedMode);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _peerClient.SubscribeToRegisterAsync(registerId, normalisedMode, cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            return new RegisterSubscribeResult
            {
                Status = "Success",
                Message = $"Subscription request for register '{registerId}' submitted (mode {normalisedMode}).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                RegisterId = registerId,
                Mode = normalisedMode
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            _logger.LogWarning("Subscribe request timed out for register {RegisterId}", registerId);
            return new RegisterSubscribeResult
            {
                Status = "Timeout",
                Message = "Request to peer service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogError(ex, "Failed to subscribe to register {RegisterId}", registerId);
            return new RegisterSubscribeResult
            {
                Status = "Error",
                Message = $"Failed to subscribe to register: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>
/// Result of a register subscription request.
/// </summary>
public sealed record RegisterSubscribeResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The register that was subscribed to (on success).</summary>
    public string? RegisterId { get; init; }

    /// <summary>The replication mode requested (on success).</summary>
    public string? Mode { get; init; }
}
