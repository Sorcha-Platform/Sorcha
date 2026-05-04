// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ApiGateway.Discoverability;

/// <summary>
/// Bound from the <c>McpManifest</c> configuration section. Spec 117 FR-013 / FR-014 / FR-015 / FR-016 —
/// every field that doesn't come from the running platform's runtime state (tool counts, version)
/// lives in configuration so deployments can override the issuer, audience, and external URLs.
/// </summary>
public sealed class McpManifestOptions
{
    public const string SectionName = "McpManifest";

    /// <summary>The MCP server name. Convention: <c>sorcha-mcp</c>.</summary>
    public string Name { get; set; } = "sorcha-mcp";

    /// <summary>One-sentence description of the MCP server.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>JWT issuer URL — points at the Tenant Service.</summary>
    public string AuthIssuer { get; set; } = string.Empty;

    /// <summary>JWT audience claim expected on inbound MCP calls.</summary>
    public string AuthAudience { get; set; } = "sorcha-mcp";

    /// <summary>External link to the JWT acquisition flow documentation or script.</summary>
    public string AuthAcquisitionUrl { get; set; } = string.Empty;

    /// <summary>Stdio launch command (typically <c>dotnet</c>).</summary>
    public string StdioCommand { get; set; } = "dotnet";

    /// <summary>Stdio args (typically <c>run --project src/Apps/Sorcha.McpServer</c>).</summary>
    public IList<string> StdioArgs { get; set; } = new List<string>();

    /// <summary>Base URL for the http+sse transport.</summary>
    public string HttpSseUrl { get; set; } = string.Empty;

    /// <summary>One-sentence description of the admin slice.</summary>
    public string AdminCategoryDescription { get; set; } = string.Empty;

    /// <summary>One-sentence description of the designer slice.</summary>
    public string DesignerCategoryDescription { get; set; } = string.Empty;

    /// <summary>One-sentence description of the participant slice.</summary>
    public string ParticipantCategoryDescription { get; set; } = string.Empty;

    /// <summary>Public URL of the MCP server documentation.</summary>
    public string DocumentationUrl { get; set; } = string.Empty;

    /// <summary>
    /// The MCP tool catalogue. Each entry carries the tool name (matching the
    /// <c>[McpServerTool(Name = "...")]</c> attribute on the implementation), the category
    /// (<c>admin</c> / <c>designer</c> / <c>participant</c>), and a one-paragraph description
    /// suitable for an AI agent to read when deciding which tool to call. The categories'
    /// counts in the served manifest derive from this list automatically.
    /// </summary>
    public IList<McpToolEntry> Tools { get; set; } = new List<McpToolEntry>();

    public sealed class McpToolEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
