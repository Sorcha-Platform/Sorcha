// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Sorcha.Tenant.Models.Identity;

namespace Sorcha.ServiceClients.Auth;

/// <summary>
/// The one home for reading a person's id out of a token as a TYPED id (issue #1709, CLAUDE.md
/// pattern 26).
/// </summary>
/// <remarks>
/// A human-tier JWT carries both ids — <c>platform_user_id</c> (<see cref="PlatformUserId"/>,
/// account-wide, what the inbox is keyed on) and <c>sub</c> (<see cref="UserIdentityId"/>,
/// org-scoped). Because both parsed to a bare <see cref="Guid"/>, the wrong one was always within
/// reach; PR #1708 found five call sites that took it. Reading through these helpers makes the kind
/// part of the value, so handing a <c>sub</c> to an inbox write no longer compiles.
/// </remarks>
public static class UserIdClaims
{
    /// <summary>The standard JWT subject claim — the org-scoped <c>UserIdentity.Id</c>.</summary>
    public const string Subject = "sub";

    /// <summary>
    /// Reads the <c>platform_user_id</c> claim as a <see cref="PlatformUserId"/>. Returns
    /// <c>null</c> when the claim is absent, not a GUID, or <see cref="Guid.Empty"/>. Never falls
    /// back to <c>sub</c> — that is a different kind of id.
    /// </summary>
    /// <param name="principal">The authenticated principal.</param>
    public static PlatformUserId? GetPlatformUserId(this ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirst(TokenClaimConstants.PlatformUserId)?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? new PlatformUserId(id) : null;
    }

    /// <summary>
    /// Reads the subject as a <see cref="UserIdentityId"/> — <see cref="ClaimTypes.NameIdentifier"/>
    /// first (the inbound-mapped form of <c>sub</c>), then the raw <c>sub</c>. Returns <c>null</c>
    /// when absent, not a GUID, or <see cref="Guid.Empty"/>.
    /// </summary>
    /// <param name="principal">The authenticated principal.</param>
    public static UserIdentityId? GetUserIdentityId(this ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? principal?.FindFirst(Subject)?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? new UserIdentityId(id) : null;
    }
}
