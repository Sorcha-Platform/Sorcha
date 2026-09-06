// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.McpServer.Services;

/// <summary>
/// Declares which trust tier(s) — and, for platform-tier tools, which role — may see and
/// invoke a given MCP tool. This is the F136 tier-primary / role-secondary entitlement
/// model that replaces the old role-only RBAC map (which left consumer-tier tokens, that
/// carry no roles, with zero tools).
/// <para>
/// The check is advisory and may only ever <em>narrow</em> what a caller sees/attempts —
/// the API Gateway remains the authoritative authorization decision. See spec 139.
/// </para>
/// </summary>
public sealed record ToolEntitlement(string ToolName, Tier[] Tiers, string? RequiredRole);

/// <summary>
/// The static tier→tool entitlement table for the foundation tool surface. Mirrors
/// <c>specs/139-mcp-foundation/contracts/transport-and-tools.md</c> §2.
/// <c>sorcha_wallet_sign</c> is intentionally absent (deferred to a dedicated wave), and
/// <c>sorcha_blueprint_diff</c> is intentionally absent (MCP P0 Task 5 — no backing route).
/// </summary>
public static class ToolEntitlements
{
    /// <summary>Sorcha MCP role constants.</summary>
    public const string AdminRole = "sorcha:admin";

    /// <summary>The designer role constant.</summary>
    public const string DesignerRole = "sorcha:designer";

    private static readonly Tier[] PlatformOnly = [Tier.Platform];
    private static readonly Tier[] ConsumerAndPlatform = [Tier.Consumer, Tier.Platform];
    private static readonly Tier[] ConsumerOnly = [Tier.Consumer];

