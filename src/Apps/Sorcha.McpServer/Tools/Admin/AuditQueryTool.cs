// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Admin tool for reading the caller's organisation audit log, including refusals (#1648).
/// </summary>
[McpServerToolType]
public sealed class AuditQueryTool
{
    private const string ToolName = "sorcha_audit_query";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ICallerContext _callerContext;
    private readonly ITenantServiceClient _tenantClient;
    private readonly ILogger<AuditQueryTool> _logger;

    /// <summary>Creates the tool.</summary>
    public AuditQueryTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ICallerContext callerContext,
        ITenantServiceClient tenantClient,
        ILogger<AuditQueryTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _callerContext = callerContext;
        _tenantClient = tenantClient;
        _logger = logger;
    }

    /// <summary>Reads the caller's organisation audit log.</summary>
    /// <param name="eventType">Filter by event type, e.g. PermissionDenied (optional).</param>
    /// <param name="userId">Filter by platform user id (optional).</param>
    /// <param name="startTime">Start of the time window, ISO 8601 (optional).</param>
    /// <param name="endTime">End of the time window, ISO 8601 (optional).</param>
    /// <param name="page">Page number, 1-based.</param>
    /// <param name="pageSize">Items per page, 1-200.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The audit entries.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Returns paged entries from YOUR organisation's audit log, newest first: logins, membership and invitation changes, and refusals. Services record refusals with their reason as PermissionDenied entries, naming the action (wallet.sign, wallet.access, blueprint.publish, blueprint.amend), the resource and the refusing service. Use this when an action was refused (a 403, or a tool reporting Unauthorized) and you need to know why: filter eventType=PermissionDenied and a startTime just before the attempt. The organisation always comes from your token; it cannot read another organisation's log. It requires the Auditor, Administrator or SystemAdmin role in that organisation. Prefer this instead of sorcha_log_query, which does not expose raw service logs.")]
    public async Task<AuditQueryResult> QueryAuditLogsAsync(
        [Description("Filter by event type, e.g. PermissionDenied for refusals, Login, UserAddedToOrganization")] string? eventType = null,
        [Description("Filter by platform user id (a GUID)")] string? userId = null,
        [Description("Start of the time window (ISO 8601)")] string? startTime = null,
        [Description("End of the time window (ISO 8601)")] string? endTime = null,
        [Description("Page number (1-based, default 1)")] int page = 1,
        [Description("Items per page (default 50, max 200)")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return Fail("Unauthorized", "Access denied. This tool requires the sorcha:admin role.");
        }

        // The organisation is the caller's own, from the token. Never an argument: a SystemAdmin token
        // is exempt from the Tenant Service's own organisation check.
        var orgId = _callerContext.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
        {
            return Fail("Error",
                "Your token carries no organisation (org_id), so there is no organisation audit log to read. "
                + "Sign in with a platform-tier account that belongs to an organisation.");
        }

        if (!TryParseTime(startTime, out var start))
        {
            return Fail("Error", $"startTime '{startTime}' is not an ISO 8601 date-time.");
        }
        if (!TryParseTime(endTime, out var end))
        {
            return Fail("Error", $"endTime '{endTime}' is not an ISO 8601 date-time.");
        }
        if (!string.IsNullOrWhiteSpace(userId) && !Guid.TryParse(userId, out _))
        {
            return Fail("Error", $"userId '{userId}' is not a platform user id (GUID).");
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return Fail("Unavailable", "Tenant service is currently unavailable. Please try again later.");
        }

        var clampedPage = Math.Max(1, page);
        var clampedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = BuildQuery(eventType, userId, start, end, clampedPage, clampedPageSize);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var (status, body) = await _tenantClient.GetOrganizationAuditEventsAsync(orgId, query, cancellationToken);
            stopwatch.Stop();
            var elapsed = (int)stopwatch.ElapsedMilliseconds;

            switch (status)
            {
                case HttpStatusCode.Forbidden:
                    // A permissions answer, not an outage: the availability tracker is left alone.
                    return Fail("Unauthorized",
                        "Reading your organisation's audit log requires the Auditor, Administrator or SystemAdmin "
                        + "role in that organisation, on a platform-tier token. The Tenant Service refused this token.",
                        elapsed);
                case HttpStatusCode.Unauthorized:
                    return Fail("Unauthorized", "The Tenant Service did not accept your token.", elapsed);
                case HttpStatusCode.NotFound:
                    return Fail("NotFound", $"Organisation '{orgId}' was not found.", elapsed);
            }

            if ((int)status >= 500)
            {
                _availabilityTracker.RecordFailure(ServiceName);
                return Fail("Error", $"The Tenant Service failed to read the audit log ({(int)status}).", elapsed);
            }

            if ((int)status is < 200 or >= 300 || string.IsNullOrWhiteSpace(body))
            {
                return Fail("Error", $"The Tenant Service returned {(int)status} for the audit log.", elapsed);
            }

            _availabilityTracker.RecordSuccess(ServiceName);
            return Map(body, orgId, clampedPage, clampedPageSize, eventType, elapsed);
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            return Fail("Timeout", "Request to the Tenant Service timed out.", (int)stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogWarning(ex, "Audit log read failed for organisation {OrgId}", orgId);
            return Fail("Unavailable", "The Tenant Service could not be reached.", (int)stopwatch.ElapsedMilliseconds);
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Audit log for organisation {OrgId} was not readable", orgId);
            return Fail("Error", "The audit log response could not be read.", (int)stopwatch.ElapsedMilliseconds);
        }
    }

    private const string ServiceName = "Tenant";

    /// <summary>Matches the Tenant Service's own page-size clamp.</summary>
    private const int MaxPageSize = 200;

    private static bool TryParseTime(string? value, out DateTimeOffset? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
        {
            parsed = time;
            return true;
        }

        return false;
    }

    private static string BuildQuery(
        string? eventType, string? userId, DateTimeOffset? start, DateTimeOffset? end, int page, int pageSize)
    {
        var parts = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(eventType))
            parts.Add($"eventType={Uri.EscapeDataString(eventType.Trim())}");
        if (!string.IsNullOrWhiteSpace(userId))
            parts.Add($"userId={Uri.EscapeDataString(userId.Trim())}");
        if (start is { } s)
            parts.Add($"startDate={Uri.EscapeDataString(s.ToString("o", CultureInfo.InvariantCulture))}");
        if (end is { } e)
            parts.Add($"endDate={Uri.EscapeDataString(e.ToString("o", CultureInfo.InvariantCulture))}");
        return string.Join("&", parts);
    }

    /// <summary>
    /// Reads the Tenant Service's <c>AuditLogResponse</c> with <see cref="JsonDocument"/> rather than a
    /// mirror type, so there is one declaration of that shape (in the Tenant Service).
    /// </summary>
    private static AuditQueryResult Map(
        string body, string orgId, int page, int pageSize, string? eventType, int elapsed)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var entries = new List<AuditEntry>();
        if (root.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in events.EnumerateArray())
            {
                var type = String(item, "eventType") ?? "Unknown";
                var details = item.TryGetProperty("details", out var d) && d.ValueKind == JsonValueKind.Object
                    ? d
                    : (JsonElement?)null;

                entries.Add(new AuditEntry
                {
                    AuditId = item.TryGetProperty("id", out var id) ? id.ToString() : string.Empty,
                    Timestamp = item.TryGetProperty("timestamp", out var ts) && ts.TryGetDateTimeOffset(out var when)
                        ? when
                        : null,
                    EventType = type,
                    UserId = String(item, "identityId"),
                    Success = item.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True,
                    Action = (details is { } a ? String(a, "action") : null) ?? type,
                    ResourceType = details is { } rt ? String(rt, "resourceType") : null,
                    ResourceId = details is { } ri ? String(ri, "resourceId") : null,
                    Reason = details is { } rs ? String(rs, "reason") : null,
                    Service = details is { } sv ? String(sv, "service") : null,
                    Details = details?.GetRawText()
                });
            }
        }

        var total = root.TryGetProperty("totalCount", out var count) && count.TryGetInt32(out var n) ? n : entries.Count;
        var refusalsOnly = string.Equals(eventType, "PermissionDenied", StringComparison.OrdinalIgnoreCase);

        return new AuditQueryResult
        {
            Status = "Success",
            Message = entries.Count == 0
                ? refusalsOnly
                    ? "No refusals are recorded for your organisation in this window."
                    : "No audit entries match this filter for your organisation."
                : $"{entries.Count} of {total} audit entr{(total == 1 ? "y" : "ies")} for your organisation, newest first.",
            CheckedAt = DateTimeOffset.UtcNow,
            ResponseTimeMs = elapsed,
            OrganizationId = orgId,
            Entries = entries,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static AuditQueryResult Fail(string status, string message, int elapsed = 0) => new()
    {
        Status = status,
        Message = message,
        CheckedAt = DateTimeOffset.UtcNow,
        ResponseTimeMs = elapsed
    };
}

