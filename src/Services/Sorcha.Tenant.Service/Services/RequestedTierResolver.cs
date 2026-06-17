// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceDefaults.Auth;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Derives the <em>requested</em> trust tier from an authentication entry (spec 136,
/// contract <c>auth-entry-tier-request</c>, FR-010). This is purely the request side —
/// the tier actually minted is <c>requested ∩ entitlement</c> (see <see cref="TierResolver"/>),
/// so an over-request is rejected, never silently downgraded.
/// <para>
/// Resolution order:
/// <list type="number">
///   <item>Explicit <c>tier</c> hint (<c>consumer</c> | <c>platform</c>); any other value is ignored.</item>
///   <item><c>returnTo</c> destination — a path under the wallet mount (<c>/wallet/…</c>) or an
///   allow-listed consumer host ⇒ <see cref="Tier.Consumer"/>; the platform SPA mount
///   (<c>/app/…</c>) ⇒ <see cref="Tier.Platform"/>. (There is no separate <c>/admin</c> route — the
///   whole platform/admin experience is the <c>/app</c> Blazor SPA; the consumer wallet is <c>/wallet</c>.)</item>
///   <item>Default ⇒ <see cref="Tier.Consumer"/> (lowest privilege, FR-009).</item>
/// </list>
/// </para>
/// Only the two human tiers are selectable here — <see cref="Tier.Service"/> and
/// <see cref="Tier.EnrolSession"/> are minted by their dedicated issuers, never via a human entry.
/// </summary>
public static class RequestedTierResolver
{
    /// <summary>
    /// Resolves the requested tier for an interactive/social/SSO sign-in. Returns a concrete
    /// human tier (never null) — callers pass it to <see cref="TierResolver.Resolve"/> for the
    /// entitlement gate.
    /// </summary>
    /// <param name="explicitTierHint">Optional caller-supplied <c>tier</c> hint.</param>
    /// <param name="returnTo">The post-authentication destination, if any.</param>
    /// <param name="allowlist">The return-to allowlist used to classify trusted consumer hosts.</param>
    public static Tier Resolve(string? explicitTierHint, string? returnTo, ReturnToAllowlistOptions? allowlist = null)
    {
        var explicitTier = ParseTierHint(explicitTierHint);
        if (explicitTier is not null)
        {
            return explicitTier.Value;
        }

        var fromReturnTo = ClassifyReturnTo(returnTo, allowlist);
        if (fromReturnTo is not null)
        {
            return fromReturnTo.Value;
        }

        return Tier.Consumer;
    }

    /// <summary>
    /// Parses an explicit <c>tier</c> hint. Only the human tiers <c>consumer</c>/<c>platform</c>
    /// are accepted (case/whitespace-insensitive); any other value (including the machine tiers)
    /// returns null and the caller falls through to the next rule.
    /// </summary>
    public static Tier? ParseTierHint(string? hint) => hint?.Trim().ToLowerInvariant() switch
    {
        "consumer" => Tier.Consumer,
        "platform" => Tier.Platform,
        _ => null,
    };

    /// <summary>
    /// Classifies a <c>returnTo</c> destination to a tier, or null when it is not classifiable.
    /// <c>/wallet</c> or an allow-listed consumer host ⇒ <see cref="Tier.Consumer"/>; the <c>/app</c>
    /// SPA ⇒ <see cref="Tier.Platform"/>. Unlike <see cref="Resolve"/> this does NOT apply the
    /// Consumer default — callers that must fail safe to Platform (e.g. the web SPA login, where the
    /// default destination is the platform <c>/app</c> experience) use <c>ClassifyReturnTo(...) ?? Tier.Platform</c>.
    /// </summary>
    public static Tier? ClassifyReturnTo(string? returnTo, ReturnToAllowlistOptions? allowlist = null)
    {
        if (string.IsNullOrWhiteSpace(returnTo))
        {
            return null;
        }

        if (Uri.TryCreate(returnTo, UriKind.Absolute, out var absolute))
        {
            // A recognised /app or /wallet path expresses tier intent directly and MUST win over the
            // host heuristic. The platform's own host (localhost, n1.sorcha.dev, ...) is in the allowlist
            // for redirect-safety, NOT as a consumer-host marker — without path-first, a self-referential
            // /app return (RedirectToLogin sends the absolute current URL) is mis-classified Consumer and
            // an entitled admin is minted a roleless consumer token, losing the entire admin surface.
            var byPath = ClassifyPath(absolute.AbsolutePath);
            if (byPath is not null)
            {
                return byPath;
            }

            // No tier-bearing path → an allow-listed *external consumer* host (e.g. a council page) is a
            // consumer destination. The allowlist itself fail-closes on open-redirects.
            if (allowlist is not null && allowlist.IsAllowed(returnTo))
            {
                return Tier.Consumer;
            }

            return null;
        }

        return ClassifyPath(returnTo);
    }

    private static Tier? ClassifyPath(string path)
    {
        // The consumer wallet PWA is mounted at /wallet; the platform/admin experience is the
        // /app Blazor SPA (there is no separate /admin route). Anything else is unclassified.
        if (StartsWithSegment(path, "/wallet"))
        {
            return Tier.Consumer;
        }

        if (StartsWithSegment(path, "/app"))
        {
            return Tier.Platform;
        }

        return null;
    }

    /// <summary>
    /// True if <paramref name="path"/> equals <paramref name="segment"/> or begins with it as a
    /// full path segment (so <c>/wallet</c> and <c>/wallet/x</c> match, but <c>/wallets</c> does not).
    /// </summary>
    private static bool StartsWithSegment(string path, string segment)
    {
        if (!path.StartsWith(segment, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.Length == segment.Length || path[segment.Length] is '/' or '?' or '#';
    }
}
