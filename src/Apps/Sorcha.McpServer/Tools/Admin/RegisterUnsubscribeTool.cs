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
/// Administrator tool that unsubscribes the local node from a register, stopping replication.
/// Routes through the typed <see cref="IPeerServiceClient"/> (spec 139 US4) so the caller's
/// bearer is forwarded and the route is contract-pinned.
/// </summary>
[McpServerToolType]
public sealed class RegisterUnsubscribeTool
{
    private const string ToolName = "sorcha_register_unsubscribe";
    private const string ServiceName = "Peer";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IPeerServiceClient _peerClient;
    private readonly ILogger<RegisterUnsubscribeTool> _logger;

    public RegisterUnsubscribeTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IPeerServiceClient peerClient,
        ILogger<RegisterUnsubscribeTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _peerClient = peerClient;
        _logger = logger;
    }

    /// <summary>
    /// Unsubscribes the local node from a register and stops replicating its dockets.
    /// </summary>
    /// <param name="registerId">The register to unsubscribe from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Unsubscription result with the register ID.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Unsubscribes this Sorcha node from a register it was replicating, so it stops pulling new dockets and (for full replicas) ceases retaining the ledger locally. Call this when an operator is decommissioning a local replica or freeing storage for a federated register the node no longer needs to follow; it is the reverse of sorcha_register_subscribe and affects only this node's membership, never the register's governance or other peers' copies. After this returns, cross-node reads against the register from this node will no longer reflect fresh data.")]
    public async Task<RegisterUnsubscribeResult> UnsubscribeAsync(
        [Description("The register ID to unsubscribe from")] string registerId,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new RegisterUnsubscribeResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(registerId))
        {
            return new RegisterUnsubscribeResult
            {
                Status = "Error",
                Message = "Register ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new RegisterUnsubscribeResult
            {
                Status = "Unavailable",
                Message = "Peer service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Unsubscribing from register {RegisterId}", registerId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _peerClient.UnsubscribeFromRegisterAsync(registerId, cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            return new RegisterUnsubscribeResult
            {
                Status = "Success",
                Message = $"Unsubscription request for register '{registerId}' submitted.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                RegisterId = registerId
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            _logger.LogWarning("Unsubscribe request timed out for register {RegisterId}", registerId);
            return new RegisterUnsubscribeResult
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
            _logger.LogError(ex, "Failed to unsubscribe from register {RegisterId}", registerId);
            return new RegisterUnsubscribeResult
            {
                Status = "Error",
                Message = $"Failed to unsubscribe from register: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>
/// Result of a register unsubscription request.
/// </summary>
public sealed record RegisterUnsubscribeResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The register that was unsubscribed from (on success).</summary>
    public string? RegisterId { get; init; }
}
