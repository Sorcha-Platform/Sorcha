// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Blueprint.Models;

// The project namespace Sorcha.McpServer shadows the SDK type
// ModelContextProtocol.Server.McpServer, so an unqualified `McpServer` in this file is CS0118
// ("namespace used like a type"). Alias it once rather than fully qualifying every mention.
using SdkMcpServer = ModelContextProtocol.Server.McpServer;

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for publishing (Go live) a draft blueprint to a register, and for putting the
/// rehearsal soft gate to a person when the version being published has never been rehearsed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The agent never waives the gate.</b> <c>PublishGate</c> blocks every publish whose
/// executable-definition hash has no matching <c>RehearsalPass</c>, so an agent's freshly
/// authored blueprint always hits <c>409 REHEARSAL_REQUIRED</c>. The rehearsal is the only
/// <em>behavioural</em> check a definition gets before it goes live — it is what would catch, for
/// example, a blueprint that issues a credential to a declined applicant (#1551). This tool
/// therefore attempts the publish with <b>no</b> override, and sends
/// <c>override: { confirm: true, reason }</c> only after
/// <see cref="ApprovalOutcome.Approved"/>. <c>ProceedWithOverride</c> writes an audit row
/// attributed to the caller's <c>PlatformUserId</c>; that attribution is only honest if a person
/// actually decided.
/// </para>
/// <para>
/// <b>Entitlement is the ADMIN role, not the designer role</b>, even though publishing is a
/// designer-workflow step. <c>POST /api/blueprints/{id}/publish</c> sits behind the Blueprint
/// Service's <c>CanPublishBlueprints</c> policy, which is satisfied by a
/// <c>can_publish_blueprint=true</c> claim OR the <c>Administrator</c> / <c>SystemAdmin</c> role —
/// and the Tenant Service's <c>TokenService</c> never emits that claim, so in practice an admin
/// role is the only way through. <c>ToolEntitlements.IsPermitted</c> matches roles exactly, so
/// entitling this on <c>sorcha:designer</c> would offer the tool to a caller the platform will
/// always refuse.
/// </para>
/// <para>
/// <b>SystemAdmin used to trip the circuit breaker.</b> <c>CanPublishBlueprints</c> accepted only
/// the literal <c>Administrator</c> role, while <c>McpRoleNormalizer</c> maps <c>SystemAdmin</c> to
/// <c>sorcha:admin</c> — so a SystemAdmin passed this tool's entitlement and was then refused by
/// ASP.NET's authorization middleware, deterministically. That refusal arrives as the same null as
/// every other failure, and the null path calls
/// <see cref="IServiceAvailabilityTracker.RecordFailure(string, Exception?)"/>, which marks a
/// service unavailable after three consecutive failures: three publish attempts silently disabled
/// EVERY Blueprint MCP tool behind a false "Blueprint service is currently unavailable". Fixed at
/// the root by widening the policy to match the shared
/// <c>AuthorizationPolicies.RequireAdministrator</c> and the Register Service's
/// <c>CanManageRegisters</c>, both of which already accept either role.
/// </para>
/// <para>
/// <b>Nobody is asked to approve something that cannot succeed.</b> That property comes for free
/// from the server's own ordering rather than from a local pre-check: <c>PublishGate</c> evaluates
/// the governance hard gate FIRST and reaches the rehearsal soft gate only once it has passed. A
/// caller lacking Owner/Admin/Designer on the register's governance roster is therefore refused on
/// the very first (override-free) attempt, long before the elicit.
/// </para>
/// <para>
/// <b>The typed client collapses every failure into one null</b> — 403 (JWT policy or governance
/// roster), 404, the 400 a publish-validation failure produces, 5xx, and a transport fault all
/// return <c>null</c> from <see cref="IBlueprintServiceClient.PublishBlueprintAsync"/>. Asserting
/// any single cause would be a confident wrong answer, so this tool says plainly which
/// possibilities remain. It CAN narrow them in one place: a 409 on the first attempt proves
/// governance already passed, so a null on the override retry is provably not an authorisation
/// failure.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class BlueprintPublishTool
{
    private const string ToolName = "sorcha_blueprint_publish";

    /// <summary>
    /// Recorded on the <c>PublishOverride</c> audit row alongside the caller's platform user id.
    /// It must say where the confirmation came from, because the row is the only lasting evidence
    /// that a person — and not an agent — waived the behavioural check.
    /// </summary>
    private const string OverrideReason =
        "Published via the Sorcha MCP server after the caller's client put the unrehearsed-publish "
        + "decision to a person, who explicitly confirmed it.";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly IHumanApproval _humanApproval;
    private readonly ILogger<BlueprintPublishTool> _logger;

    /// <summary>Creates the tool.</summary>
    public BlueprintPublishTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        IHumanApproval humanApproval,
        ILogger<BlueprintPublishTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _humanApproval = humanApproval;
        _logger = logger;
    }

    /// <summary>Publishes a draft blueprint to a register (Go live).</summary>
    /// <param name="server">The live MCP server, injected by the SDK; used to reach the caller's client.</param>
    /// <param name="blueprintId">The draft blueprint to publish.</param>
    /// <param name="registerId">The register to publish it to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The published version, or the rehearsal block if a person declined to waive it.</returns>
    [McpServerTool(Name = ToolName, Destructive = true, ReadOnly = false, Idempotent = false)]
    [Description("Publishes a draft blueprint to a register so workflow instances can be started from it, returning the immutable published version number. Call this when a draft blueprint is finished and ready to go live: it belongs after sorcha_blueprint_create and before sorcha_instance_create, which can only instantiate a blueprint that is already published. A blueprint that has not been rehearsed is blocked by a safety gate; when that happens this tool asks a person whether to publish anyway, and refuses if your MCP client cannot present that question. Publishing is recorded permanently on the register's ledger. Publishing needs organisation-administrator authority plus an Owner, Admin or Designer role on the target register's governance roster — a plain designer role is not enough, and the tool is not offered to one.")]
    public async Task<BlueprintPublishResult> PublishBlueprintAsync(
        SdkMcpServer server,
        [Description("The draft blueprint's ID")] string blueprintId,
        [Description("The register to publish to")] string registerId,
        CancellationToken cancellationToken = default)
    {
        // 1. Entitlement — cheap and local, so it precedes everything.
        if (!_authService.CanInvokeTool(ToolName))
        {
            return Fail(
                "Unauthorized",
                "Access denied. Publishing a blueprint requires the sorcha:admin role — the same "
                + "authority the Blueprint Service's own CanPublishBlueprints policy demands "
                + "(the Administrator or SystemAdmin role).");
        }

        var validationErrors = Validate(blueprintId, registerId);
        if (validationErrors.Count > 0)
        {
            return new BlueprintPublishResult
            {
                Status = "ValidationError",
                Message = "blueprintId and registerId are both required — a blueprint is always published to a specific register.",
                CheckedAt = DateTimeOffset.UtcNow,
                ValidationErrors = validationErrors
            };
        }

        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return Fail("Unavailable", "Blueprint service is currently unavailable. Please try again later. Nothing was published.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            // 2. Attempt with NO override. The agent never pre-emptively waives the gate — doing so
            //    would suppress the 409 and nobody would ever be asked.
            var outcome = await _blueprintClient.PublishBlueprintAsync(
                blueprintId,
                new PublishBlueprintRequest { RegisterId = registerId },
                cancellationToken);

            if (outcome is null)
            {
                stopwatch.Stop();
                _availabilityTracker.RecordFailure("Blueprint");
                return FirstAttemptFailed(blueprintId, registerId, stopwatch);
            }

            _availabilityTracker.RecordSuccess("Blueprint");

            if (outcome.Result is { } published)
            {
                stopwatch.Stop();
                return Published(published, registerId, publishedWithoutRehearsal: false, stopwatch);
            }

            // 3. Blocked on the rehearsal soft gate. Ask a person, naming precisely what is waived.
            //    Reaching here PROVES the governance hard gate passed (PublishGate evaluates it
            //    first), so this question can actually be acted on.
            var approval = await _humanApproval.RequestAsync(server, new HumanApprovalRequest(
                $"Publish blueprint '{blueprintId}' to register '{registerId}' WITHOUT rehearsing it?\n\n"
                + "This blueprint version has not been rehearsed, so its routing, disclosure and "
                + "credential-issuance rules have never been executed even once. Nothing has checked "
                + "that they behave as intended — for example, that a credential is not issued to an "
                + "applicant who was declined.\n\n"
                + "Publishing is recorded permanently on the register's ledger, and this waiver is "
                + "audited against your account.\n\n"
                + "The safer option is to cancel and rehearse it first in the Sorcha designer.",
                "Publish without rehearsing"), cancellationToken);

            if (approval.Outcome != ApprovalOutcome.Approved)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "Unrehearsed publish of blueprint {BlueprintId} to register {RegisterId} was not approved ({Outcome})",
                    blueprintId, registerId, approval.Outcome);

                // The two refusal paths need different responses: a decline is a decision to
                // respect, a missing elicitation capability is a client limitation to report.
                // Nothing has been published either way.
                return new BlueprintPublishResult
                {
                    Status = approval.Outcome == ApprovalOutcome.NotSupported ? "ApprovalRequired" : "RehearsalRequired",
                    Message = approval.Outcome == ApprovalOutcome.NotSupported
                        ? approval.Detail
                          + " This blueprint has not been rehearsed, and only a person may waive that "
                          + "check, so nothing was published."
                        : "This blueprint has not been rehearsed and publishing anyway was not approved. "
                          + "Nothing was published. Rehearse it in the Sorcha designer, then publish again. "
                          + $"({approval.Detail})",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    ExecDefHash = outcome.RehearsalRequired?.ExecDefHash
                };
            }

            // 4. Retry with the audited override. ProceedWithOverride attributes it to the caller's
            //    platform user id, which is exactly what the override exists for.
            var overridden = await _blueprintClient.PublishBlueprintAsync(
                blueprintId,
                new PublishBlueprintRequest
                {
                    RegisterId = registerId,
                    Override = new PublishOverride { Confirm = true, Reason = OverrideReason }
                },
                cancellationToken);

            stopwatch.Stop();

            if (overridden?.Result is not { } overriddenResult)
            {
                _availabilityTracker.RecordFailure("Blueprint");
                return OverrideRetryFailed(blueprintId, registerId, overridden, stopwatch);
            }

            _availabilityTracker.RecordSuccess("Blueprint");
            _logger.LogWarning(
                "Blueprint {BlueprintId} v{Version} published to register {RegisterId} WITHOUT a rehearsal pass, on an explicit human override via MCP",
                blueprintId, overriddenResult.Version, registerId);

            return Published(overriddenResult, registerId, publishedWithoutRehearsal: true, stopwatch);
        }
        catch (TaskCanceledException)
        {
            // BlueprintServiceClient catches HttpRequestException only, so a client-side timeout
            // arrives here rather than as a null.
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");
            _logger.LogWarning("Publishing blueprint {BlueprintId} timed out", blueprintId);

            return new BlueprintPublishResult
            {
                Status = "Timeout",
                Message =
                    $"The publish request for blueprint '{blueprintId}' timed out, so it is not known "
                    + "whether the Blueprint Service applied it. Check the blueprint's published "
                    + "versions before retrying — a repeat publish of an unchanged definition is "
                    + "deduplicated, but a retry is not free.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);
            _logger.LogWarning(ex, "Publishing blueprint {BlueprintId} failed", blueprintId);

            return new BlueprintPublishResult
            {
                Status = "Error",
                Message = $"The publish request failed: {ex.Message}. Nothing was published.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);
            _logger.LogError(ex, "Unexpected error publishing blueprint {BlueprintId}", blueprintId);

            return new BlueprintPublishResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while publishing the blueprint. Nothing was published.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// The first (override-free) attempt returned null. That single null covers every non-409
    /// failure the endpoint can produce, so the message enumerates them in the order the server
    /// evaluates them instead of picking one.
    /// </summary>
    private static BlueprintPublishResult FirstAttemptFailed(
        string blueprintId, string registerId, Stopwatch stopwatch) => new()
        {
            Status = "Error",
            Message =
                $"Publishing blueprint '{blueprintId}' to register '{registerId}' failed and nothing "
                + "was published. The Blueprint Service returned a failure the service client does "
                + "not distinguish, so this tool cannot tell you which of these it was: (a) you are "
                + "not authorised to publish — the endpoint needs the Administrator or "
                + "SystemAdmin role, and "
                + "separately needs an Owner, Admin or Designer entry for you on that register's "
                + "governance roster; (b) the blueprint does not exist; (c) the blueprint failed "
                + "publish validation; or (d) the Blueprint Service errored. The HTTP status that "
                + "settles it was logged by THIS MCP server, by BlueprintServiceClient, as a "
                + "warning reading \"Blueprint publish failed: {StatusCode}\" — look there first; "
                + "the Blueprint Service's own log corroborates it.",
            CheckedAt = DateTimeOffset.UtcNow,
            ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
        };

    /// <summary>
    /// The override retry failed. Unlike the first attempt this CAN rule authorisation out: the
    /// 409 that triggered the elicit is only reachable once <c>PublishGate</c>'s governance hard
    /// gate has passed.
    /// </summary>
    private BlueprintPublishResult OverrideRetryFailed(
        string blueprintId, string registerId, PublishBlueprintOutcome? outcome, Stopwatch stopwatch)
    {
        // A second REHEARSAL_REQUIRED would mean the confirmed override was not honoured — a
        // different fault from a publish failure, and worth naming rather than folding in.
        var stillBlocked = outcome?.IsRehearsalRequired == true;

        _logger.LogWarning(
            "Approved unrehearsed publish of blueprint {BlueprintId} to register {RegisterId} did not complete (stillBlocked={StillBlocked})",
            blueprintId, registerId, stillBlocked);

        return new BlueprintPublishResult
        {
            Status = "Error",
            Message = stillBlocked
                ? $"Blueprint '{blueprintId}' was still blocked by the rehearsal gate even though the "
                  + "publish was resent with a confirmed override. Nothing was published. This is a "
                  + "service-side fault rather than something to retry — report it."
                : $"The confirmed publish of blueprint '{blueprintId}' to register '{registerId}' "
                  + "failed and nothing was published. The rehearsal waiver a person approved was "
                  + "not used. Authorisation is not the cause: the rehearsal gate is only reached "
                  + "after the register's governance check has already passed. What remains is that "
                  + "the blueprint failed publish validation, or the Blueprint Service errored — "
                  + "validate it with sorcha_blueprint_validate, and read this MCP server's own log "
                  + "for the BlueprintServiceClient warning carrying the HTTP status.",
            CheckedAt = DateTimeOffset.UtcNow,
            ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
            ExecDefHash = outcome?.RehearsalRequired?.ExecDefHash
        };
    }

    private static BlueprintPublishResult Published(
        PublishBlueprintResult published,
        string registerId,
        bool publishedWithoutRehearsal,
        Stopwatch stopwatch) => new()
        {
            Status = "Success",
            // The publication is written to the register as a transaction; sealing is asynchronous
            // (Feature 145), so this must not read as "settled". `alreadyPublished` changes what is
            // TRUE rather than merely how it reads: a deduplicated republish carries a perfectly
            // valid version number, so without saying so, "was published as version N" is a
            // confident wrong answer about work that did not happen.
            Message = published.AlreadyPublished
                ? $"Blueprint '{published.BlueprintId}' was ALREADY published to register "
                  + $"'{registerId}' as version {published.Version} — this definition is unchanged, "
                  + "so no new version was created and nothing was written to the ledger. Start an "
                  + "instance with sorcha_instance_create."
                : publishedWithoutRehearsal
                    ? $"Blueprint '{published.BlueprintId}' was published to register '{registerId}' "
                      + $"as version {published.Version} WITHOUT a rehearsal pass, on an explicit "
                      + "human override that has been recorded against the caller's account. Its "
                      + "behaviour has never been executed. Start an instance with "
                      + "sorcha_instance_create."
                    : $"Blueprint '{published.BlueprintId}' was published to register '{registerId}' "
                      + $"as version {published.Version}. Start an instance with sorcha_instance_create.",
            CheckedAt = DateTimeOffset.UtcNow,
            ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
            BlueprintId = published.BlueprintId,
            RegisterId = string.IsNullOrWhiteSpace(published.RegisterId) ? registerId : published.RegisterId,
            Version = published.Version,
            PublishedAt = published.PublishedAt,
            // Pattern 22: the id, not the version, is what an instance is pinned to.
            PublicationTxId = published.PublicationTxId,
            ExecDefHash = published.ExecDefHash,
            AlreadyPublished = published.AlreadyPublished,
            Warnings = published.Warnings,
            // The server's own `overridden` flag is authoritative; the local expectation is the
            // fallback for a body that omitted it.
            PublishedWithoutRehearsal = published.Overridden || publishedWithoutRehearsal
        };

    private static List<string> Validate(string blueprintId, string registerId)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(blueprintId))
        {
            errors.Add("blueprintId is required");
        }

        if (string.IsNullOrWhiteSpace(registerId))
        {
            errors.Add("registerId is required — blueprints are always published to a specific register");
        }

        return errors;
    }

    private static BlueprintPublishResult Fail(string status, string message) => new()
    {
        Status = status,
        Message = message,
        CheckedAt = DateTimeOffset.UtcNow
    };
}

