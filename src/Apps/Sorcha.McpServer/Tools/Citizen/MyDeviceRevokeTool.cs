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
/// Consumer-tier citizen self-service tool (Feature 140 Wave 3). Revokes one of the
/// calling citizen's own enrolled wallet devices via the typed <see cref="ICitizenWalletClient"/>.
/// </summary>
[McpServerToolType]
public sealed class MyDeviceRevokeTool
{
    private const string ToolName = "sorcha_my_device_revoke";
    private const string ServiceName = "Wallet";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ICitizenWalletClient _walletClient;
    private readonly ILogger<MyDeviceRevokeTool> _logger;

    public MyDeviceRevokeTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ICitizenWalletClient walletClient,
        ILogger<MyDeviceRevokeTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _walletClient = walletClient;
        _logger = logger;
    }

    /// <summary>
    /// Revokes one of the calling citizen's own enrolled wallet devices.
    /// </summary>
    /// <param name="deviceId">The device id to revoke (must belong to the caller).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Revocation result.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Revoke one of the calling citizen's own enrolled wallet devices, permanently barring it from making presentations (the citizen must enrol a fresh device afterwards). The device must belong to the caller — the platform scopes the operation from the forwarded token, so an unknown or non-owned device id returns NotFound rather than affecting another account. Call this when a device is lost, stolen, or retired; use sorcha_my_device_rename instead when the device is still trusted and only its label should change.")]
    public async Task<MyDeviceRevokeResult> RevokeAsync(
        [Description("The device id to revoke (must belong to the caller)")] Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new MyDeviceRevokeResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires a consumer-tier (citizen) token.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (deviceId == Guid.Empty)
        {
            return new MyDeviceRevokeResult
            {
                Status = "Error",
                Message = "Device id is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new MyDeviceRevokeResult
            {
                Status = "Unavailable",
                Message = "Wallet service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Revoking citizen device {DeviceId}", deviceId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var revoked = await _walletClient.RevokeDeviceAsync(deviceId, cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            return revoked
                ? new MyDeviceRevokeResult
                {
                    Status = "Success",
                    Message = $"Device '{deviceId}' revoked.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    DeviceId = deviceId
                }
                : new MyDeviceRevokeResult
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
            return new MyDeviceRevokeResult
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
            _logger.LogError(ex, "Failed to revoke citizen device {DeviceId}", deviceId);
            return new MyDeviceRevokeResult
            {
                Status = "Error",
                Message = $"Failed to revoke device: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>Result of revoking the calling citizen's device.</summary>
public sealed record MyDeviceRevokeResult
{
    /// <summary>Status: Success, NotFound, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The device that was revoked (on success).</summary>
    public Guid? DeviceId { get; init; }
}
