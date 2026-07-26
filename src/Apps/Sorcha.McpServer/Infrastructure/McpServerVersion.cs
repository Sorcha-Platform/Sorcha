// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;

namespace Sorcha.McpServer.Infrastructure;

/// <summary>
/// The MCP server's own version, derived once from the assembly the unified-versioning build
/// stamped (root <c>Directory.Build.props</c>, CLAUDE.md §14).
/// </summary>
/// <remarks>
/// MCP audit 2026-07-26: <c>ServerInfo.Version</c> was hardcoded <c>"1.0.0"</c>, so an MCP
/// client's <c>initialize</c> response disagreed with the manifest at
/// <c>/.well-known/mcp.json</c> (which already derives — <c>2.886.1</c> on n1 at the time). Two
/// version claims about the same server, one false. Resolved against THIS type's assembly rather
/// than <c>Assembly.GetEntryAssembly()</c> so the value is right under any host — unit tests,
/// stdio, or the ASP.NET Streamable-HTTP process.
/// </remarks>
internal static class McpServerVersion
{
    /// <summary>
    /// The informational version with any <c>+commitHash</c> build-metadata tail removed —
    /// <c>2.&lt;run&gt;.&lt;attempt&gt;</c> in CI, <c>2.0.0-dev</c> locally.
    /// </summary>
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var raw = typeof(McpServerVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0-unstamped";

        var plusIdx = raw.IndexOf('+');
        return plusIdx > 0 ? raw[..plusIdx] : raw;
    }
}
