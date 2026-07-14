// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Services.Infrastructure;

/// <summary>
/// P0 fix (<c>fix/pwa-p0-claim-and-camera</c>) — shared wallet-resolution seam, extracted from
/// <c>Sorcha.Blueprint.Service.Endpoints.ActionEndpoints.ResolveUserWalletAddressesAsync</c> (its
/// original home) once a fourth call site (<c>InstanceActionEndpoints</c>) needed the identical
/// logic. Originally added for <c>GetPendingActions</c> / <c>GetPendingActionCount</c> (#912) and
/// reused as-is by <c>WorkflowDisclosureEndpoints</c> (Feature 176); every caller that needs "which
/// wallet(s) does this authenticated caller control" belongs on this one seam rather than a
/// per-endpoint copy of the claim-then-fallback logic.
/// </summary>
public static class ParticipantWalletResolver
{
    /// <summary>
    /// Resolves the set of wallet addresses the authenticated caller owns, in
    /// priority order:
    /// <list type="number">
    /// <item><description>The <c>wallet_address</c> JWT claim (fast path, zero-RTT).</description></item>
    /// <item><description>Live lookup via <see cref="IWalletServiceClient.GetWalletsByOwnerAsync"/>
    /// keyed on the <c>sub</c> / <see cref="ClaimTypes.NameIdentifier"/> claim,
    /// used when the JWT claim is absent (e.g. because the token was issued
    /// before the user created their first wallet and no refresh has happened
    /// since — or because it is a consumer-tier token, which omits wallet
    /// binding by design under Feature 136).</description></item>
    /// </list>
    /// The fallback is what makes every caller of this method self-healing —
    /// without it, a consumer who signs up and then creates a wallet in the
    /// same session would see empty results forever, even though the server
    /// has the data and knows which wallet it belongs to.
    /// Returns an empty list when neither path yields a wallet — this is a
    /// "couldn't resolve", not a "didn't look"; callers should fail closed
    /// (403 / empty result) rather than treat an empty list as "any wallet".
    /// </summary>
    public static async Task<IReadOnlyList<string>> ResolveUserWalletAddressesAsync(
        HttpContext httpContext,
        IWalletServiceClient walletClient,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var claim = httpContext.User.FindFirst("wallet_address")?.Value;
        if (!string.IsNullOrEmpty(claim))
        {
            return new[] { claim };
        }

        // Wallet Service fallback (no wallet_address claim — e.g. consumer-tier tokens, which
        // omit wallet binding by design under Feature 136). Wallets are keyed by their Owner,
        // and per the Feature 136 / #878 alignment a wallet's Owner is the cross-org
        // platform_user_id, NOT the per-org sub/UserIdentity.Id. For admin tokens the two are
        // equal, but for an org-scoped Consumer (e.g. a verification analyst running the rules
        // agent) they differ — so a sub-keyed lookup silently misses the wallet and the agent
        // never discovers the action it must approve (#912). Prefer platform_user_id, then fall
        // back to sub/NameIdentifier so pre-#878 wallets stay discoverable. Both eras coexist.
        var ownerCandidates = new List<string>();
        var platformUserId = httpContext.User.FindFirst("platform_user_id")?.Value;
        if (!string.IsNullOrEmpty(platformUserId))
        {
            ownerCandidates.Add(platformUserId);
        }

        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(userId) && !ownerCandidates.Contains(userId, StringComparer.Ordinal))
        {
            ownerCandidates.Add(userId);
        }

        if (ownerCandidates.Count == 0)
        {
            return Array.Empty<string>();
        }

        try
        {
            foreach (var owner in ownerCandidates)
            {
                var wallets = await walletClient.GetWalletsByOwnerAsync(owner, cancellationToken);
                var addresses = wallets
                    .Select(w => w.Address)
                    .Where(a => !string.IsNullOrEmpty(a))
                    .ToArray();
                if (addresses.Length > 0)
                {
                    return addresses;
                }
            }

            return Array.Empty<string>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Non-fatal: surface an empty list so the caller can still return a clean response
            // (e.g. a 200 with an empty list, or a 403) rather than cascading a 500. Cancellation
            // is explicitly NOT caught here — tab-close / abort must propagate cleanly so ASP.NET's
            // request pipeline can short circuit without a bogus success response.
            logger.LogWarning(ex,
                "Failed to resolve wallet addresses for user (platform_user_id={PlatformUserId}, sub={UserId}) via Wallet Service fallback; returning empty list",
                platformUserId, userId);
            return Array.Empty<string>();
        }
    }
}
