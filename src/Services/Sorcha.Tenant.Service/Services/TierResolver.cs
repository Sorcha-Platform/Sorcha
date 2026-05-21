// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceDefaults.Auth;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>Why a tier request was refused (spec 136, FR-008).</summary>
public enum TierRejectionReason
{
    /// <summary>Not refused.</summary>
    None = 0,

    /// <summary>The holder is not entitled to the requested tier (e.g. a roleless user asked for Platform).</summary>
    NotEntitled = 1,
}

/// <summary>
/// Outcome of tier selection. When <see cref="Allowed"/> is false the request MUST be
/// refused (never silently downgraded) — see FR-008.
/// </summary>
/// <param name="Allowed">True if a token of <see cref="Tier"/> may be minted.</param>
/// <param name="Tier">The tier that was requested/would be minted.</param>
/// <param name="Reason">Why the request was refused, when not allowed.</param>
public readonly record struct TierResolution(bool Allowed, Tier Tier, TierRejectionReason Reason);

/// <summary>
/// Pure tier-selection logic (spec 136): <c>mintedTier = (requestedTier ?? Consumer) ∩ entitledTiers</c>,
/// with over-request refused rather than downgraded. Stateless and dependency-free so it is trivially
/// testable; callers (issuance paths) record metrics and map a refusal to an HTTP response.
/// </summary>
public static class TierResolver
{
    private static readonly UserRole[] PlatformRoles =
        [UserRole.SystemAdmin, UserRole.Administrator, UserRole.Designer, UserRole.Auditor];

    /// <summary>
    /// The tiers a holder may obtain given their roles in the **active org context**.
    /// Consumer for every authenticated human; Platform additionally when any platform role is held.
    /// Service and EnrolSession are never selectable here — they are minted by their dedicated issuers.
    /// </summary>
    public static IReadOnlySet<Tier> EntitledTiers(IEnumerable<UserRole>? rolesInActiveContext)
    {
        var set = new HashSet<Tier> { Tier.Consumer };
        if (rolesInActiveContext is not null && rolesInActiveContext.Any(r => PlatformRoles.Contains(r)))
        {
            set.Add(Tier.Platform);
        }
        return set;
    }

    /// <summary>
    /// Resolves the tier to mint. A null <paramref name="requestedTier"/> defaults to Consumer
    /// (lowest privilege, FR-009). If the resolved tier is not entitled, the result is a refusal
    /// (FR-008) — the caller must not downgrade.
    /// </summary>
    public static TierResolution Resolve(Tier? requestedTier, IEnumerable<UserRole>? rolesInActiveContext)
    {
        var minted = requestedTier ?? Tier.Consumer;
        var entitled = EntitledTiers(rolesInActiveContext);
        return entitled.Contains(minted)
            ? new TierResolution(true, minted, TierRejectionReason.None)
            : new TierResolution(false, minted, TierRejectionReason.NotEntitled);
    }

    /// <summary>
    /// Resolves a tier <em>preference</em> against entitlement (spec 136, the decision of 2026-05-21):
    /// the tier follows the person, not the UI host. A citizen / non-admin is consumer-tier wherever
    /// they sign in; an admin reaches platform in org context.
    /// <list type="bullet">
    ///   <item>If the preference is entitled → mint it.</item>
    ///   <item>If NOT entitled and the request was <paramref name="isExplicit"/> (a deliberate
    ///   <c>tier=platform</c> request) → <b>refuse</b> (FR-008 over-request, 403).</item>
    ///   <item>If NOT entitled and the preference was merely derived from the destination (e.g. a
    ///   citizen landing on the <c>/app</c> SPA) → <b>downgrade</b> to the highest entitled tier
    ///   (Consumer), so the citizen keeps working rather than being locked out.</item>
    /// </list>
    /// </summary>
    /// <param name="preferred">The preferred tier (from an explicit hint or the destination).</param>
    /// <param name="isExplicit">True when <paramref name="preferred"/> came from an explicit caller request.</param>
    /// <param name="rolesInActiveContext">The holder's roles in the active org context.</param>
    public static TierResolution ResolvePreference(Tier preferred, bool isExplicit, IEnumerable<UserRole>? rolesInActiveContext)
    {
        var entitled = EntitledTiers(rolesInActiveContext);
        if (entitled.Contains(preferred))
        {
            return new TierResolution(true, preferred, TierRejectionReason.None);
        }

        if (isExplicit)
        {
            // A deliberate over-request is refused, never silently downgraded (FR-008).
            return new TierResolution(false, preferred, TierRejectionReason.NotEntitled);
        }

        // Destination-derived preference the holder is not entitled to → downgrade to the
        // lowest-privilege human tier (Consumer is entitled to every authenticated human).
        return new TierResolution(true, Tier.Consumer, TierRejectionReason.None);
    }
}
