// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

namespace Sorcha.McpServer.Services;

/// <summary>
/// Records audit logs for tool invocations using structured logging, and emits the
/// <c>Sorcha.Mcp</c> observability metrics (spec 139 US5). Never records token material,
/// tool arguments, or subject identifiers — only tier, tool name, outcome, and backend status.
/// </summary>
public sealed class ToolAuditService : IToolAuditService
{
    private readonly ILogger<ToolAuditService> _logger;
    private readonly McpMetrics _metrics;

    public ToolAuditService(ILogger<ToolAuditService> logger, McpMetrics metrics)
    {
        _logger = logger;
        _metrics = metrics;
    }

    /// <inheritdoc />
    public void RecordInvocation(ToolInvocationRecord record)
    {
        if (record.Success)
        {
            _logger.LogInformation(
                "Tool invocation: {InvocationId} | User: {UserId} | Tool: {ToolName} | Duration: {DurationMs}ms | Success",
                record.InvocationId,
                record.UserId,
                record.ToolName,
                record.Duration.TotalMilliseconds);
        }
        else
        {
            _logger.LogWarning(
                "Tool invocation failed: {InvocationId} | User: {UserId} | Tool: {ToolName} | Duration: {DurationMs}ms | Error: {ErrorMessage}",
                record.InvocationId,
                record.UserId,
                record.ToolName,
                record.Duration.TotalMilliseconds,
                record.ErrorMessage);
        }
    }

    /// <inheritdoc />
    public void RecordOutcome(ToolOutcomeRecord outcome)
    {
        var tierTag = McpMetrics.TagFor(outcome.CallerTier);

        // Privacy-safe structured audit: tier + tool + outcome + backend status only. No token,
        // no arguments, no subject.
        _logger.LogInformation(
            "MCP tool outcome | Tier: {CallerTier} | Tool: {ToolName} | Outcome: {Outcome} | Backend: {BackendStatus} | Duration: {DurationMs}ms",
            tierTag,
            outcome.ToolName,
            outcome.Outcome,
            outcome.BackendStatus ?? "n/a",
            outcome.Duration.TotalMilliseconds);

        _metrics.ToolInvoked(outcome.ToolName, outcome.CallerTier, outcome.Outcome, outcome.Duration.TotalMilliseconds);

        if (!string.IsNullOrWhiteSpace(outcome.BackendStatus))
        {
            // The MCP server fronts the platform as a single logical backend (the API Gateway);
            // record the backend status against the "gateway" service dimension.
            _metrics.BackendCall("gateway", outcome.BackendStatus);
        }
    }
}
