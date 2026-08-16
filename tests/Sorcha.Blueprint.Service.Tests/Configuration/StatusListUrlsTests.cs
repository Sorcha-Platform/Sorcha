// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Sorcha.Blueprint.Service.Configuration;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Configuration;

/// <summary>
/// Issue #1447: every issued credential pointed its status list at the unroutable
/// placeholder https://sorcha.example because nothing set StatusList:BaseUrl and the
/// inline defaults used the placeholder. The URL is signed into the credential, so a
/// wrong value is permanently broken — these tests pin the resolver's fail-closed
/// behaviour in Production/Staging and its deployment-local default elsewhere.
/// </summary>
public class StatusListUrlsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs)
    {
        var builder = new ConfigurationBuilder();
        builder.AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value));
        return builder.Build();
    }

    [Fact]
    public void Resolve_ConfiguredValues_AreReturnedVerbatim()
    {
        var config = Config(
            ("StatusList:BaseUrl", "https://n1.sorcha.dev/api/v1/credentials/status-lists"),
            ("StatusList:IetfBaseUrl", "https://n1.sorcha.dev/api/v1/credentials/ietf-status-lists"));

        var urls = StatusListUrls.Resolve(config, environmentName: "Production");

        urls.BaseUrl.Should().Be("https://n1.sorcha.dev/api/v1/credentials/status-lists");
        urls.IetfBaseUrl.Should().Be("https://n1.sorcha.dev/api/v1/credentials/ietf-status-lists");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Resolve_UnsetOutsideProduction_DefaultsToDeploymentLocalHttps(string env)
    {
        var urls = StatusListUrls.Resolve(Config(), env);

        // https (the Wallet Service refuses to sign a non-HTTPS status URL) and
        // deployment-local — never the unroutable sorcha.example placeholder.
        urls.BaseUrl.Should().Be("https://localhost/api/v1/credentials/status-lists");
        urls.IetfBaseUrl.Should().Be("https://localhost/api/v1/credentials/ietf-status-lists");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Resolve_UnsetInProductionOrStaging_FailsClosed(string env)
    {
        var act = () => StatusListUrls.Resolve(Config(), env);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*StatusList:BaseUrl*", "an unset base URL must refuse startup rather than sign dead URLs into credentials");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Resolve_PlaceholderInProductionOrStaging_FailsClosed(string env)
    {
        var config = Config(
            ("StatusList:BaseUrl", "https://sorcha.example/api/v1/credentials/status-lists"),
            ("StatusList:IetfBaseUrl", "https://sorcha.example/api/v1/credentials/ietf-status-lists"));

        var act = () => StatusListUrls.Resolve(config, env);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*sorcha.example*", "the reserved placeholder host can never serve a status list");
    }

    [Fact]
    public void Resolve_PlaceholderIetfOnly_StillFailsClosedInProduction()
    {
        // The shipped appsettings.json used to carry the placeholder on IetfBaseUrl only —
        // a partial misconfiguration must be caught exactly like a full one.
        var config = Config(
            ("StatusList:BaseUrl", "https://n1.sorcha.dev/api/v1/credentials/status-lists"),
            ("StatusList:IetfBaseUrl", "https://sorcha.example/api/v1/credentials/ietf-status-lists"));

        var act = () => StatusListUrls.Resolve(config, "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*sorcha.example*");
    }

    [Fact]
    public void Resolve_PlaceholderInDevelopment_IsRejectedToo()
    {
        // A committed placeholder that "works" in dev is how #1447 stayed hidden — the
        // resolver treats sorcha.example as invalid in EVERY environment.
        var config = Config(
            ("StatusList:BaseUrl", "https://sorcha.example/api/v1/credentials/status-lists"));

        var act = () => StatusListUrls.Resolve(config, "Development");

        act.Should().Throw<InvalidOperationException>().WithMessage("*sorcha.example*");
    }

    [Fact]
    public void Resolve_TrailingSlash_IsTrimmed()
    {
        var config = Config(
            ("StatusList:BaseUrl", "https://n1.sorcha.dev/api/v1/credentials/status-lists/"),
            ("StatusList:IetfBaseUrl", "https://n1.sorcha.dev/api/v1/credentials/ietf-status-lists/"));

        var urls = StatusListUrls.Resolve(config, "Production");

        urls.BaseUrl.Should().NotEndWith("/");
        urls.IetfBaseUrl.Should().NotEndWith("/");
    }
}
