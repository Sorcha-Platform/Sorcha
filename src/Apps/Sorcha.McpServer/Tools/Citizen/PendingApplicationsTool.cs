// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.CitizenWallet;

namespace Sorcha.McpServer.Tools.Citizen;

/// <summary>
/// Consumer-tier citizen self-service tool (Feature 140 Wave 3). Reads the calling
/// citizen's pending-application notice (Feature 124) via the typed
/// <see cref="ICitizenWalletClient"/>.
/// </summary>
[McpServerToolType]
public sealed class PendingApplicationsTool
{
    private const string ToolName = "sorcha_pending_applications";
    private const string ServiceName = "Wallet";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ICitizenWalletClient _walletClient;
    private readonly ILogger<PendingApplicationsTool> _logger;

    public PendingApplicationsTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ICitizenWalletClient walletClient,
        ILogger<PendingApplicationsTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _walletClient = walletClient;
        _logger = logger;
    }

    /// <summary>
    /// Reads the calling citizen's pending-application notice.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pending-application notice, or an indication that none is in flight.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Read whether the calling citizen has an application in flight whose credential has not yet landed in their wallet (Feature 124) — returns the human-readable application label and the time the notice was set, or indicates that nothing is pending. The notice is scoped to the caller by the platform from the forwarded token. Call this when an agent wants to tell the citizen to 'watch their wallet' or explain why a credential has not arrived yet; use sorcha_my_credentials instead to see credentials that have already been issued rather than ones still being processed.")]
    public async Task<PendingApplicationsResult> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new PendingApplicationsResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires a consumer-tier (citizen) token.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new PendingApplicationsResult
            {
                Status = "Unavailable",
                Message = "Wallet service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Reading the calling citizen's pending-application notice");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _walletClient.GetPendingApplicationAsync(cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            var notice = response.Notice;
            return new PendingApplicationsResult
            {
                Status = "Success",
                Message = notice is null
                    ? "No application is currently pending."
                    : $"A pending application is in flight: '{notice.Label}'.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                HasPending = notice is not null,
                Label = notice?.Label,
                SetAt = notice?.SetAt
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            return new PendingApplicationsResult
            {
                Status = "Timeout",
                Message = "Request to wallet service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogError(ex, "Failed to read the citizen's pending-application notice");
            return new PendingApplicationsResult
            {
                Status = "Error",
                Message = $"Failed to read pending applications: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>Result of reading the calling citizen's pending-application notice.</summary>
public sealed record PendingApplicationsResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>True when an application is currently pending.</summary>
    public bool HasPending { get; init; }

    /// <summary>Human-readable application label (when pending).</summary>
    public string? Label { get; init; }

    /// <summary>UTC time the notice was set (when pending).</summary>
    public DateTimeOffset? SetAt { get; init; }
}
