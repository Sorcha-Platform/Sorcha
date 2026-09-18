// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.McpServer.Infrastructure;

/// <summary>
/// The ambient identity behind the current MCP invocation. Resolved once per process on
/// the stdio transport, and (in a later feature) once per request on the HTTP transport.
/// <para>
/// This is the single seam tools and authorization read for caller identity, and the
/// <see cref="CallerTokenForwardingHandler"/> reads <see cref="RawToken"/> to forward the
/// caller's credentials to backend services so the platform (API Gateway) enforces
/// F136-tiered privileges authoritatively. See spec 139 (MCP Server Foundation).
/// </para>
/// </summary>
public interface ICallerContext
{
    /// <summary>The caller's bearer token, forwarded verbatim to backends. Never logged.</summary>
    string? RawToken { get; }

    /// <summary>
    /// The caller's trust tier, derived from the token audience. Null when no valid token
    /// is present or the audience is not a recognised tier audience.
    /// </summary>
    Tier? Tier { get; }

    /// <summary>The caller's roles in Sorcha MCP form (e.g. <c>sorcha:admin</c>). Empty for consumer tier.</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>The caller's home/organisation identifier (<c>org_id</c>), if any.</summary>
    string? OrganizationId { get; }

    /// <summary>The caller's subject identifier (<c>sub</c>), if any.</summary>
    string? Subject { get; }

    /// <summary>
    /// The caller's platform user id (<c>platform_user_id</c>), falling back to <see cref="Subject"/>.
    /// This is what a wallet's <c>Owner</c> holds, so it is how the caller's own wallets are found —
    /// the same resolution order the services' own <c>ParticipantWalletResolver</c> uses, because a
    /// consumer-tier token carries no <c>wallet_address</c> claim (Feature 136).
    /// </summary>
    string? PlatformUserId { get; }

    /// <summary>True when a valid, unexpired token is resolved for this caller.</summary>
    bool IsAuthenticated { get; }
}

/// <summary>
/// Resolves a <see cref="Tier"/> from JWT audience values. The MCP server matches on the
/// installation-namespaced tier <em>suffix</em> (<c>:consumer</c> / <c>:platform</c> /
/// <c>:service</c> / <c>:enrol-session</c>) for transport-time tool gating; the API Gateway
/// remains authoritative for the full installation-namespaced audience + signature checks.
/// </summary>
public static class TierResolution
{
    /// <summary>
    /// Returns the first recognised tier among the supplied audiences, or null if none match.
    /// </summary>
    public static Tier? Resolve(IEnumerable<string>? audiences)
    {
        if (audiences is null)
        {
            return null;
        }

        foreach (var audience in audiences)
        {
            if (string.IsNullOrWhiteSpace(audience))
            {
                continue;
            }

            var suffix = audience.Contains(':')
                ? audience[(audience.LastIndexOf(':') + 1)..]
                : audience;

            switch (suffix)
            {
                case "consumer": return ServiceDefaults.Auth.Tier.Consumer;
                case "platform": return ServiceDefaults.Auth.Tier.Platform;
                case "service": return ServiceDefaults.Auth.Tier.Service;
                case "enrol-session": return ServiceDefaults.Auth.Tier.EnrolSession;
            }
        }

        return null;
    }
}
