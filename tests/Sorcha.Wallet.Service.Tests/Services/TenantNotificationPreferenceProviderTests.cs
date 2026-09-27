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
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Tests for TenantNotificationPreferenceProvider mapping and caching behaviour.
/// </summary>
public class TenantNotificationPreferenceProviderTests
{
    private const string UserId = "3f1c9a52-7d44-4e0b-9a1e-5b2c6d8e0f11";

    private static Sorcha.ServiceClients.Auth.IServiceAuthClient ServiceAuth()
    {
        var auth = new Moq.Mock<Sorcha.ServiceClients.Auth.IServiceAuthClient>();
        auth.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("service-token");
        return auth.Object;
    }

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
        return new TenantNotificationPreferenceProvider(httpClient, ServiceAuth(), config, cache, logger);
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
        var result = await provider.GetPreferencesAsync(UserId);

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
        var result = await provider.GetPreferencesAsync(UserId);

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
        var result = await provider.GetPreferencesAsync(UserId);

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
        var result = await provider.GetPreferencesAsync(UserId);

        result.NotificationsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetPreferencesAsync_ServiceFailure_FallsBackToDefaults()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var provider = CreateProvider(response);
        var result = await provider.GetPreferencesAsync(UserId);

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
        var provider = new TenantNotificationPreferenceProvider(httpClient, ServiceAuth(), config, cache, logger);

        // First call hits HTTP
        await provider.GetPreferencesAsync(UserId);
        handler.CallCount.Should().Be(1);

        // Second call hits cache
        await provider.GetPreferencesAsync(UserId);
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
        var result = await provider.GetPreferencesAsync(UserId);

        result.WantsEmail.Should().BeTrue();
    }

    // ── #1694 — the join itself. The old provider called the USER endpoint unauthenticated, so it
    // 401'd on every call; these pin the route, the credential, and what a miss means.

    private static (TenantNotificationPreferenceProvider Provider, FakeHttpMessageHandler Handler) Wired(
        HttpStatusCode status, string? json = null)
    {
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent(json ?? "", System.Text.Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var config = new ConfigurationBuilder().Build();
        var provider = new TenantNotificationPreferenceProvider(
            httpClient, ServiceAuth(), config, new MemoryCache(new MemoryCacheOptions()),
            NullLogger<TenantNotificationPreferenceProvider>.Instance);
        return (provider, handler);
    }

    [Fact]
    public async Task GetPreferencesAsync_CallsTheServiceAuthenticatedInternalRoute_ForThePerson()
    {
        var (provider, handler) = Wired(HttpStatusCode.OK,
            """{"platformUserId":"3f1c9a52-7d44-4e0b-9a1e-5b2c6d8e0f11","notificationsEnabled":false,"notificationMethod":"InApp","notificationFrequency":"DailyDigest"}""");

        var result = await provider.GetPreferencesAsync(UserId);

        handler.LastRequest!.RequestUri!.AbsolutePath
            .Should().Be($"/api/internal/users/{UserId}/notification-preferences");
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be("service-token");
        result.NotificationsEnabled.Should().BeFalse("a saved 'off' must now actually be honoured");
        result.IsRealTime.Should().BeFalse();
    }

    [Fact]
    public async Task GetPreferencesAsync_NoSavedPreferences_UsesTheDefault_WhichIsOn()
    {
        var (provider, _) = Wired(HttpStatusCode.NotFound);

        var result = await provider.GetPreferencesAsync(UserId);

        result.Should().Be(NotificationPreferences.Default);
        result.NotificationsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetPreferencesAsync_AnOwnerThatIsNotAPerson_MakesNoCall()
    {
        var (provider, handler) = Wired(HttpStatusCode.OK, "{}");

        var result = await provider.GetPreferencesAsync("validator:local-validator");

        handler.CallCount.Should().Be(0);
        result.Should().Be(NotificationPreferences.Default);
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        public FakeHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }
}
