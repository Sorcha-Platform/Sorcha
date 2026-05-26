// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.McpServer.Services;

/// <summary>
/// OpenTelemetry instruments for the MCP server's per-invocation observability (spec 139 US5).
/// Records the caller's trust tier, the tool name, the caller-facing outcome, and any backend
/// call status — never token material, arguments, or subject identifiers. Register as a
/// singleton and add <see cref="MeterName"/> to the meter provider's sources where consumed.
/// </summary>
public sealed class McpMetrics
{
    /// <summary>The OpenTelemetry meter name. Add this to the OTel meter sources to export.</summary>
    public const string MeterName = "Sorcha.Mcp";

    private readonly Counter<long> _toolInvoked;
    private readonly Counter<long> _backendCall;
    private readonly Histogram<double> _toolLatency;

    /// <summary>Creates the meter + instruments via the host's <see cref="IMeterFactory"/>.</summary>
    public McpMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);

        _toolInvoked = meter.CreateCounter<long>(
            "sorcha_mcp_tool_invoked_total",
            unit: "{invocation}",
            description: "MCP tool invocations, by tool, caller trust tier, and caller-facing outcome.");

        _backendCall = meter.CreateCounter<long>(
            "sorcha_mcp_backend_call_total",
            unit: "{call}",
            description: "Backend (platform) calls made while serving an MCP tool, by service and status.");

        _toolLatency = meter.CreateHistogram<double>(
            "sorcha_mcp_tool_duration_ms",
            unit: "ms",
            description: "Wall-clock duration of an MCP tool invocation, by tool, tier, and outcome.");
    }

    /// <summary>
    /// Records one tool invocation and its wall-clock duration, tagged by tool, tier, and outcome.
    /// </summary>
    public void ToolInvoked(string toolName, Tier? tier, string outcome, double durationMs)
    {
        var tierTag = TagFor(tier);
        var outcomeTag = string.IsNullOrWhiteSpace(outcome) ? "unknown" : outcome;

        _toolInvoked.Add(1,
            new KeyValuePair<string, object?>("tool", toolName),
            new KeyValuePair<string, object?>("tier", tierTag),
            new KeyValuePair<string, object?>("outcome", outcomeTag));

        _toolLatency.Record(durationMs,
            new KeyValuePair<string, object?>("tool", toolName),
            new KeyValuePair<string, object?>("tier", tierTag),
            new KeyValuePair<string, object?>("outcome", outcomeTag));
    }

    /// <summary>
    /// Records one backend call status (e.g. the platform-mapped status a tool reported), by service.
    /// </summary>
    public void BackendCall(string service, string status)
    {
        if (string.IsNullOrWhiteSpace(service))
        {
            return;
        }

        _backendCall.Add(1,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("status", string.IsNullOrWhiteSpace(status) ? "unknown" : status));
    }

    /// <summary>The low-cardinality tier tag value (no subject data).</summary>
    public static string TagFor(Tier? tier) => tier switch
    {
        Tier.Consumer => "consumer",
        Tier.Platform => "platform",
        Tier.Service => "service",
        Tier.EnrolSession => "enrol-session",
        _ => "unknown",
    };
}
