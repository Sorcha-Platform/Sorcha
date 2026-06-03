// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;

using Sorcha.Wallet.Service.Endpoints;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Endpoint-metadata regression tests for the system-wallet endpoints (Feature 147 / review H1).
/// Guards against re-introducing <c>AllowAnonymous</c> and confirms each endpoint requires its
/// intended policy. Uses <see cref="EndpointAuthorizationMetadata"/> to map the real
/// <see cref="WalletEndpoints.MapWalletEndpoints"/> and inspect route metadata — no request is issued.
/// </summary>
public class SystemWalletEndpointAuthorizationTests
{
    private static IReadOnlyList<Microsoft.AspNetCore.Routing.RouteEndpoint> Endpoints() =>
        EndpointAuthorizationMetadata.Collect(rb => rb.MapWalletEndpoints());

    [Fact]
    public void SystemWalletCreate_HasNoAllowAnonymous_AndRequiresService()
    {
        var create = EndpointAuthorizationMetadata.FindByPath(Endpoints(), "api/v1/wallets/system");

        create.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull(
            "the system-wallet create endpoint must not be anonymous (review H1)");
        create.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(a => a.Policy == AuthorizationPolicies.RequireService).Should().BeTrue(
            "system-wallet create must require the RequireService policy");
    }

    [Fact]
    public void SystemWalletRecover_HasNoAllowAnonymous_AndRequiresRecoveryPolicy()
    {
        var recover = EndpointAuthorizationMetadata.FindByPath(Endpoints(), "api/v1/wallets/system/recover");

        recover.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull(
            "the system-wallet recover endpoint must not be anonymous (review H1)");
        recover.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(a => a.Policy == "CanRecoverSystemWallet").Should().BeTrue(
            "system-wallet recover must require the CanRecoverSystemWallet policy");
    }
}
