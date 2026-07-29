// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Routing;

using Sorcha.Wallet.Service.Authorization;
using Sorcha.Wallet.Service.Endpoints;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// G1 (catch-up security review 2026-07-29) — the wallet-ownership gate must actually be WIRED to
/// every wallet-scoped route, not merely to exist.
///
/// <para>An endpoint filter is invisible to route metadata, so "someone added a new
/// <c>{walletAddress}</c> group and forgot the gate" would be undetectable — which is precisely how
/// the original defect survived: the ownership check was not subtly wrong, it was simply absent, in
/// several groups at once. <c>RequireWalletOwnership()</c> therefore also stamps
/// <see cref="WalletOwnershipRequiredMetadata"/>, and these tests assert its presence per route.</para>
/// </summary>
public class WalletOwnershipWiringTests
{
    public static TheoryData<string, Action<IEndpointRouteBuilder>> GatedGroups() => new()
    {
        { "credentials", rb => rb.MapCredentialEndpoints() },
        { "delegation/access", rb => rb.MapDelegationEndpoints() },
    };

    [Theory]
    [MemberData(nameof(GatedGroups))]
    public void EveryWalletScopedRoute_CarriesTheOwnershipGate(
        string groupName, Action<IEndpointRouteBuilder> map)
    {
        var endpoints = EndpointAuthorizationMetadata.Collect(map);
        endpoints.Should().NotBeEmpty($"the {groupName} group must map at least one route");

        var walletScoped = endpoints
            .Where(e => e.RoutePattern.RawText!.Contains("{walletAddress}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        walletScoped.Should().NotBeEmpty(
            $"the {groupName} group is addressed per-wallet, so its templates must carry {{walletAddress}}");

        foreach (var endpoint in walletScoped)
        {
            endpoint.Metadata.GetMetadata<WalletOwnershipRequiredMetadata>().Should().NotBeNull(
                $"'{endpoint.RoutePattern.RawText}' names a wallet in its route, so the caller must be "
                + "bound to that wallet — CanManageWallets alone only proves they are some "
                + "authenticated org-scoped caller, which every citizen is");
        }
    }

    [Theory]
    [InlineData("PATCH", "api/v1/wallets/{address}")]
    [InlineData("DELETE", "api/v1/wallets/{address}")]
    public void MutatingPerAddressWalletRoutes_CarryTheOwnershipGate(string method, string template)
    {
        // These two had NO ownership check of any kind: GET/sign/decrypt/decapsulate on the same
        // group each verify wallet.Owner inline, but rename and soft-delete did not — so any
        // authenticated citizen could delete any wallet by address.
        var endpoints = EndpointAuthorizationMetadata.Collect(rb => rb.MapWalletEndpoints());

        var endpoint = endpoints.Single(e =>
            string.Equals(e.RoutePattern.RawText?.TrimStart('/'), template, StringComparison.OrdinalIgnoreCase)
            && e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IHttpMethodMetadata>()!
                .HttpMethods.Contains(method));

        endpoint.Metadata.GetMetadata<WalletOwnershipRequiredMetadata>().Should().NotBeNull(
            $"{method} {template} mutates a wallet identified purely by address, so the caller "
            + "must be bound to that wallet");
    }

    [Theory]
    [MemberData(nameof(GatedGroups))]
    public void GatedRoutes_StillCarryTheirAuthorizationPolicy(
        string groupName, Action<IEndpointRouteBuilder> map)
    {
        // The ownership gate composes with authorization; it must not have displaced it.
        var endpoints = EndpointAuthorizationMetadata.Collect(map);

        foreach (var endpoint in endpoints.Where(e =>
                     e.RoutePattern.RawText!.Contains("{walletAddress}", StringComparison.OrdinalIgnoreCase)))
        {
            endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>()
                .Should().BeNull($"'{endpoint.RoutePattern.RawText}' must never be anonymous");

            endpoint.Metadata
                .GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                .Should().NotBeEmpty(
                    $"'{endpoint.RoutePattern.RawText}' must still require authorization ({groupName})");
        }
    }
}
