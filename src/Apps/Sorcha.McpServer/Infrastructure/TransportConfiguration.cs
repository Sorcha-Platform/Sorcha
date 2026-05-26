// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer;

/// <summary>
/// The transport the MCP server serves, selected at startup via <c>--transport</c> (spec 139 US3).
/// </summary>
public enum TransportMode
{
    /// <summary>Standard input/output — one caller per process (default; local operators).</summary>
    Stdio,

    /// <summary>Streamable HTTP — per-request identity from the Authorization bearer (remote agents).</summary>
    Http
}

/// <summary>
/// Shared MCP server-builder configuration so both transports wire the advisory
/// tools/list narrowing filter identically (spec 139 US2/US3).
/// </summary>
public static class McpServerBuilderExtensions
{
    /// <summary>
    /// Narrows the advertised <c>tools/list</c> to the caller's tier/role entitlement so a
    /// consumer never even sees admin/designer tools. Advisory only — invocation-time gating
    /// (<see cref="IMcpAuthorizationService"/>) and the gateway remain authoritative.
    /// <para>
    /// Transport-agnostic: the filter runs per request, reading the ambient
    /// <see cref="Infrastructure.ICallerContext"/>. On stdio that is the one-per-process session;
    /// on HTTP it is the per-request <see cref="Infrastructure.HttpCallerContext"/>.
    /// </para>
    /// </summary>
    public static IMcpServerBuilder WithAuthorizationNarrowingListToolsFilter(this IMcpServerBuilder builder)
    {
        return builder.WithRequestFilters(filters =>
        {
            filters.AddListToolsFilter(next => async (context, cancellationToken) =>
            {
                var result = await next(context, cancellationToken);

                var authz = context.Services?.GetService<IMcpAuthorizationService>();
                if (authz is not null && result.Tools.Count > 0)
                {
                    var allowed = authz.GetAuthorizedTools().ToHashSet(StringComparer.Ordinal);
                    result.Tools = [.. result.Tools.Where(tool => allowed.Contains(tool.Name))];
                }

                return result;
            });
        });
    }

    /// <summary>
    /// Central per-invocation observability (spec 139 US5). Times every tool call and records
    /// <c>{ callerTier, toolName, outcome, backendStatus }</c> through <see cref="IToolAuditService"/>
    /// (which also emits the <c>Sorcha.Mcp</c> metrics) — no per-tool wiring needed. The outcome is
    /// read from the tool result's <c>Status</c> field where one exists, falling back to the
    /// protocol-level error flag. No token material, arguments, or subject identifiers are recorded.
    /// <para>Transport-agnostic: runs per request on both stdio and HTTP, reading the ambient
    /// <see cref="ICallerContext"/> for the tier.</para>
    /// </summary>
    public static IMcpServerBuilder WithToolInvocationAuditFilter(this IMcpServerBuilder builder)
    {
        return builder.WithRequestFilters(filters =>
        {
            filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                var toolName = context.Params?.Name ?? "unknown";
                var stopwatch = Stopwatch.StartNew();

                var audit = context.Services?.GetService<IToolAuditService>();
                var caller = context.Services?.GetService<ICallerContext>();

                try
                {
                    var result = await next(context, cancellationToken);
                    stopwatch.Stop();

                    if (audit is not null)
                    {
                        var (outcome, backendStatus) = DeriveOutcome(result);
                        audit.RecordOutcome(new ToolOutcomeRecord
                        {
                            CallerTier = caller?.Tier,
                            ToolName = toolName,
                            Outcome = outcome,
                            BackendStatus = backendStatus,
                            Duration = stopwatch.Elapsed,
                        });
                    }

                    return result;
                }
                catch
                {
                    stopwatch.Stop();
                    audit?.RecordOutcome(new ToolOutcomeRecord
                    {
                        CallerTier = caller?.Tier,
                        ToolName = toolName,
                        Outcome = "exception",
                        Duration = stopwatch.Elapsed,
                    });
                    throw;
                }
            });
        });
    }

    /// <summary>
    /// Derives the caller-facing outcome (and backend status, when distinct) from a tool result.
    /// Most Sorcha tools return an object with a <c>Status</c> field; that becomes both the outcome
    /// and the backend status. When there is no structured status, the protocol-level error flag is used.
    /// </summary>
    private static (string Outcome, string? BackendStatus) DeriveOutcome(
        ModelContextProtocol.Protocol.CallToolResult result)
    {
        var status = TryReadStatus(result.StructuredContent);
        if (!string.IsNullOrWhiteSpace(status))
        {
            return (status!, status);
        }

        return result.IsError == true ? ("error", null) : ("success", null);
    }

    private static string? TryReadStatus(JsonElement? structured)
    {
        if (structured is not { ValueKind: JsonValueKind.Object } element)
        {
            return null;
        }

        // MCP serialises tool results with camelCase by default; accept PascalCase defensively.
        if (element.TryGetProperty("status", out var camel) && camel.ValueKind == JsonValueKind.String)
        {
            return camel.GetString();
        }

        if (element.TryGetProperty("Status", out var pascal) && pascal.ValueKind == JsonValueKind.String)
        {
            return pascal.GetString();
        }

        return null;
    }
}
