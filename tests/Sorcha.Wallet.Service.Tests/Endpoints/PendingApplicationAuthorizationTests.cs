// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;

using Sorcha.Wallet.Service.Endpoints;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Endpoint-metadata regression test for the pending-application notice endpoints (Feature 147 / review F124).
/// Confirms the group requires the consumer-tier audience so a platform token cannot read/set a
/// citizen's notice. Only the pending-applications group is mapped, so every collected endpoint
/// belongs to it. Inspects route metadata via <see cref="EndpointAuthorizationMetadata"/>.
/// </summary>
public class PendingApplicationAuthorizationTests
{
    [Fact]
    public void PendingApplicationEndpoints_RequireConsumerAudience()
    {
        var endpoints = EndpointAuthorizationMetadata.Collect(rb => rb.MapPendingApplicationEndpoints());

        endpoints.Should().NotBeEmpty("the pending-applications endpoints must be mapped");

        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull(
                "a citizen notice surface must not be anonymous");
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Any(a => a.Policy == AuthorizationPolicies.RequireConsumerAudience).Should().BeTrue(
                $"endpoint '{endpoint.RoutePattern.RawText}' must require the consumer-tier audience (review F124)");
        }
    }
}
