// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Invitation;

namespace Sorcha.McpServer.Tools.Citizen;

/// <summary>
/// Consumer-tier citizen self-service tool (Feature 140 Wave 3). Lists the calling
/// citizen's register invitations via the typed <see cref="IRegisterInvitationServiceClient"/>.
/// The organisation is derived from the caller's <c>org_id</c> claim — there is no
/// cross-citizen org parameter.
/// </summary>
[McpServerToolType]
public sealed class MyInvitationsTool
{
    private const string ToolName = "sorcha_my_invitations";
    private const string ServiceName = "Tenant";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IRegisterInvitationServiceClient _invitationClient;
    private readonly ICallerContext _caller;
    private readonly ILogger<MyInvitationsTool> _logger;

    public MyInvitationsTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IRegisterInvitationServiceClient invitationClient,
        ICallerContext caller,
        ILogger<MyInvitationsTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _invitationClient = invitationClient;
        _caller = caller;
        _logger = logger;
    }

    /// <summary>
    /// Lists the calling citizen's register invitations.
    /// </summary>
    /// <param name="direction">Which invitations to list: "received" (default), "sent", or "all".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The citizen's invitations.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("List the register invitations for the calling citizen's own organisation — each entry carries the invitation id, the register and its name, the source and target organisations, the direction, the status (pending / accepted / revoked / expired), and the expiry. The organisation is derived from the caller's token (org_id); there is no parameter to read another organisation's invitations. Call this when the citizen asks what register invitations are waiting for them; pass direction 'received' (the default) for invitations addressed to them, 'sent' for ones they issued, or 'all' rather than guessing.")]
    public async Task<MyInvitationsResult> ListAsync(
        [Description("Which invitations to list: 'received' (default), 'sent', or 'all'")] string direction = "received",
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new MyInvitationsResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires a consumer-tier (citizen) token.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        var normalisedDirection = string.IsNullOrWhiteSpace(direction) ? "received" : direction.Trim().ToLowerInvariant();
        if (normalisedDirection is not ("received" or "sent" or "all"))
        {
            return new MyInvitationsResult
            {
                Status = "Error",
                Message = "Direction must be 'received', 'sent', or 'all'.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!Guid.TryParse(_caller.OrganizationId, out var orgId) || orgId == Guid.Empty)
        {
            return new MyInvitationsResult
            {
                Status = "Error",
                Message = "The caller's token does not carry an organisation (org_id); cannot list invitations.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new MyInvitationsResult
            {
                Status = "Unavailable",
                Message = "Tenant service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Listing invitations for the caller's organisation (direction {Direction})", normalisedDirection);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _invitationClient.ListAsync(orgId, normalisedDirection, cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            var invitations = response.Invitations
                .Select(i => new MyInvitationSummary
                {
                    InvitationId = i.InvitationId,
                    RegisterId = i.RegisterId,
                    RegisterName = i.RegisterName,
                    SourceOrgName = i.SourceOrgName,
                    TargetOrgName = i.TargetOrgName,
                    Direction = i.Direction,
                    Status = i.Status,
                    ExpiresAt = i.ExpiresAt,
                    CreatedAt = i.CreatedAt
                })
                .ToList();

            return new MyInvitationsResult
            {
                Status = "Success",
                Message = $"Retrieved {invitations.Count} invitation(s).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Invitations = invitations
            };
        }
        catch (InvitationApiException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogWarning(ex, "Invitation listing rejected ({StatusCode})", ex.StatusCode);
            return new MyInvitationsResult
            {
                Status = "Error",
                Message = $"Failed to list invitations: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            return new MyInvitationsResult
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
            _logger.LogError(ex, "Failed to list the citizen's invitations");
            return new MyInvitationsResult
            {
                Status = "Error",
                Message = $"Failed to list invitations: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>Result of listing the calling citizen's invitations.</summary>
public sealed record MyInvitationsResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The citizen's invitations (on success).</summary>
    public IReadOnlyList<MyInvitationSummary> Invitations { get; init; } = [];
}

/// <summary>Summary of one register invitation visible to the calling citizen's organisation.</summary>
public sealed record MyInvitationSummary
{
    /// <summary>Invitation identifier.</summary>
    public required string InvitationId { get; init; }

    /// <summary>Register identifier.</summary>
    public required string RegisterId { get; init; }

    /// <summary>Register name, if known.</summary>
    public string? RegisterName { get; init; }

    /// <summary>Source organisation name, if known.</summary>
    public string? SourceOrgName { get; init; }

    /// <summary>Target organisation name, if known.</summary>
    public string? TargetOrgName { get; init; }

    /// <summary>Direction (sent / received).</summary>
    public required string Direction { get; init; }

    /// <summary>Current status.</summary>
    public required string Status { get; init; }

    /// <summary>UTC expiry.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>UTC creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
