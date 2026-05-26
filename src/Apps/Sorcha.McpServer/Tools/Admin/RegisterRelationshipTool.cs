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
/// Administrator tool returning the local node's derived role set for a register (Feature 108).
/// Routes through the typed <see cref="IRegisterServiceClient"/>.
/// </summary>
[McpServerToolType]
public sealed class RegisterRelationshipTool
{
    private const string ToolName = "sorcha_register_relationship";
    private const string ServiceName = "Register";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IRegisterServiceClient _registerClient;
    private readonly ILogger<RegisterRelationshipTool> _logger;

    public RegisterRelationshipTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IRegisterServiceClient registerClient,
        ILogger<RegisterRelationshipTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _registerClient = registerClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the local node's derived relationship (role set) for a register.
    /// </summary>
    /// <param name="registerId">The register to inspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The derived relationship, or a NotFound result if the register is not held locally.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Returns the local node's derived authority on a register — whether it is the Owner, an Admin, a Validator on the roster, an Auditor, a Designer, or only a read-only Subscriber — as derived from the register's latest control record and this node's identity. Call this to answer \"what can this node actually do on this register?\" before attempting a governance change, a docket seal, or a revocation, since those require Owner/Admin/Validator authority and will be rejected otherwise. Prefer this over sorcha_register_sync_state when the question is authority rather than replication freshness; returns NotFound when the register is not known locally.")]
    public async Task<RegisterRelationshipResult> GetRelationshipAsync(
        [Description("The register ID to inspect")] string registerId,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new RegisterRelationshipResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(registerId))
        {
            return new RegisterRelationshipResult
            {
                Status = "Error",
                Message = "Register ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new RegisterRelationshipResult
            {
                Status = "Unavailable",
                Message = "Register service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Getting local relationship for register {RegisterId}", registerId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var relationship = await _registerClient.GetLocalRelationshipAsync(registerId, cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            if (relationship is null)
            {
                return new RegisterRelationshipResult
                {
                    Status = "NotFound",
                    Message = $"Register '{registerId}' is not known locally.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            return new RegisterRelationshipResult
            {
                Status = "Success",
                Message = $"Register '{registerId}' roles: {relationship.Roles}.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Relationship = relationship
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            _logger.LogWarning("Relationship query timed out for register {RegisterId}", registerId);
            return new RegisterRelationshipResult
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
            _logger.LogError(ex, "Failed to get local relationship for register {RegisterId}", registerId);
            return new RegisterRelationshipResult
            {
                Status = "Error",
                Message = $"Failed to get relationship: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>
/// Result of a register local-relationship query.
/// </summary>
public sealed record RegisterRelationshipResult
{
    /// <summary>Status: Success, NotFound, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the query was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The derived local relationship (on success).</summary>
    public RegisterLocalRelationship? Relationship { get; init; }
}
