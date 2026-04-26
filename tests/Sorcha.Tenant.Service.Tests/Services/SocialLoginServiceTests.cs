// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="SocialLoginService.GetConfiguredProviderNames"/> —
/// the visibility surface that drives conditional rendering of provider
/// buttons on the signup and login pages. Feature 115 FR-001/FR-002.
/// </summary>
public class SocialLoginServiceTests
{
    private static SocialLoginService CreateService(
        params (string Name, string ClientId, string ClientSecret)[] providers)
    {
        var configData = new List<KeyValuePair<string, string?>>();
        for (var i = 0; i < providers.Length; i++)
        {
            configData.Add(new($"SocialProviders:{i}:Name", providers[i].Name));
            configData.Add(new($"SocialProviders:{i}:ClientId", providers[i].ClientId));
            configData.Add(new($"SocialProviders:{i}:ClientSecret", providers[i].ClientSecret));
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        return new SocialLoginService(
            new Mock<IHttpClientFactory>().Object,
            new Mock<IDistributedCache>().Object,
            configuration,
            NullLogger<SocialLoginService>.Instance);
    }

    [Fact]
    public void GetConfiguredProviderNames_NoProviders_ReturnsEmpty()
    {
        var service = CreateService();
        service.GetConfiguredProviderNames().Should().BeEmpty();
    }

    [Fact]
    public void GetConfiguredProviderNames_FullyConfiguredProvider_IsIncluded()
    {
        var service = CreateService(("Google", "client-id", "client-secret"));
        service.GetConfiguredProviderNames().Should().ContainSingle().Which.Should().Be("Google");
    }

    [Fact]
    public void GetConfiguredProviderNames_EmptyClientId_IsExcluded()
    {
        var service = CreateService(
            ("Google", "google-id", "google-secret"),
            ("GitHub", "", "github-secret"));

        service.GetConfiguredProviderNames().Should().Equal("Google");
    }

    [Fact]
    public void GetConfiguredProviderNames_EmptyClientSecret_IsExcluded()
    {
        var service = CreateService(
            ("Google", "google-id", "google-secret"),
            ("GitHub", "github-id", ""));

        service.GetConfiguredProviderNames().Should().Equal("Google");
    }

    [Fact]
    public void GetConfiguredProviderNames_BothCredsBlank_IsExcluded()
    {
        // The default n1 .env shipped with empty placeholder values; the resulting
        // SocialProviders entry should not appear as a configured provider.
        var service = CreateService(
            ("Google", "", ""),
            ("GitHub", "github-id", "github-secret"));

        service.GetConfiguredProviderNames().Should().Equal("GitHub");
    }

    [Fact]
    public void GetConfiguredProviderNames_TwoFullyConfigured_BothIncluded()
    {
        var service = CreateService(
            ("Google", "google-id", "google-secret"),
            ("GitHub", "github-id", "github-secret"));

        service.GetConfiguredProviderNames().Should().Equal("Google", "GitHub");
    }
}