    /// <summary>The complete entitlement table for every advertised tool.</summary>
    public static readonly IReadOnlyList<ToolEntitlement> All =
    [
        // Operator / Admin — platform tier + admin role
        new("sorcha_health_check", PlatformOnly, AdminRole),
        new("sorcha_log_query", PlatformOnly, AdminRole),
        new("sorcha_metrics", PlatformOnly, AdminRole),
        new("sorcha_audit_query", PlatformOnly, AdminRole),
        new("sorcha_tenant_list", PlatformOnly, AdminRole),
        new("sorcha_tenant_create", PlatformOnly, AdminRole),
        new("sorcha_tenant_update", PlatformOnly, AdminRole),
        new("sorcha_user_list", PlatformOnly, AdminRole),
        new("sorcha_user_manage", PlatformOnly, AdminRole),
        new("sorcha_peer_status", PlatformOnly, AdminRole),
        new("sorcha_validator_status", PlatformOnly, AdminRole),
        new("sorcha_register_stats", PlatformOnly, AdminRole),
        new("sorcha_token_revoke", PlatformOnly, AdminRole),

        // Register control & federation (Feature 140 Wave 1) — platform tier + admin role
        new("sorcha_register_subscribe", PlatformOnly, AdminRole),
        new("sorcha_register_unsubscribe", PlatformOnly, AdminRole),
        new("sorcha_register_sync_state", PlatformOnly, AdminRole),
        new("sorcha_register_relationship", PlatformOnly, AdminRole),
        new("sorcha_transaction_status", PlatformOnly, AdminRole),
        new("sorcha_transaction_inclusion_proof", PlatformOnly, AdminRole),
        new("sorcha_transaction_verification_bundle", PlatformOnly, AdminRole),
        new("sorcha_transaction_revoke", PlatformOnly, AdminRole),

        // Credential & presentation lifecycle (Feature 140 Wave 2) — platform tier + admin role
        new("sorcha_credential_offer", PlatformOnly, AdminRole),
        new("sorcha_presentation_request", PlatformOnly, AdminRole),
        new("sorcha_presentation_status", PlatformOnly, AdminRole),
        new("sorcha_credential_revoke", PlatformOnly, AdminRole),
        new("sorcha_credential_suspend", PlatformOnly, AdminRole),
        new("sorcha_credential_reinstate", PlatformOnly, AdminRole),
        new("sorcha_credential_refresh", PlatformOnly, AdminRole),

        // Platform-administration depth (Feature 140 Wave 4) — platform tier + admin role
        new("sorcha_org_status", PlatformOnly, AdminRole),
        new("sorcha_platform_settings", PlatformOnly, AdminRole),
        new("sorcha_org_user_audit", PlatformOnly, AdminRole),
        new("sorcha_org_wallet_status", PlatformOnly, AdminRole),
        new("sorcha_validator_control", PlatformOnly, AdminRole),
        new("sorcha_user_provision", PlatformOnly, AdminRole),
        new("sorcha_user_password_reset", PlatformOnly, AdminRole),

        // Designer — platform tier + designer role
        new("sorcha_blueprint_list", PlatformOnly, DesignerRole),
        new("sorcha_blueprint_get", PlatformOnly, DesignerRole),
        new("sorcha_blueprint_create", PlatformOnly, DesignerRole),
        new("sorcha_blueprint_update", PlatformOnly, DesignerRole),
        new("sorcha_blueprint_validate", PlatformOnly, DesignerRole),
        new("sorcha_blueprint_simulate", PlatformOnly, DesignerRole),
        new("sorcha_disclosure_analysis", PlatformOnly, DesignerRole),
        // sorcha_blueprint_diff — REMOVED from the surface (MCP P0 Task 5): no /diff endpoint
        // exists anywhere to back it. See BlueprintDiffTool and issue #1607.
        new("sorcha_blueprint_export", PlatformOnly, DesignerRole),
        new("sorcha_schema_validate", PlatformOnly, DesignerRole),
        new("sorcha_schema_generate", PlatformOnly, DesignerRole),
        new("sorcha_jsonlogic_test", PlatformOnly, DesignerRole),
        new("sorcha_workflow_instances", PlatformOnly, DesignerRole),

        // Workflow participation + citizen read — cross-tier (consumer OR platform), no role
        new("sorcha_inbox_list", ConsumerAndPlatform, null),
        new("sorcha_action_details", ConsumerAndPlatform, null),
        new("sorcha_action_validate", ConsumerAndPlatform, null),
        new("sorcha_action_submit", ConsumerAndPlatform, null),
        new("sorcha_workflow_status", ConsumerAndPlatform, null),
        new("sorcha_disclosed_data", ConsumerAndPlatform, null),
        new("sorcha_transaction_history", ConsumerAndPlatform, null),
        new("sorcha_register_query", ConsumerAndPlatform, null),
        new("sorcha_wallet_info", ConsumerAndPlatform, null),
        // sorcha_wallet_sign — REMOVED from the surface (deferred to a dedicated security-reviewed wave)

        // Citizen self-service (Feature 140 Wave 3) — consumer tier ONLY, no role.
        // Scoped to the calling citizen by the platform (the forwarded token carries identity);
        // a platform-admin context does NOT see these — they are the consumer-facing slice.
        new("sorcha_my_credentials", ConsumerOnly, null),
        new("sorcha_my_devices", ConsumerOnly, null),
        new("sorcha_my_device_rename", ConsumerOnly, null),
        new("sorcha_my_device_revoke", ConsumerOnly, null),
        new("sorcha_my_persona", ConsumerOnly, null),
        new("sorcha_pending_applications", ConsumerOnly, null),
        new("sorcha_my_presentations", ConsumerOnly, null),
        new("sorcha_my_invitations", ConsumerOnly, null),
    ];

    private static readonly Dictionary<string, ToolEntitlement> ByName =
        All.ToDictionary(e => e.ToolName, StringComparer.Ordinal);

    /// <summary>
    /// Returns true if a caller of the given tier and roles may invoke the tool. Unknown
    /// tools and unknown tiers return false (fail-closed). The gateway remains authoritative.
    /// </summary>
    public static bool IsPermitted(string toolName, Tier? tier, IReadOnlyCollection<string> roles)
    {
        if (tier is null || !ByName.TryGetValue(toolName, out var entitlement))
        {
            return false;
        }

        if (!entitlement.Tiers.Contains(tier.Value))
        {
            return false;
        }

        if (entitlement.RequiredRole is null)
        {
            return true;
        }

        return roles.Contains(entitlement.RequiredRole);
    }

    /// <summary>The tools a caller of the given tier and roles may see, sorted by name.</summary>
    public static IReadOnlyList<string> VisibleTools(Tier? tier, IReadOnlyCollection<string> roles) =>
        All.Where(e => IsPermitted(e.ToolName, tier, roles))
           .Select(e => e.ToolName)
           .Order(StringComparer.Ordinal)
           .ToList();
}
