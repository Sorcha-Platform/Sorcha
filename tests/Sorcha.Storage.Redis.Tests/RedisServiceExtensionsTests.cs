// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Sorcha.Storage.Abstractions;

namespace Sorcha.Storage.Redis.Tests;

public class RedisServiceExtensionsTests
{
    [Fact]
    public void AddRedisCacheStore_WithConfiguration_RegistersServices()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Hot:Provider"] = "Redis",
                ["Storage:Hot:DefaultTtlSeconds"] = "600",
                ["Storage:Hot:Redis:ConnectionString"] = "localhost:6379",
                ["Storage:Hot:Redis:InstanceName"] = "test:"
            })
            .Build();

        services.AddRedisCacheStore(config);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICacheStore));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
        descriptor.ImplementationType.Should().Be(typeof(RedisCacheStore));
    }

    [Fact]
    public void AddRedisCacheStore_WithConfigureAction_RegistersServices()
    {
        var services = new ServiceCollection();

        services.AddRedisCacheStore(config =>
        {
            config.Provider = "Redis";
            config.DefaultTtlSeconds = 300;
            config.Redis = new RedisConfiguration
            {
                ConnectionString = "localhost:6379",
                InstanceName = "unit-test:"
            };
        });

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICacheStore));
        descriptor.Should().NotBeNull();
        descriptor!.ImplementationType.Should().Be(typeof(RedisCacheStore));
    }

    [Fact]
    public void AddRedisCacheStore_WithConfigureAction_BindsOptions()
    {
        var services = new ServiceCollection();

        services.AddRedisCacheStore(config =>
        {
            config.Provider = "Redis";
            config.DefaultTtlSeconds = 300;
            config.Redis = new RedisConfiguration
            {
                ConnectionString = "localhost:6379",
                InstanceName = "opts:"
            };
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HotTierConfiguration>>();

        options.Value.Provider.Should().Be("Redis");
        options.Value.DefaultTtlSeconds.Should().Be(300);
        options.Value.Redis.Should().NotBeNull();
        options.Value.Redis!.InstanceName.Should().Be("opts:");
    }

    [Fact]
    public void AddRedisCacheStore_WithConnectionString_ConfiguresOptions()
    {
        var services = new ServiceCollection();

        services.AddRedisCacheStore("localhost:6379", "myapp:", 120);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HotTierConfiguration>>();

        options.Value.Provider.Should().Be("Redis");
        options.Value.DefaultTtlSeconds.Should().Be(120);
        options.Value.Redis.Should().NotBeNull();
        options.Value.Redis!.ConnectionString.Should().Be("localhost:6379");
        options.Value.Redis!.InstanceName.Should().Be("myapp:");
    }

    [Fact]
    public void AddRedisCacheStore_WithConnectionString_DefaultParameters()
    {
        var services = new ServiceCollection();

        services.AddRedisCacheStore("localhost:6379");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HotTierConfiguration>>();

        options.Value.Redis!.InstanceName.Should().Be("sorcha:");
        options.Value.DefaultTtlSeconds.Should().Be(900);
    }

    [Fact]
    public void AddRedisCacheStore_CalledTwice_DoesNotRegisterDuplicate()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Hot:Redis:ConnectionString"] = "localhost:6379"
            })
            .Build();

        services.AddRedisCacheStore(config);
        services.AddRedisCacheStore(config);

        var descriptors = services.Where(d => d.ServiceType == typeof(ICacheStore)).ToList();
        descriptors.Should().HaveCount(1, "TryAddSingleton should prevent duplicate registration");
    }

    [Fact]
    public void AddRedisCacheStore_ReturnsServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var result = services.AddRedisCacheStore(config);

        result.Should().BeSameAs(services);
    }
}
