// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Administrator tool answering "who governs this register?" — every named role holder (Owner,
/// Admin, Designer, Auditor) on a register's governance roster, not just the caller's own derived
/// standing (#1647).
/// </summary>
/// <remarks>
/// <para>
/// In the MCP cold-start run on n1 (2026-09-15), an agent refused by a governance/publish 403 had
/// no MCP path to the register's Owner. It SSH'd to the node and decoded the raw genesis
/// transaction from MongoDB, guessing the wrong identity field first. This tool exposes the same
/// data <c>GET /api/registers/{registerId}/governance/roster</c> already returns, through
/// <see cref="IRegisterServiceClient.GetGovernanceRosterAsync"/> (built for #1659's publish-refusal
/// fix, but never surfaced as an MCP tool until now).
/// </para>
/// <para>
/// <b>Distinct from <see cref="RegisterRelationshipTool"/>.</b> That tool answers "what can I (or
/// this node) do here?" — the caller's own derived role set. This tool answers "who can approve
/// this?" — every subject with a named role, so an agent refused by a governance action can find
/// the Owner/Admin/Designer to escalate to without decoding ledger internals.
/// </para>
/// <para>
/// <b>Entitlement matches <c>sorcha_register_relationship</c> exactly</b> (platform tier + the
/// <c>sorcha:admin</c> role) rather than inventing a combined admin-or-designer entitlement:
/// <see cref="ToolEntitlements"/> models one required role per tool, not a set, and the endpoint
/// behind both tools sits behind the same Register Service policy, <c>CanReadTransactions</c>. The
/// people this tool serves — an agent that hit a governance or publish 403 — are, by construction,
/// already entitled to attempt the admin surface; a designer-only caller cannot reach the refusal
/// this tool exists to unblock in the first place (<c>sorcha_blueprint_publish</c> itself requires
/// the admin role — see <c>BlueprintPublishTool</c>).
/// </para>
/// <para>
/// <b>Three outcomes, not two.</b> <see cref="GovernanceRosterLookupStatus.NotFound"/> (a register
/// with no sealed genesis yet, or an unknown id) is reported as exactly that — never "does not
/// exist", since a brand-new register's genesis can still be sealing. A 4xx from the Register
/// Service (a 403 lacking <c>CanReadTransactions</c>, a 401 expired token) is a REFUSAL, not an
/// outage, and does not count against the service's availability tracker — only a 5xx or a
/// transport exception does (mirrors <c>PublishGate</c> and #1673's lesson).
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class RegisterGovernorsTool
{
    private const string ToolName = "sorcha_register_governors";
    private const string ServiceName = "Register";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IRegisterServiceClient _registerClient;
    private readonly ILogger<RegisterGovernorsTool> _logger;

    /// <summary>Creates the tool.</summary>
    public RegisterGovernorsTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IRegisterServiceClient registerClient,
        ILogger<RegisterGovernorsTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _registerClient = registerClient;
        _logger = logger;
    }

    /// <summary>Gets every named role holder on a register's governance roster.</summary>
    /// <param name="registerId">The register to inspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The roster's members, or a status explaining why they could not be read.</returns>
    [McpServerTool(Name = ToolName, Destructive = false, ReadOnly = true, Idempotent = true)]
    [Description("Answers \"who governs this register?\" — every named role holder (Owner, Admin, Designer, Auditor) on a register's governance roster, reconstructed from its sealed Control transactions. Call this when a governance action or sorcha_blueprint_publish is refused with a 403: publishing and governance changes need an Owner, Admin or Designer entry here (plus organisation-administrator authority), so this is the obvious next read — it names who can approve or perform the action instead, without decoding ledger internals over SSH. Prefer this over sorcha_register_relationship when the question is who ELSE can act, not what the caller can do: that tool reports only the CALLER's (or this node's) own derived role on the register, never the other role holders. Returns NotFound when the register has no sealed governance roster yet — which happens for an unknown register id, or a brand-new one whose genesis has not sealed (usually a few seconds) — never reported as the register not existing.")]
    public async Task<RegisterGovernorsResult> GetGovernorsAsync(
        [Description("The register ID to inspect")] string registerId,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new RegisterGovernorsResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(registerId))
        {
            return new RegisterGovernorsResult
            {
                Status = "Error",
                Message = "Register ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new RegisterGovernorsResult
            {
                Status = "Unavailable",
                Message = "Register service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Getting governance roster for register {RegisterId}", registerId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var lookup = await _registerClient.GetGovernanceRosterAsync(registerId, cancellationToken);
            stopwatch.Stop();

            switch (lookup.Status)
            {
                case GovernanceRosterLookupStatus.Found:
                {
                    _availabilityTracker.RecordSuccess(ServiceName);
                    var roster = lookup.Roster!;
                    var members = roster.Members
                        .OrderBy(RoleRank)
                        .ThenBy(m => m.Subject, StringComparer.Ordinal)
                        .ToList();

                    return new RegisterGovernorsResult
                    {
                        Status = "Success",
                        Message = $"Register '{registerId}' governance roster: {Summarize(members)}.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                        RegisterId = roster.RegisterId,
                        Members = members,
                        MemberCount = roster.MemberCount,
                        ControlTransactionCount = roster.ControlTransactionCount,
                        LastControlTxId = roster.LastControlTxId
                    };
                }

                case GovernanceRosterLookupStatus.NotFound:
                    // #1659 lesson, restated for this tool: a 404 here means no SEALED governance
                    // roster — an unknown register id, or one whose genesis has not sealed yet
                    // (typically a few seconds for a new register) — never that the register does
                    // not exist. This does not count against the service's availability.
                    _availabilityTracker.RecordSuccess(ServiceName);
                    return new RegisterGovernorsResult
                    {
                        Status = "NotFound",
                        Message = $"Register '{registerId}' has no sealed governance roster yet. This is not "
                            + "evidence the register is unknown or missing — it can also mean the register is "
                            + "new and its genesis has not sealed yet (usually a few seconds): retry shortly.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };

                default: // Unavailable
                    return Unavailable(registerId, lookup.HttpStatus, stopwatch);
            }
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            _logger.LogWarning("Governance roster query timed out for register {RegisterId}", registerId);
            return new RegisterGovernorsResult
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
            _logger.LogError(ex, "Failed to get governance roster for register {RegisterId}", registerId);
            return new RegisterGovernorsResult
            {
                Status = "Error",
                Message = $"Failed to get governance roster: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// Turns a Register Service failure status into the outcome this tool reports. A 4xx is the
    /// service answering — a refusal or an authentication problem, not an outage — so it does NOT
    /// call <see cref="IServiceAvailabilityTracker.RecordFailure(string, Exception?)"/>; only a 5xx
    /// or a transport-level exception (caught above) counts against availability (#1673).
    /// </summary>
    private RegisterGovernorsResult Unavailable(string registerId, int? httpStatus, Stopwatch stopwatch)
    {
        if (httpStatus is >= 500 or null)
        {
            _availabilityTracker.RecordFailure(ServiceName);
            _logger.LogWarning(
                "Governance roster for register {RegisterId} could not be read (status {Status})",
                registerId, httpStatus?.ToString() ?? "transport error");

            return new RegisterGovernorsResult
            {
                Status = "Error",
                Message = $"Could not read the governance roster for register '{registerId}'"
                    + (httpStatus is { } code ? $" (the Register Service returned HTTP {code})" : " (the Register Service could not be reached)")
                    + ". Try again shortly.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }

        // A deterministic 4xx: the service answered. This is a refusal, not an outage — recorded
        // as a success from the availability tracker's point of view (the service IS up).
        _availabilityTracker.RecordSuccess(ServiceName);

        var status = httpStatus == 401 ? "Unauthorized" : "Refused";
        var message = httpStatus == 401
            ? "Your session is not authenticated (HTTP 401). The access token has probably expired; reconnect and retry."
            : $"You are not permitted to read register '{registerId}' governance roster (HTTP {httpStatus}). "
              + "This is an authorisation refusal, not a missing resource or an outage.";

        return new RegisterGovernorsResult
        {
            Status = status,
            Message = message,
            CheckedAt = DateTimeOffset.UtcNow,
            ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
        };
    }

    /// <summary>Owner first, then Admin, Designer, Auditor, then anything unrecognised.</summary>
    private static int RoleRank(RosterMember member) =>
        Enum.TryParse<RegisterRole>(member.Role, ignoreCase: true, out var role)
            ? role switch
            {
                RegisterRole.Owner => 0,
                RegisterRole.Admin => 1,
                RegisterRole.Designer => 2,
                RegisterRole.Auditor => 3,
                _ => 4
            }
            : 5;

    private static string Summarize(IReadOnlyCollection<RosterMember> members) =>
        members.Count == 0
            ? "no members"
            : string.Join(", ", members
                .GroupBy(m => m.Role, StringComparer.Ordinal)
                .OrderBy(g => Enum.TryParse<RegisterRole>(g.Key, ignoreCase: true, out var role) ? (int)role : int.MaxValue)
                .Select(g => $"{g.Count()} {g.Key}"));
}

/// <summary>
/// Result of a register governance-roster query (#1647).
/// </summary>
public sealed record RegisterGovernorsResult
{
    /// <summary>Status: Success, NotFound, Refused, Unauthorized, Error, Unavailable, or Timeout.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the query was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The register the roster belongs to (on success).</summary>
    public string? RegisterId { get; init; }

    /// <summary>The roster's members, Owner first (on success).</summary>
    public IReadOnlyList<RosterMember>? Members { get; init; }

    /// <summary>Total member count (on success).</summary>
    public int? MemberCount { get; init; }

    /// <summary>How many Control transactions the roster was reconstructed from (on success).</summary>
    public int? ControlTransactionCount { get; init; }

    /// <summary>The most recent Control transaction id (on success).</summary>
    public string? LastControlTxId { get; init; }
}
