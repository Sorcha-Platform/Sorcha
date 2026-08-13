// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Data;

namespace Sorcha.Tenant.Service.Tests.Data;

/// <summary>
/// Covers <see cref="ServicePrincipalSecretResolver.Resolve"/> — the per-deploy service-secret
/// selection extracted from <see cref="DatabaseInitializer.SeedServicePrincipalsAsync"/> for issue
/// #1412. Client (docker-compose <c>ServiceAuth__ClientSecret</c>) and server (this resolver, fed by
/// <c>Seed:ServicePrincipals:{clientId}</c>) must agree on a value, or inter-service auth silently
/// breaks — the bug this resolver exists to close.
/// </summary>
public sealed class ServicePrincipalSecretResolverTests
{
    private const string ClientId = "service-blueprint";
    private const string DevSecret = "blueprint-service-secret";
    private const string ConfiguredSecret = "a-generated-per-deploy-secret-value";

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    [InlineData(null)]
    public void Resolve_ConfiguredSecretPresent_IsUsedRegardlessOfEnvironment(string? environment)
    {
        var (secret, source) = ServicePrincipalSecretResolver.Resolve(
            ClientId, DevSecret, ConfiguredSecret, environment);

        secret.Should().Be(ConfiguredSecret);
        source.Should().Be(ServicePrincipalSecretSource.Configured);
    }

    [Fact]
    public void Resolve_ConfiguredSecretIsWhitespace_FallsThroughAsAbsent()
    {
        var (secret, source) = ServicePrincipalSecretResolver.Resolve(
            ClientId, DevSecret, "   ", "Development");

        secret.Should().Be(DevSecret);
        source.Should().Be(ServicePrincipalSecretSource.DevLiteral);
    }

    [Fact]
    public void Resolve_DevelopmentNoConfiguredSecret_FallsBackToDevLiteral()
    {
        var (secret, source) = ServicePrincipalSecretResolver.Resolve(
            ClientId, DevSecret, configuredValue: null, "Development");

        secret.Should().Be(DevSecret);
        source.Should().Be(ServicePrincipalSecretSource.DevLiteral);
    }

    [Fact]
    public void Resolve_DevelopmentIsCaseInsensitive()
    {
        var (secret, source) = ServicePrincipalSecretResolver.Resolve(
            ClientId, DevSecret, configuredValue: null, "development");

        secret.Should().Be(DevSecret);
        source.Should().Be(ServicePrincipalSecretSource.DevLiteral);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Resolve_ProductionOrStagingNoConfiguredSecret_ThrowsWithClearMessage(string environment)
    {
        var act = () => ServicePrincipalSecretResolver.Resolve(
            ClientId, DevSecret, configuredValue: null, environment);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{ClientId}*")
            .Where(ex => ex.Message.Contains($"Seed:ServicePrincipals:{ClientId}", StringComparison.Ordinal)
                      && ex.Message.Contains($"Seed__ServicePrincipals__{ClientId}", StringComparison.Ordinal));
    }

    [Fact]
    public void Resolve_ProductionNoConfiguredSecretAndNoDevLiteral_StillThrows()
    {
        var act = () => ServicePrincipalSecretResolver.Resolve(
            ClientId, devSecret: null, configuredValue: null, "Production");

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("SomethingElse")]
    [InlineData(null)]
    public void Resolve_OtherEnvironmentNoConfiguredSecret_GeneratesFreshNonLiteralSecret(string? environment)
    {
        var (secret, source) = ServicePrincipalSecretResolver.Resolve(
            ClientId, DevSecret, configuredValue: null, environment);

        secret.Should().NotBeNullOrWhiteSpace();
        secret.Should().NotBe(DevSecret);
        secret.Should().NotBe(ConfiguredSecret);
        source.Should().Be(ServicePrincipalSecretSource.Generated);
    }

    [Fact]
    public void Resolve_TestingEnvironment_GeneratesADifferentSecretEachCall()
    {
        var (first, _) = ServicePrincipalSecretResolver.Resolve(
            ClientId, DevSecret, configuredValue: null, "Testing");
        var (second, _) = ServicePrincipalSecretResolver.Resolve(
            ClientId, DevSecret, configuredValue: null, "Testing");

        first.Should().NotBe(second);
    }

    [Fact]
    public void Resolve_NonDevelopmentEnvironmentWithDevLiteral_DoesNotUseTheDevLiteral()
    {
        // A dev-literal fallback must never leak outside Development, even when one is supplied.
        var (secret, source) = ServicePrincipalSecretResolver.Resolve(
            ClientId, DevSecret, configuredValue: null, "Testing");

        secret.Should().NotBe(DevSecret);
        source.Should().Be(ServicePrincipalSecretSource.Generated);
    }
}
