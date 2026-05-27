// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

public class SocialLoginServiceSurfaceTests
{
    private static SocialLoginService BuildService(IDistributedCache cache)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SocialProviders:0:Name"] = "Google",
                ["SocialProviders:0:ClientId"] = "test-client",
                ["SocialProviders:0:ClientSecret"] = "test-secret",
            })
            .Build();
        return new SocialLoginService(
            new TestHttpClientFactory(), cache, config, NullLogger<SocialLoginService>.Instance);
    }

    [Fact]
    public async Task GenerateAuthorizationUrl_WithSurface_RoundTripsSurfaceThroughState()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var svc = BuildService(cache);

        var init = await svc.GenerateAuthorizationUrlAsync(
            "Google", "https://host/auth/social/callback",
            SocialFlowIntent.Login, targetPlatformUserId: null, surface: "wallet");

        var stateJson = Encoding.UTF8.GetString((await cache.GetAsync($"social:state:{init.State}"))!);
        stateJson.Should().Contain("\"Surface\":\"wallet\"");
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
