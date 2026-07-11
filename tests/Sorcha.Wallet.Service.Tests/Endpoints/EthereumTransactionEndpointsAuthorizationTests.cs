// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authorization;
using Sorcha.Wallet.Service.Endpoints;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Feature 182 (Phase 4) — endpoint authorization metadata for native ETH transacting. Value-moving
/// endpoints (send + preview) must require the dedicated <c>CanTransactEthereum</c> policy; the status
/// endpoint must at least require authentication. Inspects route metadata (no server, no request).
/// </summary>
public class EthereumTransactionEndpointsAuthorizationTests
{
    [Fact]
    public void SendAndPreview_RequireCanTransactEthereum()
    {
        var endpoints = EndpointAuthorizationMetadata.Collect(rb => rb.MapEthereumTransactionEndpoints());
        endpoints.Should().NotBeEmpty();

        var valueMoving = endpoints.Where(e =>
            e.RoutePattern.RawText!.Contains("/wallets/", StringComparison.OrdinalIgnoreCase)).ToList();
        valueMoving.Should().HaveCountGreaterThanOrEqualTo(2, "send and preview are both wallet-scoped");

        foreach (var endpoint in valueMoving)
        {
            endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Any(a => a.Policy == "CanTransactEthereum").Should().BeTrue(
                    $"'{endpoint.RoutePattern.RawText}' must gate value movement behind CanTransactEthereum");
        }
    }

    [Fact]
    public void StatusEndpoint_RequiresAuthentication()
    {
        var endpoints = EndpointAuthorizationMetadata.Collect(rb => rb.MapEthereumTransactionEndpoints());
        var status = endpoints.Single(e =>
            e.RoutePattern.RawText!.Contains("/ethereum/transactions/", StringComparison.OrdinalIgnoreCase)
            && !e.RoutePattern.RawText!.Contains("/wallets/", StringComparison.OrdinalIgnoreCase));

        status.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
        status.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().NotBeEmpty("status requires authentication");
    }
}
