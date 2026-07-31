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
    private static readonly string HolderLookupInterface =
        typeof(Sorcha.Wallet.Service.Services.Interfaces.IHolderAddressLookup).FullName!;

    /// <summary>
    /// Finds a registration by interface name. Both registrations this method makes are driven by
    /// the same connection-string branch, so asserting on positional index would break every time
    /// the branch gains a member — and would silently assert against the wrong interface rather
    /// than fail loudly.
    /// </summary>
    private static StorageRegistrationRecord Record(IServiceCollection services, string interfaceName)
    {
        var snapshot = services.GetStorageRegistrationLog().Snapshot();
        return snapshot.Should().ContainSingle(r => r.InterfaceName == interfaceName).Subject;
    }

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

        var repository = Record(services, AuditedInterface);
        repository.IsInMemory.Should().BeTrue();
        repository.IsAudited.Should().BeTrue();
        repository.ImplementationName.Should().EndWith("InMemoryWalletRepository");
        repository.Reason.Should().Contain("ConnectionStrings:Wallet:Postgres");
        repository.Reason.Should().Contain("ConnectionStrings:Sorcha:Postgres");
    }

    [Fact]
    public void NoConnectionString_RegistersInMemoryHolderAddressLookup()
    {
        // IHolderAddressLookup used to be an unconditional AddScoped to the EF Core implementation
        // in Program.cs, which cannot be activated without a WalletDbContext — so the supported
        // no-Postgres path 500'd on every endpoint that touched it, including POST /api/v1/wallets.
        var services = NewServicesWithStorageRegistration();

        services.AddWalletDatabase(Config());

        var lookup = Record(services, HolderLookupInterface);
        lookup.IsInMemory.Should().BeTrue();
        lookup.ImplementationName.Should().EndWith("InMemoryHolderAddressLookup");
        lookup.Reason.Should().Contain("ConnectionStrings:Wallet:Postgres");

        // Deliberately NOT audited: it shares IWalletRepository's connection-string branch, so it
        // can never be the only audited interface on an in-memory backend — the repository's
        // fail-fast already covers exactly the same condition. Listing it would add a second
        // identical trigger, not a second safeguard.
        lookup.IsAudited.Should().BeFalse();
    }

    [Fact]
    public void ConnectionString_RegistersEfCoreHolderAddressLookup()
    {
        var services = NewServicesWithStorageRegistration();
        var config = Config(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Wallet:Postgres"] = "Host=wallet-pg;Username=wallet"
        });

        services.AddWalletDatabase(config);

        var lookup = Record(services, HolderLookupInterface);
        lookup.IsInMemory.Should().BeFalse();
        lookup.Backend.Should().Be("postgres");
        lookup.ImplementationName.Should().EndWith("EfCoreHolderAddressLookup");
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

        var repository = Record(services, AuditedInterface);
        repository.IsInMemory.Should().BeFalse();
        repository.IsAudited.Should().BeTrue();
        repository.Backend.Should().Be("postgres");
        repository.ImplementationName.Should().EndWith("EfCoreWalletRepository");
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

        var repository = Record(services, AuditedInterface);
        repository.IsInMemory.Should().BeFalse();
        repository.Backend.Should().Be("postgres");
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
