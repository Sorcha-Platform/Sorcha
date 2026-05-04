// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Sorcha.ApiGateway.Discoverability.Models;

namespace Sorcha.ApiGateway.Discoverability;

/// <summary>
/// Returns the live tool catalogue and per-category counts for the MCP manifest. Spec 117 FR-016.
///
/// Implementation note: the gateway does not take a project reference on Sorcha.McpServer (that
/// would create an upward dependency). The counts and per-tool descriptions are sourced from
/// <see cref="McpManifestOptions.Tools"/> in <c>appsettings.json</c>. The PR template carries an
/// "MCP catalogue updated when adding/removing a tool" reminder; future Phase 9 work can add a
/// CI step that reflects over the running MCP server and asserts agreement with this configuration.
/// </summary>
internal sealed class ToolCatalogueProvider(IOptions<McpManifestOptions> options)
{
    public IReadOnlyList<McpToolDescriptor> GetTools() =>
        options.Value.Tools
            .Select(t => new McpToolDescriptor(t.Name, t.Category, t.Description))
            .ToList();

    public CategoryCounts GetCategoryCounts()
    {
        var tools = options.Value.Tools;
        return new CategoryCounts(
            Admin: tools.Count(t => string.Equals(t.Category, "admin", StringComparison.OrdinalIgnoreCase)),
            Designer: tools.Count(t => string.Equals(t.Category, "designer", StringComparison.OrdinalIgnoreCase)),
            Participant: tools.Count(t => string.Equals(t.Category, "participant", StringComparison.OrdinalIgnoreCase)));
    }

    public sealed record CategoryCounts(int Admin, int Designer, int Participant);
}
