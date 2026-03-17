// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Wallet.Service.Services.Implementation;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Tests for TenantNotificationPreferenceProvider mapping and caching behaviour.
/// </summary>
public class TenantNotificationPreferenceProviderTests
{
    private static TenantNotificationPreferenceProvider CreateProvider(
        HttpResponseMessage response)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceClients:TenantService:Address"] = "http://localhost"
            })
            .Build();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = NullLogger<TenantNotificationPreferenceProvider>.Instance;
        return new TenantNotificationPreferenceProvider(httpClient, config, cache, logger);
    }

    [Fact]
    public async Task GetPreferencesAsync_RealTime_MapsCorrectly()
    {
        var json = """{"notificationsEnabled":true,"notificationMethod":"InApp","notificationFrequency":"RealTime"}""";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        var provider = CreateProvider(response);
        var result = await provider.GetPreferencesAsync("user-1");

        result.NotificationsEnabled.Should().BeTrue();
        result.IsRealTime.Should().BeTrue();
        result.WantsEmail.Should().BeFalse();
        result.WantsPush.Should().BeFalse();
    }

    [Fact]
    public async Task GetPreferencesAsync_HourlyDigest_MapsIsRealTimeFalse()
    {
        var json = """{"notificationsEnabled":true,"notificationMethod":"InApp","notificationFrequency":"HourlyDigest"}""";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        var provider = CreateProvider(response);
        var result = await provider.GetPreferencesAsync("user-1");

        result.IsRealTime.Should().BeFalse();
    }

    [Fact]
    public async Task GetPreferencesAsync_DailyDigest_MapsIsRealTimeFalse()
    {
        var json = """{"notificationsEnabled":true,"notificationMethod":"InApp","notificationFrequency":"DailyDigest"}""";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        var provider = CreateProvider(response);
        var result = await provider.GetPreferencesAsync("user-1");

        result.IsRealTime.Should().BeFalse();
    }

    [Fact]
    public async Task GetPreferencesAsync_DisabledNotifications_ReturnsNotEnabled()
    {
        var json = """{"notificationsEnabled":false,"notificationMethod":"InApp","notificationFrequency":"RealTime"}""";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        var provider = CreateProvider(response);
        var result = await provider.GetPreferencesAsync("user-1");

        result.NotificationsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetPreferencesAsync_ServiceFailure_FallsBackToDefaults()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var provider = CreateProvider(response);
        var result = await provider.GetPreferencesAsync("user-1");

        result.NotificationsEnabled.Should().BeTrue();
        result.IsRealTime.Should().BeTrue();
    }

    [Fact]
    public async Task GetPreferencesAsync_CachesResults()
    {
        var json = """{"notificationsEnabled":true,"notificationMethod":"InApp","notificationFrequency":"RealTime"}""";
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceClients:TenantService:Address"] = "http://localhost"
            })
            .Build();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = NullLogger<TenantNotificationPreferenceProvider>.Instance;
        var provider = new TenantNotificationPreferenceProvider(httpClient, config, cache, logger);

        // First call hits HTTP
        await provider.GetPreferencesAsync("user-1");
        handler.CallCount.Should().Be(1);

        // Second call hits cache
        await provider.GetPreferencesAsync("user-1");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPreferencesAsync_EmailMethod_SetsWantsEmail()
    {
        var json = """{"notificationsEnabled":true,"notificationMethod":"InAppPlusEmail","notificationFrequency":"RealTime"}""";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        var provider = CreateProvider(response);
        var result = await provider.GetPreferencesAsync("user-1");

        result.WantsEmail.Should().BeTrue();
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public int CallCount { get; private set; }

        public FakeHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_response);
        }
    }
}
