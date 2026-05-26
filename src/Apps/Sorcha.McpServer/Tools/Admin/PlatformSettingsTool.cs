// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Administrator tool that reads the platform settings or toggles the public organisation
/// (Feature 140 Wave 4). One tool, two modes: omit the body to READ, supply
/// <c>publicOrgEnabled</c> to UPDATE the public-org self-registration toggle. Routes through
/// the typed <see cref="ITenantServiceClient"/> so the caller's bearer is forwarded.
/// </summary>
[McpServerToolType]
public sealed class PlatformSettingsTool
{
    private const string ToolName = "sorcha_platform_settings";
    private const string ServiceName = "Tenant";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ITenantServiceClient _tenantClient;
    private readonly ILogger<PlatformSettingsTool> _logger;

    public PlatformSettingsTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ITenantServiceClient tenantClient,
        ILogger<PlatformSettingsTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _tenantClient = tenantClient;
        _logger = logger;
    }

    /// <summary>
    /// Reads the platform settings (no argument) or toggles the public-org self-registration setting.
    /// </summary>
    /// <param name="publicOrgEnabled">When supplied, enables (true) or disables (false) the public org; omit to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The platform-settings read-model, or an error result.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Read the platform settings or toggle the public organisation that gates self-registration. Omit the publicOrgEnabled argument to READ the current settings (public-org status, max-orgs-per-user, last-updated); supply publicOrgEnabled=true/false to UPDATE — enabling sets the public org Active and self-registration on, disabling sets it Suspended and self-registration off. Call this before opening or closing the platform to public sign-ups; prefer this over sorcha_org_status, which changes one named organisation, when the goal is specifically the platform-wide public-registration gate rather than an arbitrary org.")]
    public async Task<PlatformSettingsResult> InvokeAsync(
        [Description("Enable (true) or disable (false) the public org; omit to read current settings")] bool? publicOrgEnabled = null,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new PlatformSettingsResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new PlatformSettingsResult
            {
                Status = "Unavailable",
                Message = "Tenant service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        var isUpdate = publicOrgEnabled.HasValue;
        _logger.LogInformation("Platform settings {Mode}", isUpdate ? $"update (publicOrgEnabled={publicOrgEnabled})" : "read");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var body = isUpdate
                ? await _tenantClient.UpdatePublicOrgAsync(
                    JsonSerializer.Serialize(new { enabled = publicOrgEnabled!.Value }), cancellationToken)
                : await _tenantClient.GetPlatformSettingsAsync(cancellationToken);
            stopwatch.Stop();

            if (body is null)
            {
                _availabilityTracker.RecordFailure(ServiceName);
                return new PlatformSettingsResult
                {
                    Status = "Error",
                    Message = isUpdate
                        ? "Tenant service rejected the public-org toggle."
                        : "Tenant service returned no platform settings.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess(ServiceName);
            return new PlatformSettingsResult
            {
                Status = "Success",
                Message = isUpdate ? "Public organisation toggled." : "Platform settings retrieved.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Updated = isUpdate,
                Settings = body
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            return new PlatformSettingsResult
            {
                Status = "Timeout",
                Message = "Request to tenant service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogError(ex, "Failed platform settings {Mode}", isUpdate ? "update" : "read");
            return new PlatformSettingsResult
            {
                Status = "Error",
                Message = $"Failed to {(isUpdate ? "update" : "read")} platform settings: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>Result of reading or updating the platform settings.</summary>
public sealed record PlatformSettingsResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>True when the call toggled the public org; false for a read.</summary>
    public bool Updated { get; init; }

    /// <summary>The platform-settings read-model JSON body on success.</summary>
    public string? Settings { get; init; }
}
