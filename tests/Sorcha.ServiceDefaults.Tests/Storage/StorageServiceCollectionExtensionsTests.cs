// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Sorcha.ServiceDefaults.Storage;

namespace Sorcha.ServiceDefaults.Tests.Storage;

/// <summary>
/// Tests for <see cref="StorageServiceCollectionExtensions.AddStorageRegistration"/>
/// — the DI wiring helper.
/// </summary>
public class StorageServiceCollectionExtensionsTests
{
    [Fact]
    public void AddStorageRegistration_RegistersLogSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddStorageRegistration();

        var descriptors = services.Where(d => d.ServiceType == typeof(IStorageRegistrationLog)).ToArray();
        descriptors.Should().HaveCount(1);
        descriptors[0].Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddStorageRegistration_RegistersMetrics()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddStorageRegistration();

        services.Should().Contain(d => d.ServiceType == typeof(StorageRegistrationMetrics));
    }

    [Fact]
    public void AddStorageRegistration_RegistersHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddStorageRegistration();

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>().Value;

        options.Registrations.Should().ContainSingle(r => r.Name == StorageProvidersHealthCheck.Name);
    }

    [Fact]
    public void AddStorageRegistration_RegistersHostedServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddStorageRegistration();

        var hostedDescriptors = services.Where(d => d.ServiceType == typeof(IHostedService)).ToArray();
        // Expect one activator + one enforcement service.
        hostedDescriptors.Should().HaveCountGreaterThanOrEqualTo(2);
        hostedDescriptors.Should().Contain(d => d.ImplementationType == typeof(StorageEnforcementHostedService));
    }

    [Fact]
    public void AddStorageRegistration_CalledTwice_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddStorageRegistration();
        services.AddStorageRegistration();

        // Log: exactly one registration.
        services.Where(d => d.ServiceType == typeof(IStorageRegistrationLog))
            .Should().HaveCount(1);

        // Metrics: exactly one.
        services.Where(d => d.ServiceType == typeof(StorageRegistrationMetrics))
            .Should().HaveCount(1);

        // Health check: exactly one HealthCheckRegistration named "storage-providers".
        // (AddCheck<T> appends to HealthCheckServiceOptions.Registrations — duplicate
        // calls would register two checks with the same name, which crashes at runtime
        // when HealthCheckService validates name uniqueness.)
        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>().Value;
        options.Registrations.Where(r => r.Name == StorageProvidersHealthCheck.Name)
            .Should().HaveCount(1);

        // Enforcement hosted service: exactly one.
        services.Where(d => d.ServiceType == typeof(IHostedService)
                            && d.ImplementationType == typeof(StorageEnforcementHostedService))
            .Should().HaveCount(1);
    }

    [Fact]
    public void AddStorageRegistration_LogResolvesAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStorageRegistration();

        using var sp = services.BuildServiceProvider();

        var log1 = sp.GetRequiredService<IStorageRegistrationLog>();
        var log2 = sp.GetRequiredService<IStorageRegistrationLog>();
        log1.Should().BeSameAs(log2);
    }

    [Fact]
    public void AddStorageRegistration_NullServices_Throws()
    {
        IServiceCollection? services = null;
        var act = () => services!.AddStorageRegistration();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetStorageRegistrationLog_AfterAddStorageRegistration_ReturnsInstance()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStorageRegistration();

        var log = services.GetStorageRegistrationLog();

        log.Should().NotBeNull();
        log.Should().BeOfType<StorageRegistrationLog>();
    }

    [Fact]
    public void GetStorageRegistrationLog_ReturnsSameInstanceFromDIContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStorageRegistration();

        var fromExtension = services.GetStorageRegistrationLog();

        using var sp = services.BuildServiceProvider();
        var fromContainer = sp.GetRequiredService<IStorageRegistrationLog>();

        fromContainer.Should().BeSameAs(fromExtension);
    }

    [Fact]
    public void GetStorageRegistrationLog_RegistrationsArePreservedThroughBuild()
    {
        // Service-side AddXxxDatabase calls register entries on the log instance
        // BEFORE the container is built. Those entries must survive into the resolved
        // singleton so EnforcementHostedService and the metrics callbacks see them.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStorageRegistration();

        var log = services.GetStorageRegistrationLog();
        log.RegisterPersistent("Sorcha.Wallet.Core.Repositories.IWalletRepository", "EfCoreWalletRepository", "postgres");

        using var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<IStorageRegistrationLog>();

        resolved.Snapshot().Should().HaveCount(1);
        resolved.Snapshot()[0].InterfaceName.Should().Be("Sorcha.Wallet.Core.Repositories.IWalletRepository");
    }

    [Fact]
    public void GetStorageRegistrationLog_BeforeAddStorageRegistration_AutoRegistersAndReturns()
    {
        // Defensive: GetStorageRegistrationLog calls AddStorageRegistration if it hasn't run yet.
        // This keeps service-extension methods unit-testable in isolation. AddStorageRegistration
        // is itself idempotent so subsequent AddServiceDefaults() in the same test doesn't double-wire.
        var services = new ServiceCollection();

        var log = services.GetStorageRegistrationLog();

        log.Should().NotBeNull();
        services.Where(d => d.ServiceType == typeof(IStorageRegistrationLog)).Should().HaveCount(1);
    }

    [Fact]
    public void GetStorageRegistrationLog_NullServices_Throws()
    {
        IServiceCollection? services = null;
        var act = () => services!.GetStorageRegistrationLog();
        act.Should().Throw<ArgumentNullException>();
    }
}
