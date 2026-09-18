// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tools.Participant;

/// <summary>
/// Lists the participant records published to a register: which blueprint roles are bound to which
/// wallets, and therefore which roles can send an action (#1664).
/// </summary>
[McpServerToolType]
public sealed class ParticipantListTool
{
    private const string ToolName = "sorcha_participant_list";
    private const string ServiceName = "Register";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IRegisterServiceClient _registerClient;
    private readonly ILogger<ParticipantListTool> _logger;

    public ParticipantListTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IRegisterServiceClient registerClient,
        ILogger<ParticipantListTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _registerClient = registerClient;
        _logger = logger;
    }

    /// <summary>Lists the participant records published to a register.</summary>
    /// <param name="registerId">The register to read.</param>
    /// <param name="includeRevoked">Include deprecated and revoked records as well as active ones.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Items per page (default 20, max 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [McpServerTool(Name = ToolName)]
    [Description("List the participant records published to a register: which blueprint participants (roles) are bound to which wallet addresses, by which organisation, and whether each record is active. A role with no record here cannot send an action — a submission would be accepted and then refused by the validator — so call this when an action reports that it is awaiting a participant record, to confirm the binding has appeared, and after sorcha_participant_publish to confirm the record has sealed (publishing is a register transaction and takes a few seconds). Use sorcha_register_query instead when you want the register's transactions rather than who is bound to it. An empty list means no records are published yet, not that the register is unreachable.")]
    public async Task<ParticipantListResult> ListParticipantsAsync(
        [Description("The register to read")] string registerId,
        [Description("Include deprecated and revoked records too (default false: active only)")] bool includeRevoked = false,
        [Description("Page number (1-based, default 1)")] int page = 1,
        [Description("Items per page (default 20, max 100)")] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return Fail("Unauthorized", "Access denied. This tool requires an authenticated consumer- or platform-tier caller.");
        }

        if (string.IsNullOrWhiteSpace(registerId))
        {
            return Fail("Error", "A registerId is required.");
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return Fail("Unavailable", "Register service is currently unavailable. Please try again later.");
        }

        var clampedPage = Math.Max(1, page);
        var clampedPageSize = Math.Clamp(pageSize, 1, 100);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await _registerClient.GetPublishedParticipantsAsync(
                registerId,
                skip: (clampedPage - 1) * clampedPageSize,
                top: clampedPageSize,
                statusFilter: includeRevoked ? "all" : "active",
                cancellationToken);
            _availabilityTracker.RecordSuccess(ServiceName);

            var participants = result.Participants.Select(p => new PublishedParticipantSummary
            {
                ParticipantName = p.ParticipantName,
                OrganisationName = p.OrganizationName,
                Status = p.Status,
                WalletAddresses = p.Addresses.Select(a => a.WalletAddress).ToList(),
                RecordId = p.ParticipantId,
            }).ToList();

            _logger.LogInformation(
                "Listed {Count} published participant(s) on register {RegisterId}", participants.Count, registerId);

            return new ParticipantListResult
            {
                Status = "Success",
                Message = participants.Count == 0
                    ? $"No participant records are published on register {registerId} yet, so no role is bound to a "
                      + "wallet there. A role must have a record before it can send an action."
                    : $"{participants.Count} of {result.Total} published participant record(s) on register {registerId}. "
                      + "A role is bound by the record whose name matches it.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                RegisterId = registerId,
                Participants = participants,
                TotalCount = result.Total,
                Page = clampedPage,
                PageSize = clampedPageSize,
            };
        }
        catch (TaskCanceledException)
        {
            _availabilityTracker.RecordFailure(ServiceName);
            return Fail("Timeout", "Request to the register service timed out.");
        }
        catch (HttpRequestException ex)
        {
            _availabilityTracker.RecordFailure(ServiceName, ex);
            return Fail("Error", $"Failed to connect to the register service: {ex.Message}");
        }
        catch (Exception ex)
        {
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogError(ex, "Unexpected error listing published participants");
            return Fail("Error", "An unexpected error occurred while listing published participants.");
        }
    }

    private static ParticipantListResult Fail(string status, string message) => new()
    {
        Status = status,
        Message = message,
        CheckedAt = DateTimeOffset.UtcNow,
    };
}

/// <summary>Result of listing a register's published participant records.</summary>
public sealed record ParticipantListResult
{
    /// <summary>Operation status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the operation result.</summary>
    public required string Message { get; init; }

    /// <summary>When the operation was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The register that was read.</summary>
    public string? RegisterId { get; init; }

    /// <summary>The published participant records on this page.</summary>
    public IReadOnlyList<PublishedParticipantSummary> Participants { get; init; } = [];

    /// <summary>Total records available, across all pages.</summary>
    public int TotalCount { get; init; }

    /// <summary>Page number (1-based).</summary>
    public int Page { get; init; }

    /// <summary>Items per page.</summary>
    public int PageSize { get; init; }
}

/// <summary>One participant record published to a register.</summary>
public sealed record PublishedParticipantSummary
{
    /// <summary>
    /// The record's name. This is what a blueprint participant (role) id is matched against, so it is the
    /// role this record binds.
    /// </summary>
    public required string ParticipantName { get; init; }

    /// <summary>The organisation that published the record.</summary>
    public required string OrganisationName { get; init; }

    /// <summary>Record status: Active, Deprecated or Revoked. Only an active record binds a role.</summary>
    public required string Status { get; init; }

    /// <summary>The wallet addresses bound to this role. Any of them may sign that role's actions.</summary>
    public IReadOnlyList<string> WalletAddresses { get; init; } = [];

    /// <summary>
    /// The record's own identifier, assigned by the Tenant Service. NOT the blueprint role id — see
    /// <see cref="ParticipantName"/> for that.
    /// </summary>
    public required string RecordId { get; init; }
}
