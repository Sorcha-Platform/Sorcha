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
/// Consumer-tier citizen self-service tool (Feature 140 Wave 3). Lists the calling
/// citizen's own enrolled wallet devices via the typed <see cref="ICitizenWalletClient"/>.
/// </summary>
[McpServerToolType]
public sealed class MyDevicesTool
{
    private const string ToolName = "sorcha_my_devices";
    private const string ServiceName = "Wallet";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ICitizenWalletClient _walletClient;
    private readonly ILogger<MyDevicesTool> _logger;

    public MyDevicesTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ICitizenWalletClient walletClient,
        ILogger<MyDevicesTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _walletClient = walletClient;
        _logger = logger;
    }

    /// <summary>
    /// Lists the calling citizen's own enrolled wallet devices.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The citizen's enrolled devices.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("List the wallet devices enrolled under the calling citizen's account — device id, label, platform descriptor, status (Active / Revoked), enrolment time, last-seen time, and delegation expiry. The list is scoped to the caller by the platform; there is no parameter to read another person's devices. Call this first when an agent intends to rename or revoke a device, so it can resolve the right device id; use sorcha_my_device_rename or sorcha_my_device_revoke to act on a device rather than this read-only listing.")]
    public async Task<MyDevicesResult> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new MyDevicesResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires a consumer-tier (citizen) token.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new MyDevicesResult
            {
                Status = "Unavailable",
                Message = "Wallet service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Listing the calling citizen's devices");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _walletClient.ListDevicesAsync(cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            var devices = response.Devices
                .Select(d => new MyDeviceSummary
                {
                    DeviceId = d.DeviceId,
                    Label = d.Label,
                    Platform = d.Platform,
                    Status = d.Status.ToString(),
                    EnrolledAt = d.EnrolledAt,
                    RevokedAt = d.RevokedAt,
                    LastSeenAt = d.LastSeenAt,
                    DelegationExpiresAt = d.DelegationExpiresAt
                })
                .ToList();

            return new MyDevicesResult
            {
                Status = "Success",
                Message = $"Retrieved {devices.Count} device(s).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Devices = devices
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            return new MyDevicesResult
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
            _logger.LogError(ex, "Failed to list the citizen's devices");
            return new MyDevicesResult
            {
                Status = "Error",
                Message = $"Failed to list devices: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>Result of listing the calling citizen's devices.</summary>
public sealed record MyDevicesResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The citizen's enrolled devices (on success).</summary>
    public IReadOnlyList<MyDeviceSummary> Devices { get; init; } = [];
}

/// <summary>Public summary of one of the citizen's enrolled devices.</summary>
public sealed record MyDeviceSummary
{
    /// <summary>Server-assigned device identifier.</summary>
    public Guid DeviceId { get; init; }

    /// <summary>Citizen-set label.</summary>
    public required string Label { get; init; }

    /// <summary>Free-form platform descriptor at enrolment.</summary>
    public required string Platform { get; init; }

    /// <summary>Active or Revoked.</summary>
    public required string Status { get; init; }

    /// <summary>UTC time of original enrolment.</summary>
    public DateTimeOffset EnrolledAt { get; init; }

    /// <summary>UTC time of revocation, if revoked.</summary>
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>UTC time of the most recent successful sync (or null if never).</summary>
    public DateTimeOffset? LastSeenAt { get; init; }

    /// <summary>UTC time at which the current delegation credential expires.</summary>
    public DateTimeOffset DelegationExpiresAt { get; init; }
}
