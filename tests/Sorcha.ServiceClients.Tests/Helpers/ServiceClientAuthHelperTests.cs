// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.ServiceClients.Tests.Helpers;

/// <summary>
/// The credential path every typed Sorcha client shares. Two host shapes must stay
/// DISTINGUISHABLE here, because collapsing them turns a fail-closed credential check into a
/// fail-open one:
/// <list type="bullet">
/// <item>a host with NO ServiceAuth credential material at all authorises by forwarding the
/// caller's bearer (the public MCP server) — the token demand is skipped and the Authorization
/// header is left to that host's DelegatingHandler;</item>
/// <item>a host that IS configured, including one configured incompletely, still demands a token
/// and still fails loudly.</item>
/// </list>
/// </summary>
public class ServiceClientAuthHelperTests
{
    private static HttpClient NewClient() => new() { BaseAddress = new Uri("http://localhost") };

    [Fact]
    public async Task SetAuthHeaderAsync_HostWithNoCredentials_DoesNotEvenAskForAToken()
    {
        var auth = new Mock<IServiceAuthClient>();
        auth.SetupGet(a => a.HasNoCredentialsConfigured).Returns(true);
        using var httpClient = NewClient();

        await ServiceClientAuthHelper.SetAuthHeaderAsync(
            httpClient, auth.Object, NullLogger.Instance, "Blueprint", TestContext.Current.CancellationToken);

        httpClient.DefaultRequestHeaders.Authorization.Should().BeNull(
            "the caller-token forwarding handler stamps the header for this host");
        auth.Verify(a => a.GetTokenAsync(It.IsAny<CancellationToken>()), Times.Never,
            "asking would throw, the client would swallow it, and the request would never be made");
    }

    [Fact]
    public async Task SetAuthHeaderAsync_ConfiguredHost_StillAcquiresAndSetsTheServiceToken()
    {
        var auth = new Mock<IServiceAuthClient>();
        auth.SetupGet(a => a.HasNoCredentialsConfigured).Returns(false);
        auth.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("service-token");
        using var httpClient = NewClient();

        await ServiceClientAuthHelper.SetAuthHeaderAsync(
            httpClient, auth.Object, NullLogger.Instance, "Blueprint", TestContext.Current.CancellationToken);

        httpClient.DefaultRequestHeaders.Authorization.Should().NotBeNull();
        httpClient.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        httpClient.DefaultRequestHeaders.Authorization.Parameter.Should().Be("service-token");
    }

    [Fact]
    public async Task SetAuthHeaderAsync_ConfiguredButBrokenHost_StillFailsLoudly()
    {
        var auth = new Mock<IServiceAuthClient>();
        auth.SetupGet(a => a.HasNoCredentialsConfigured).Returns(false);
        auth.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ServiceAuth:ClientSecret not configured"));
        using var httpClient = NewClient();

        var act = async () => await ServiceClientAuthHelper.SetAuthHeaderAsync(
            httpClient, auth.Object, NullLogger.Instance, "Blueprint", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ServiceAuth:ClientSecret*",
                "the helper must never absorb a broken credential configuration");
    }

    [Fact]
    public void HasNoCredentialsConfigured_DefaultsToFalse_SoAForgottenImplementationStaysFailClosed()
    {
        // The property is phrased negatively on purpose: default(bool) is the CONSERVATIVE value.
        var auth = new Mock<IServiceAuthClient>();

        auth.Object.HasNoCredentialsConfigured.Should().BeFalse();
    }

    // ---- The real client's own view of "configured" -------------------------------------------

    private static ServiceAuthClient BuildClient(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceAuthClient(
            new HttpClient { BaseAddress = new Uri("http://tenant-service") },
            configuration,
            NullLogger<ServiceAuthClient>.Instance);
    }

    [Fact]
    public void ServiceAuthClient_WithNoServiceAuthConfiguration_ReportsNoCredentials()
    {
        using var client = BuildClient([]);

        client.HasNoCredentialsConfigured.Should().BeTrue();
    }

    [Theory]
    [InlineData("ServiceAuth:ClientId", "mcp-server")]
    [InlineData("ServiceAuth:ClientSecret", "s3cret")]
    public void ServiceAuthClient_WithAnyCredentialMaterial_ReportsConfigured(string key, string value)
    {
        using var client = BuildClient(new Dictionary<string, string?> { [key] = value });

        client.HasNoCredentialsConfigured.Should().BeFalse(
            "partial configuration is CONFIGURED — the missing half must fail loudly, not be waived");
    }

    [Fact]
    public async Task ServiceAuthClient_PartiallyConfigured_StillThrowsOnTheMissingHalf()
    {
        using var client = BuildClient(new Dictionary<string, string?>
        {
            ["ServiceAuth:ClientId"] = "some-service",
        });

        var act = async () => await client.GetTokenAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ServiceAuth:ClientSecret*",
                "a service that meant to authenticate as itself must not silently degrade to anonymous");
    }
}
