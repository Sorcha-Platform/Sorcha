// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Data;

namespace Sorcha.Tenant.Service.Tests.Data;

/// <summary>
/// Covers <see cref="AdminPasswordResolver.Resolve"/> — the per-deploy admin-password selection
/// extracted from <see cref="DatabaseInitializer.SeedAdminUserAsync"/> for issue #1409. A
/// Production/Staging node brought up without <c>Seed:AdminPassword</c> must never seed the
/// well-known <c>Dev_Pass_2025!</c> literal — it must refuse to start instead.
/// </summary>
public sealed class AdminPasswordResolverTests
{
    private const string ConfiguredPassword = "a-per-deploy-admin-password";

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    [InlineData(null)]
    public void Resolve_ConfiguredPasswordPresent_IsUsedRegardlessOfEnvironment(string? environment)
    {
        var (password, source) = AdminPasswordResolver.Resolve(ConfiguredPassword, environment);

        password.Should().Be(ConfiguredPassword);
        source.Should().Be(AdminPasswordSource.Configured);
    }

    [Fact]
    public void Resolve_ConfiguredPasswordIsWhitespace_FallsThroughAsAbsent()
    {
        var (password, source) = AdminPasswordResolver.Resolve("   ", "Development");

        password.Should().Be(DatabaseInitializer.DefaultAdminPassword);
        source.Should().Be(AdminPasswordSource.DevDefault);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Resolve_ProductionOrStagingNoConfiguredPassword_ThrowsWithClearMessage(string environment)
    {
        var act = () => AdminPasswordResolver.Resolve(configuredValue: null, environment);

        act.Should().Throw<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("Seed:AdminPassword", StringComparison.Ordinal)
                      && ex.Message.Contains("Seed__AdminPassword", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("SomethingElse")]
    [InlineData(null)]
    public void Resolve_NonProdEnvironmentNoConfiguredPassword_UsesDevDefault(string? environment)
    {
        var (password, source) = AdminPasswordResolver.Resolve(configuredValue: null, environment);

        password.Should().Be(DatabaseInitializer.DefaultAdminPassword);
        source.Should().Be(AdminPasswordSource.DevDefault);
    }
}
