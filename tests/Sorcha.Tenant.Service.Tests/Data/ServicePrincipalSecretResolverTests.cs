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
/// breaks — the bug this resolver exists to close. The committed dev-literal fallback is gone:
/// docker-compose.yml now guards both sides with <c>${VAR:?...}</c>, so a real deployment always has
/// the configured value; only the Generated branch (below) remains reachable, and only for
/// self-contained tests.
/// </summary>
public sealed class ServicePrincipalSecretResolverTests
{
    private const string ClientId = "service-blueprint";
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
            ClientId, ConfiguredSecret, environment);

        secret.Should().Be(ConfiguredSecret);
        source.Should().Be(ServicePrincipalSecretSource.Configured);
    }

    [Fact]
    public void Resolve_ConfiguredSecretIsWhitespace_FallsThroughAsAbsent()
    {
        var (secret, source) = ServicePrincipalSecretResolver.Resolve(
            ClientId, "   ", "Development");

        secret.Should().NotBeNullOrWhiteSpace();
        secret.Should().NotBe(ConfiguredSecret);
        source.Should().Be(ServicePrincipalSecretSource.Generated);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Resolve_ProductionOrStagingNoConfiguredSecret_ThrowsWithClearMessage(string environment)
    {
        var act = () => ServicePrincipalSecretResolver.Resolve(
            ClientId, configuredValue: null, environment);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{ClientId}*")
            .Where(ex => ex.Message.Contains($"Seed:ServicePrincipals:{ClientId}", StringComparison.Ordinal)
                      && ex.Message.Contains($"Seed__ServicePrincipals__{ClientId}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("SomethingElse")]
    [InlineData(null)]
    public void Resolve_NonProdEnvironmentNoConfiguredSecret_GeneratesFreshSecret(string? environment)
    {
        var (secret, source) = ServicePrincipalSecretResolver.Resolve(
            ClientId, configuredValue: null, environment);

        secret.Should().NotBeNullOrWhiteSpace();
        secret.Should().NotBe(ConfiguredSecret);
        source.Should().Be(ServicePrincipalSecretSource.Generated);
    }

    [Fact]
    public void Resolve_TestingEnvironment_GeneratesADifferentSecretEachCall()
    {
        var (first, _) = ServicePrincipalSecretResolver.Resolve(
            ClientId, configuredValue: null, "Testing");
        var (second, _) = ServicePrincipalSecretResolver.Resolve(
            ClientId, configuredValue: null, "Testing");

        first.Should().NotBe(second);
    }
}
