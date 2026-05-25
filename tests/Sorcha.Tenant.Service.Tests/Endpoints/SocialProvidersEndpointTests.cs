// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using Sorcha.Tenant.Service.Endpoints;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

public class SocialProvidersEndpointTests
{
    [Fact]
    public void ListProviders_ReturnsConfiguredProviderNames()
    {
        var social = new Mock<ISocialLoginService>();
        social.Setup(s => s.GetConfiguredProviderNames()).Returns(new[] { "Google", "Apple" });

        var result = SocialLoginEndpoints.ListConfiguredProvidersForTest(social.Object);

        var ok = result.Should().BeOfType<Ok<SocialProvidersResponse>>().Subject;
        ok.Value!.Providers.Should().BeEquivalentTo(new[] { "Google", "Apple" });
    }
}
