// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.McpServer.Services;

/// <summary>
/// Advisory tier/role gate for MCP tools (spec 139). Decides, from the caller's F136 tier
/// and roles, which tools the caller may see and attempt. This is a UX/efficiency narrowing
/// only — the API Gateway is the authoritative authorization decision, and this gate can
/// never grant access the gateway would refuse.
/// <para>
/// Tier-primary, role-secondary: consumer-tier callers get citizen + participation tools;
/// platform-tier admins/designers get their slices; participation tools are cross-tier.
/// Service-tier (and enrol-session) tokens are not valid MCP callers and see/​invoke nothing.
/// </para>
/// </summary>
public sealed class McpAuthorizationService : IMcpAuthorizationService
{
    private readonly ICallerContext _caller;
    private readonly ILogger<McpAuthorizationService> _logger;

    public McpAuthorizationService(
        ICallerContext caller,
        ILogger<McpAuthorizationService> logger)
    {
        _caller = caller;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool CanInvokeTool(string toolName)
    {
        if (!_caller.IsAuthenticated)
        {
            _logger.LogWarning("Authorization check failed: no authenticated caller");
            return false;
        }

        if (!IsCallerTier(out var tier))
        {
            _logger.LogWarning(
                "Authorization check failed: tier {Tier} is not a valid MCP caller for {ToolName}",
                _caller.Tier, toolName);
            return false;
        }

        var permitted = ToolEntitlements.IsPermitted(toolName, tier, _caller.Roles);
        if (!permitted)
        {
            _logger.LogWarning(
                "Caller {Subject} (tier {Tier}) denied tool {ToolName} — not entitled",
                _caller.Subject, tier, toolName);
        }

        return permitted;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAuthorizedTools()
    {
        if (!_caller.IsAuthenticated || !IsCallerTier(out var tier))
        {
            return [];
        }

        return ToolEntitlements.VisibleTools(tier, _caller.Roles);
    }

    /// <summary>
    /// True only for the human caller tiers (consumer / platform). Service and enrol-session
    /// tokens are rejected as MCP callers; a null tier (unrecognised audience) is rejected too.
    /// </summary>
    private bool IsCallerTier(out Tier tier)
    {
        switch (_caller.Tier)
        {
            case Tier.Consumer:
                tier = Tier.Consumer;
                return true;
            case Tier.Platform:
                tier = Tier.Platform;
                return true;
            default:
                tier = default;
                return false;
        }
    }
}
