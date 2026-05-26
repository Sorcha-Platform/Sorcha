// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
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
}
