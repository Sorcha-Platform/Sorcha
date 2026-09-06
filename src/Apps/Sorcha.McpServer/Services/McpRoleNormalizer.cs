// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.McpServer.Services;

/// <summary>
/// The single home for mapping a platform role name onto its Sorcha MCP <c>sorcha:*</c> form
/// (spec 139 US3). Previously duplicated verbatim in <see cref="HttpCallerContext"/> and
/// <see cref="McpSessionService"/> — both now delegate here.
/// <para>
/// The platform's real <c>UserRole</c> enum
/// (<c>Sorcha.Tenant.Service.Models.UserRole</c>) has exactly five values: <c>SystemAdmin</c>,
/// <c>Administrator</c>, <c>Designer</c>, <c>Auditor</c>, <c>Consumer</c>. The old normaliser
/// only recognised <c>admin|administrator|systemadmin</c>, <c>designer|...</c>, and
/// <c>participant|user|member</c> — none of which the platform ever emits for a citizen or an
/// auditor, so those two roles passed through unmapped and could never satisfy a
/// <c>RequiredRole</c> check.
/// </para>
/// </summary>
public static class McpRoleNormalizer
{
    /// <summary>
    /// Normalises a single platform role name to its <c>sorcha:*</c> MCP form. A value already
    /// in <c>sorcha:*</c> form is lower-cased and returned unchanged (aside from casing). An
    /// unrecognised value is returned as-is — it will not satisfy any <c>RequiredRole</c> check,
    /// which is the deliberate fail-closed behaviour for a role the platform doesn't emit.
    /// </summary>
    /// <param name="platformRole">A role claim value, e.g. "Consumer" or "sorcha:admin".</param>
    public static string Normalize(string platformRole)
    {
        if (platformRole.StartsWith("sorcha:", StringComparison.OrdinalIgnoreCase))
        {
            return platformRole.ToLowerInvariant();
        }

        return platformRole.ToLowerInvariant() switch
        {
            "admin" or "administrator" or "systemadmin" => "sorcha:admin",
            "designer" or "workflowdesigner" or "blueprintdesigner" => "sorcha:designer",
            "participant" or "user" or "member" or "consumer" => "sorcha:participant",
            "auditor" => "sorcha:auditor",
            _ => platformRole
        };
    }

    /// <summary>
    /// Normalises a collection of platform role names, deduplicating the result.
    /// </summary>
    public static List<string> NormalizeAll(IEnumerable<string> platformRoles) =>
        platformRoles.Select(Normalize).Distinct().ToList();
}
