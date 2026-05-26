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
/// Consumer-tier citizen self-service tool (Feature 140 Wave 3). Renames one of the
/// calling citizen's own enrolled wallet devices via the typed <see cref="ICitizenWalletClient"/>.
/// </summary>
[McpServerToolType]
public sealed class MyDeviceRenameTool
{
    private const string ToolName = "sorcha_my_device_rename";
    private const string ServiceName = "Wallet";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ICitizenWalletClient _walletClient;
    private readonly ILogger<MyDeviceRenameTool> _logger;

    public MyDeviceRenameTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ICitizenWalletClient walletClient,
        ILogger<MyDeviceRenameTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _walletClient = walletClient;
        _logger = logger;
    }

    /// <summary>
    /// Renames one of the calling citizen's own enrolled wallet devices.
    /// </summary>
    /// <param name="deviceId">The device id to rename (must belong to the caller).</param>
    /// <param name="label">The new label for the device.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Rename result.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Rename one of the calling citizen's own enrolled wallet devices, setting a new human-readable label. The device must belong to the caller — the platform scopes the operation from the forwarded token, so a device id that is unknown or owned by someone else returns NotFound rather than touching another account. Call this when the citizen wants to relabel a device (for example after replacing a phone); use sorcha_my_device_revoke instead when the goal is to stop a lost or retired device from being used.")]
    public async Task<MyDeviceRenameResult> RenameAsync(
        [Description("The device id to rename (must belong to the caller)")] Guid deviceId,
        [Description("The new label for the device")] string label,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new MyDeviceRenameResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires a consumer-tier (citizen) token.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (deviceId == Guid.Empty)
        {
            return new MyDeviceRenameResult
            {
                Status = "Error",
                Message = "Device id is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            return new MyDeviceRenameResult
            {
                Status = "Error",
                Message = "A non-empty label is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new MyDeviceRenameResult
            {
                Status = "Unavailable",
                Message = "Wallet service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Renaming citizen device {DeviceId}", deviceId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var renamed = await _walletClient.RenameDeviceAsync(deviceId, label.Trim(), cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            return renamed
                ? new MyDeviceRenameResult
                {
                    Status = "Success",
                    Message = $"Device '{deviceId}' renamed to '{label.Trim()}'.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    DeviceId = deviceId,
                    Label = label.Trim()
                }
                : new MyDeviceRenameResult
                {
                    Status = "NotFound",
                    Message = $"Device '{deviceId}' was not found or is not owned by the caller.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            return new MyDeviceRenameResult
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
            _logger.LogError(ex, "Failed to rename citizen device {DeviceId}", deviceId);
            return new MyDeviceRenameResult
            {
                Status = "Error",
                Message = $"Failed to rename device: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>Result of renaming the calling citizen's device.</summary>
public sealed record MyDeviceRenameResult
{
    /// <summary>Status: Success, NotFound, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The device that was renamed (on success).</summary>
    public Guid? DeviceId { get; init; }

    /// <summary>The new label (on success).</summary>
    public string? Label { get; init; }
}
