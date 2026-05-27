// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.Register.Models.LocalRelationship;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Administrator tool returning the typed sync-state view for a register and the evidence
/// that derived it (Feature 108). Routes through the typed <see cref="IRegisterServiceClient"/>.
/// </summary>
[McpServerToolType]
public sealed class RegisterSyncStateTool
{
    private const string ToolName = "sorcha_register_sync_state";
    private const string ServiceName = "Register";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IRegisterServiceClient _registerClient;
    private readonly ILogger<RegisterSyncStateTool> _logger;

    public RegisterSyncStateTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IRegisterServiceClient registerClient,
        ILogger<RegisterSyncStateTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _registerClient = registerClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the local node's sync state for a register and the inputs that derived it.
    /// </summary>
    /// <param name="registerId">The register to inspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The typed sync-state view, or a NotFound result if the register is not held locally.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Returns this node's replication sync state for a register (synced, recovering, stalled, or error) together with the evidence that produced it: the local docket height, the network high-water-mark claimed by peers, how many distinct peers were observed, and the freshest advert timestamp. Call this to decide whether a locally-held register is current enough to read from before consuming transactions, issuing verification bundles, or approving actions — a stalled or recovering register may be missing recently-sealed dockets. Prefer this over sorcha_register_stats when the question is freshness/health of replication rather than how much data the register holds; returns NotFound when the register is not subscribed locally (subscribe first with sorcha_register_subscribe).")]
    public async Task<RegisterSyncStateResult> GetSyncStateAsync(
        [Description("The register ID to inspect")] string registerId,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new RegisterSyncStateResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(registerId))
        {
            return new RegisterSyncStateResult
            {
                Status = "Error",
                Message = "Register ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new RegisterSyncStateResult
            {
                Status = "Unavailable",
                Message = "Register service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Getting sync state for register {RegisterId}", registerId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var view = await _registerClient.GetSyncStateAsync(registerId, cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            if (view is null)
            {
                return new RegisterSyncStateResult
                {
                    Status = "NotFound",
                    Message = $"Register '{registerId}' is not known/held locally.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            return new RegisterSyncStateResult
            {
                Status = "Success",
                Message = $"Register '{registerId}' sync state: {view.State}.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                SyncState = view
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            _logger.LogWarning("Sync-state query timed out for register {RegisterId}", registerId);
            return new RegisterSyncStateResult
            {
                Status = "Timeout",
                Message = "Request to register service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogError(ex, "Failed to get sync state for register {RegisterId}", registerId);
            return new RegisterSyncStateResult
            {
                Status = "Error",
                Message = $"Failed to get sync state: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>
/// Result of a register sync-state query.
/// </summary>
public sealed record RegisterSyncStateResult
{
    /// <summary>Status: Success, NotFound, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the query was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The typed sync-state view (on success).</summary>
    public RegisterSyncStateView? SyncState { get; init; }
}
