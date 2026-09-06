// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.McpServer.Services;

/// <summary>
/// One home for reading a workflow instance's state off the wire.
/// <para>
/// <c>Sorcha.Blueprint.Service.Models.InstanceState</c> has no <c>JsonStringEnumConverter</c>
/// registered, so <c>GET /api/instances</c> and <c>GET /api/instances/{id}</c> may carry the state
/// as its underlying int; older/other producers carry the name. Both are accepted, and an int is
/// resolved through <see cref="StateNames"/>, whose INDEX ORDER mirrors that enum's declaration
/// order. The MCP server cannot reference the service's model project, so the mirror is
/// unavoidable — but a mirror kept in two places is a mirror that drifts in one of them, which is
/// why this lives once and is pinned by a reflective test over the real enum
/// (<c>InstanceStateResolverTests</c>), exactly as <see cref="McpRoleNormalizer"/> is.
/// </para>
/// </summary>
public static class InstanceStateResolver
{
    /// <summary>
    /// <c>InstanceState</c>'s members in declaration order. Index MUST track that enum.
    /// </summary>
    public static readonly string[] StateNames =
        ["Active", "Completed", "Rejected", "TimedOut", "Cancelled"];

    /// <summary>
    /// Resolves an instance state carried as either a JSON string or a JSON number.
    /// Returns <c>"Unknown"</c> for a missing, null, or unrecognised value.
    /// </summary>
    /// <param name="state">The raw <c>state</c> element from the instance body.</param>
    public static string Resolve(JsonElement? state)
    {
        if (state is not { } value)
        {
            return "Unknown";
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "Unknown",
            JsonValueKind.Number when value.TryGetInt32(out var ordinal)
                && ordinal >= 0 && ordinal < StateNames.Length => StateNames[ordinal],
            _ => "Unknown"
        };
    }
}
