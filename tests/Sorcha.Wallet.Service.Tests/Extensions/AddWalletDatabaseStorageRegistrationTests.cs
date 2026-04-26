// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceDefaults.Storage;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.Extensions;

namespace Sorcha.Wallet.Service.Tests.Extensions;

/// <summary>
/// Tests that <see cref="WalletServiceExtensions.AddWalletDatabase"/> registers
/// itself with the Sorcha.ServiceDefaults storage registration log so that:
/// (a) operators see <c>IWalletRepository → ...</c> in startup logs, the
///     <c>storage-providers</c> health check, and the OTel metrics; and
/// (b) Production / Staging deployments fail-fast when no Postgres
///     connection string resolves.
/// </summary>
public class AddWalletDatabaseStorageRegistrationTests
{
    private static readonly string AuditedInterface = typeof(IWalletRepository).FullName!;

    private static IConfiguration Config(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();

    private static IServiceCollection NewServicesWithStorageRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStorageRegistration();
        return services;
    }

    [Fact]
    public void NoConnectionString_RegistersInMemory()
    {
        var services = NewServicesWithStorageRegistration();

        services.AddWalletDatabase(Config());

        var snapshot = services.GetStorageRegistrationLog().Snapshot();
        snapshot.Should().HaveCount(1);
        snapshot[0].InterfaceName.Should().Be(AuditedInterface);
        snapshot[0].IsInMemory.Should().BeTrue();
        snapshot[0].IsAudited.Should().BeTrue();
        snapshot[0].ImplementationName.Should().EndWith("InMemoryWalletRepository");
        snapshot[0].Reason.Should().Contain("ConnectionStrings:Wallet:Postgres");
        snapshot[0].Reason.Should().Contain("ConnectionStrings:Sorcha:Postgres");
    }

    [Fact]
    public void WalletConnectionString_RegistersPersistent()
    {
        var services = NewServicesWithStorageRegistration();
        var config = Config(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Wallet:Postgres"] = "Host=wallet-pg;Username=wallet"
        });

        services.AddWalletDatabase(config);

        var snapshot = services.GetStorageRegistrationLog().Snapshot();
        snapshot.Should().HaveCount(1);
        snapshot[0].IsInMemory.Should().BeFalse();
        snapshot[0].IsAudited.Should().BeTrue();
        snapshot[0].Backend.Should().Be("postgres");
        snapshot[0].ImplementationName.Should().EndWith("EfCoreWalletRepository");
    }

    [Fact]
    public void SorchaConnectionString_RegistersPersistent()
    {
        var services = NewServicesWithStorageRegistration();
        var config = Config(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Sorcha:Postgres"] = "Host=default-pg;Username=sorcha"
        });

        services.AddWalletDatabase(config);

        var snapshot = services.GetStorageRegistrationLog().Snapshot();
        snapshot.Should().HaveCount(1);
        snapshot[0].IsInMemory.Should().BeFalse();
        snapshot[0].Backend.Should().Be("postgres");
    }

    [Fact]
    public async Task Production_NoConnectionString_StorageEnforcementThrows()
    {
        // The full Wallet startup is too expensive to spin up here; this verifies the
        // contract: AddWalletDatabase records IWalletRepository as in-memory, and the
        // standard StorageEnforcementHostedService refuses to start in Production.
        var services = NewServicesWithStorageRegistration();
        services.AddWalletDatabase(Config());

        var hostedService = new StorageEnforcementHostedService(
            services.GetStorageRegistrationLog(),
            FakeEnv(Environments.Production),
            Config(),
            NullLogger<StorageEnforcementHostedService>.Instance);

        var assertion = await hostedService.Invoking(s => s.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Message.Should().Contain(AuditedInterface);
    }

    [Fact]
    public async Task Staging_NoConnectionString_StorageEnforcementThrows()
    {
        // Staging is treated identically to Production for the audited-interface
        // fail-fast. Mirrors the Production test to pin that contract.
        var services = NewServicesWithStorageRegistration();
        services.AddWalletDatabase(Config());

        var hostedService = new StorageEnforcementHostedService(
            services.GetStorageRegistrationLog(),
            FakeEnv(Environments.Staging),
            Config(),
            NullLogger<StorageEnforcementHostedService>.Instance);

        var assertion = await hostedService.Invoking(s => s.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Message.Should().Contain(AuditedInterface);
    }

    [Fact]
    public async Task Production_WithBypassFlag_DoesNotThrow()
    {
        var services = NewServicesWithStorageRegistration();
        services.AddWalletDatabase(Config());

        var hostedService = new StorageEnforcementHostedService(
            services.GetStorageRegistrationLog(),
            FakeEnv(Environments.Production),
            Config(new Dictionary<string, string?>
            {
                [StorageRegistrationEnforcement.AllowInMemoryConfigKey] = "true"
            }),
            NullLogger<StorageEnforcementHostedService>.Instance);

        await hostedService.Invoking(s => s.StartAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task Development_NoConnectionString_DoesNotThrow()
    {
        var services = NewServicesWithStorageRegistration();
        services.AddWalletDatabase(Config());

        var hostedService = new StorageEnforcementHostedService(
            services.GetStorageRegistrationLog(),
            FakeEnv(Environments.Development),
            Config(),
            NullLogger<StorageEnforcementHostedService>.Instance);

        await hostedService.Invoking(s => s.StartAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task Production_WithConnectionString_DoesNotThrow()
    {
        var services = NewServicesWithStorageRegistration();
        var config = Config(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Wallet:Postgres"] = "Host=wallet-pg;Username=wallet"
        });
        services.AddWalletDatabase(config);

        var hostedService = new StorageEnforcementHostedService(
            services.GetStorageRegistrationLog(),
            FakeEnv(Environments.Production),
            Config(),
            NullLogger<StorageEnforcementHostedService>.Instance);

        await hostedService.Invoking(s => s.StartAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    private static IHostEnvironment FakeEnv(string environment) => new TestHostEnvironment
    {
        EnvironmentName = environment,
        ApplicationName = "Sorcha.Wallet.Service",
    };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Sorcha.Wallet.Service";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
