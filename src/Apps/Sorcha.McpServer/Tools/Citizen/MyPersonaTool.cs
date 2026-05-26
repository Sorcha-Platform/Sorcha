// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tools.Citizen;

/// <summary>
/// Consumer-tier citizen self-service tool (Feature 140 Wave 3). Reads or replaces the
/// calling citizen's persona (self-asserted identity attributes used for form autofill)
/// via the typed <see cref="ITenantServiceClient"/>. One tool, two modes: omit the body to
/// read, supply the body to replace.
/// </summary>
[McpServerToolType]
public sealed class MyPersonaTool
{
    private const string ToolName = "sorcha_my_persona";
    private const string ServiceName = "Tenant";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ITenantServiceClient _tenantClient;
    private readonly ILogger<MyPersonaTool> _logger;

    public MyPersonaTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ITenantServiceClient tenantClient,
        ILogger<MyPersonaTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _tenantClient = tenantClient;
        _logger = logger;
    }

    /// <summary>
    /// Reads (no body) or replaces (with body) the calling citizen's persona.
    /// </summary>
    /// <param name="personaJson">When supplied, a PersonaAttributesV1 JSON body to fully replace the persona; omit to read.</param>
    /// <param name="context">Optional organisation-context id; omit for the Personal context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persona read-model.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Read or replace the calling citizen's own persona — the self-asserted identity attributes (name, date of birth, emails, phones, addresses, nationalities) Sorcha uses to autofill application forms. Omit the personaJson argument to READ the current persona; supply a PersonaAttributesV1 JSON body to fully REPLACE it (a full replace, not a merge). The persona is always scoped to the caller by the platform from the forwarded token; call this before starting a council or service application so the agent can confirm or update the details that will be prefilled, rather than entering them by hand each time.")]
    public async Task<MyPersonaResult> InvokeAsync(
        [Description("PersonaAttributesV1 JSON body to fully replace the persona; omit to read")] string? personaJson = null,
        [Description("Optional organisation-context id; omit for the Personal context")] string? context = null,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new MyPersonaResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires a consumer-tier (citizen) token.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new MyPersonaResult
            {
                Status = "Unavailable",
                Message = "Tenant service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        var queryString = string.IsNullOrWhiteSpace(context)
            ? null
            : $"context={Uri.EscapeDataString(context.Trim())}";
        var isUpdate = !string.IsNullOrWhiteSpace(personaJson);

        _logger.LogInformation("Citizen persona {Mode}", isUpdate ? "replace" : "read");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var body = isUpdate
                ? await _tenantClient.ReplaceMyPersonaAsync(personaJson!, queryString, cancellationToken)
                : await _tenantClient.GetMyPersonaAsync(queryString, cancellationToken);
            stopwatch.Stop();

            if (body is null)
            {
                _availabilityTracker.RecordFailure(ServiceName);
                return new MyPersonaResult
                {
                    Status = "Error",
                    Message = isUpdate
                        ? "Tenant service rejected the persona replacement (validation or context error)."
                        : "Tenant service returned no persona.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess(ServiceName);
            return new MyPersonaResult
            {
                Status = "Success",
                Message = isUpdate ? "Persona replaced." : "Persona retrieved.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Updated = isUpdate,
                Persona = body
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            return new MyPersonaResult
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
            _logger.LogError(ex, "Failed citizen persona {Mode}", isUpdate ? "replace" : "read");
            return new MyPersonaResult
            {
                Status = "Error",
                Message = $"Failed to {(isUpdate ? "replace" : "read")} persona: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>Result of reading or replacing the calling citizen's persona.</summary>
public sealed record MyPersonaResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>True when the call replaced the persona; false for a read.</summary>
    public bool Updated { get; init; }

    /// <summary>The persona read-model JSON body (PersonaReadModelV1) on success.</summary>
    public string? Persona { get; init; }
}
