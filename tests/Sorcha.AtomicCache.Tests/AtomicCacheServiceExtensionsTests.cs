// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.AtomicCache;
using Sorcha.AtomicCache.Extensions;
using Sorcha.ServiceDefaults.Storage;

namespace Sorcha.AtomicCache.Tests;

/// <summary>
/// Tests for <see cref="AtomicCacheServiceExtensions.AddAtomicDistributedCache"/>.
/// Branches on SorchaConnections cascade resolution + records the choice in
/// the storage registration log so audited-interface fail-fast fires correctly.
/// </summary>
public class AtomicCacheServiceExtensionsTests
{
    private static IConfiguration Config(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();

    [Fact]
    public void NoConnectionString_RegistersInMemory_AndLogsAsInMemory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStorageRegistration();

        services.AddAtomicDistributedCache(Config(), "TestService");

        var snapshot = services.GetStorageRegistrationLog().Snapshot();
        snapshot.Should().HaveCount(1);
        snapshot[0].InterfaceName.Should().Be(typeof(IAtomicDistributedCache).FullName);
        snapshot[0].IsInMemory.Should().BeTrue();
        snapshot[0].IsAudited.Should().BeTrue();
        snapshot[0].ImplementationName.Should().EndWith("InMemoryAtomicDistributedCache");
        snapshot[0].Reason.Should().Contain("ConnectionStrings:TestService:Redis");
        snapshot[0].Reason.Should().Contain("ConnectionStrings:Sorcha:Redis");
    }

    [Fact]
    public void ServiceOverrideConnectionString_RegistersPersistent_WithRedisBackend()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStorageRegistration();

        var config = Config(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Haip:Redis"] = "haip-redis:6379"
        });
        services.AddAtomicDistributedCache(config, "Haip");

        var snapshot = services.GetStorageRegistrationLog().Snapshot();
        snapshot.Should().HaveCount(1);
        snapshot[0].IsInMemory.Should().BeFalse();
        snapshot[0].IsAudited.Should().BeTrue();
        snapshot[0].Backend.Should().Be("redis");
        snapshot[0].ImplementationName.Should().EndWith("RedisAtomicDistributedCache");
    }

    [Fact]
    public void SorchaDefaultConnectionString_RegistersPersistent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStorageRegistration();

        var config = Config(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Sorcha:Redis"] = "default-redis:6379"
        });
        services.AddAtomicDistributedCache(config, "Haip");

        var snapshot = services.GetStorageRegistrationLog().Snapshot();
        snapshot.Should().HaveCount(1);
        snapshot[0].IsInMemory.Should().BeFalse();
        snapshot[0].Backend.Should().Be("redis");
    }

    [Fact]
    public void NoConfig_ResolvesInMemoryImplementationFromContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStorageRegistration();
        services.AddAtomicDistributedCache(Config(), "TestService");

        using var sp = services.BuildServiceProvider();
        var cache = sp.GetRequiredService<IAtomicDistributedCache>();

        cache.Should().BeOfType<InMemoryAtomicDistributedCache>();
    }

    [Fact]
    public void CalledTwice_IsIdempotent()
    {
        // Multiple HAIP consumers (NonceStore, PreAuthCodeStore,
        // PresentationRequestStore) typically each call AddAtomicDistributedCache.
        // The extension's idempotency sentinel ensures only the first call
        // registers the implementation + records the storage-log entry; subsequent
        // calls are no-ops.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStorageRegistration();

        services.AddAtomicDistributedCache(Config(), "TestService");
        services.AddAtomicDistributedCache(Config(), "TestService");

        services.Where(d => d.ServiceType == typeof(IAtomicDistributedCache))
            .Should().HaveCount(1);
        services.GetStorageRegistrationLog().Snapshot().Should().HaveCount(1);
    }

    [Fact]
    public void NullServices_Throws()
    {
        IServiceCollection? services = null;
        var act = () => services!.AddAtomicDistributedCache(Config(), "TestService");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NullConfiguration_Throws()
    {
        var services = new ServiceCollection();
        var act = () => services.AddAtomicDistributedCache(configuration: null!, "TestService");
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankServiceName_Throws(string? serviceName)
    {
        var services = new ServiceCollection();
        var act = () => services.AddAtomicDistributedCache(Config(), serviceName!);
        act.Should().Throw<ArgumentException>();
    }
}