/// <summary>
/// Result of a blueprint publish (Go live) operation.
/// </summary>
public sealed record BlueprintPublishResult
{
    /// <summary>
    /// Operation status: Success, RehearsalRequired, ApprovalRequired, ValidationError,
    /// Unauthorized, Unavailable, Timeout, or Error.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the operation result.</summary>
    public required string Message { get; init; }

    /// <summary>When the operation was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>Validation errors (if Status is ValidationError).</summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];

    /// <summary>The blueprint that was published (if successful).</summary>
    public string? BlueprintId { get; init; }

    /// <summary>The register the version was published to (if successful).</summary>
    public string? RegisterId { get; init; }

    /// <summary>The immutable published version number, when the publish succeeded.</summary>
    public int? Version { get; init; }

    /// <summary>When the version was published (if successful).</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>
    /// The publication transaction id — the published definition's IDENTITY (Feature 195 /
    /// CLAUDE.md pattern 22). <see cref="Version"/> is a display label; this is what an instance is
    /// pinned to and what names this exact definition later.
    /// </summary>
    public string? PublicationTxId { get; init; }

    /// <summary>
    /// True when this definition was ALREADY published to this register, so no new version was
    /// created. The version number is real either way, which is exactly why this must be reported.
    /// </summary>
    public bool AlreadyPublished { get; init; }

    /// <summary>Non-blocking publish warnings (e.g. cycle detection); empty when there were none.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// True when this version went live without a rehearsal pass, on an explicit human override.
    /// </summary>
    public bool PublishedWithoutRehearsal { get; init; }

    /// <summary>
    /// The executable-definition hash — the version's BEHAVIOURAL signature, which is what the
    /// rehearsal gate matches a <c>RehearsalPass</c> on. Populated on a successful publish and on a
    /// rehearsal block alike. It is not an identity: see <see cref="PublicationTxId"/> for that.
    /// </summary>
    public string? ExecDefHash { get; init; }
}