/// <summary>Result of reading the organisation audit log.</summary>
public sealed record AuditQueryResult
{
    /// <summary>Status: Success, Unauthorized, Error, NotFound, Unavailable, or Timeout.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the query was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The organisation whose log was read (always the caller's own).</summary>
    public string? OrganizationId { get; init; }

    /// <summary>The audit entries on this page, newest first.</summary>
    public IReadOnlyList<AuditEntry> Entries { get; init; } = [];

    /// <summary>Total number of entries matching the filter.</summary>
    public int TotalCount { get; init; }

    /// <summary>Current page number.</summary>
    public int Page { get; init; }

    /// <summary>Items per page.</summary>
    public int PageSize { get; init; }

    /// <summary>Total number of pages.</summary>
    public int TotalPages { get; init; }
}

/// <summary>One audit log entry.</summary>
public sealed record AuditEntry
{
    /// <summary>Audit entry id.</summary>
    public required string AuditId { get; init; }

    /// <summary>When the event occurred.</summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>Event type, e.g. Login or PermissionDenied.</summary>
    public required string EventType { get; init; }

    /// <summary>The platform user the event concerns, when known.</summary>
    public string? UserId { get; init; }

    /// <summary>False for refusals and failures.</summary>
    public bool Success { get; init; }

    /// <summary>What was attempted, e.g. wallet.sign; the event type when the entry names no action.</summary>
    public required string Action { get; init; }

    /// <summary>The kind of resource acted on, e.g. wallet or register.</summary>
    public string? ResourceType { get; init; }

    /// <summary>The resource acted on, e.g. a wallet address or register id.</summary>
    public string? ResourceId { get; init; }

    /// <summary>Why it was refused, for refusal entries.</summary>
    public string? Reason { get; init; }

    /// <summary>The service that recorded a refusal.</summary>
    public string? Service { get; init; }

    /// <summary>The entry's full details as JSON.</summary>
    public string? Details { get; init; }
}
