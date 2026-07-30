// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sorcha.ServiceClients.Auth;
using Sorcha.Wallet.Core.Repositories.Interfaces;

namespace Sorcha.Wallet.Service.Authorization;

/// <summary>
/// Binds a request to the wallet named in its route.
///
/// <para>G1 (catch-up security review 2026-07-29): the wallet-scoped route groups authorized on
/// <c>CanManageWallets</c>, which is literally "the token carries any non-empty <c>org_id</c>, OR it
/// is a service token". It never compared the caller to the <c>{walletAddress}</c> segment, and the
/// handlers in those groups did not either. Consumer-tier citizen tokens carry <c>org_id</c> (their
/// home org, Feature 136), so every authenticated citizen satisfied the policy for every wallet.
/// Wallet addresses are public, and the API Gateway proxies <c>/api/v1/wallets/{**catch-all}</c>
/// under <c>RequireAuthenticated</c> only — so this was browser-reachable.</para>
///
/// <para>This gate exists as one shared primitive rather than a check copied into each handler
/// precisely because the original defect was per-handler checks that were simply absent. A new
/// wallet-scoped group gets the gate by calling <see cref="WalletOwnershipEndpointExtensions
/// .RequireWalletOwnership{TBuilder}"/>, and a guard test asserts every route carrying a wallet
/// address in its template has it.</para>
/// </summary>
public static class WalletOwnershipGate
{
    /// <summary>Route-value names that carry a wallet address, in probe order.</summary>
    private static readonly string[] RouteValueNames = ["walletAddress", "address"];

    /// <summary>
    /// Decides whether the caller may act on the wallet named in the route.
    /// Returns <c>null</c> to allow the request through, or the <see cref="IResult"/> to short-circuit with.
    /// </summary>
    public static async Task<IResult?> EvaluateAsync(HttpContext http, CancellationToken ct = default)
    {
        var walletAddress = ResolveRouteWalletAddress(http);
        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            // The gate was applied to a route with no wallet address in its template. Fail closed:
            // silently allowing would make a mis-wiring look like a working control.
            Logger(http).LogError(
                "Wallet-ownership gate is applied to {Path} but no wallet-address route value was "
                + "found. Refusing the request — check the route template.", http.Request.Path);
            return Results.Problem(
                title: "Wallet ownership could not be determined",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // Service tokens legitimately act on wallets they do not own. Blueprint's credential
        // issuance posts to the ISSUING ORG's wallet while the recipient is a different citizen
        // entirely, so removing this bypass would break issuance. The tier boundary is what
        // constrains it: RequireService additionally demands this installation's :service audience.
        if (IsServiceToken(http.User))
        {
            return null;
        }

        var caller = ResolveCallerIdentity(http.User);
        if (string.IsNullOrWhiteSpace(caller))
        {
            return Results.Unauthorized();
        }

        var repository = http.RequestServices.GetRequiredService<IWalletRepository>();
        var wallet = await repository.GetByAddressAsync(walletAddress, cancellationToken: ct);

        if (wallet is null)
        {
            // Matches the house pattern in WalletEndpoints (Get/Sign/Decrypt): unknown wallet is a
            // 404 for everyone, so the response does not depend on who is asking.
            return Results.NotFound(new { error = "Wallet not found." });
        }

        if (string.Equals(wallet.Owner, caller, StringComparison.Ordinal))
        {
            return null;
        }

        Logger(http).LogWarning(
            "SEC-AUDIT: caller {Caller} attempted {Method} {Path} on wallet {Wallet} owned by {Owner}",
            caller, http.Request.Method, http.Request.Path, walletAddress, wallet.Owner);

        return Results.Forbid();
    }

    /// <summary>Reads the wallet address from the route, tolerating either parameter spelling.</summary>
    private static string? ResolveRouteWalletAddress(HttpContext http)
    {
        foreach (var name in RouteValueNames)
        {
            if (http.Request.RouteValues.TryGetValue(name, out var value)
                && value?.ToString() is { Length: > 0 } address)
            {
                return address;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the caller identity that <c>Wallets.Owner</c> is stamped with. MUST match
    /// <c>WalletEndpoints.GetCurrentUser</c>: post-#878 wallet creation prefers
    /// <c>platform_user_id</c> (the cross-org person), falling back to <c>NameIdentifier</c> for
    /// service / recovery tokens and for wallets created before that cutover. Comparing against the
    /// wrong claim would not fail loudly — it would silently deny every citizen their own wallet.
    /// </summary>
    private static string? ResolveCallerIdentity(ClaimsPrincipal user) =>
        user.FindFirstValue(TokenClaimConstants.PlatformUserId)
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

    private static bool IsServiceToken(ClaimsPrincipal user) =>
        user.Claims.Any(c =>
            c.Type == TokenClaimConstants.TokenType
            && c.Value == TokenClaimConstants.TokenTypeService);

    private static ILogger Logger(HttpContext http) =>
        http.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(WalletOwnershipGate).FullName!);
}
