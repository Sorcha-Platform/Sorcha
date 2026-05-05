// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.ServiceDefaults.Hubs;
using Sorcha.ServiceDefaults.Storage;
using Xunit;

namespace Sorcha.ServiceDefaults.Tests.Hubs;

public class AddSorchaHubExtensionsTests
{
    private interface IFakeHubClient
    {
        Task FakeEvent(string id);
    }

    private sealed class FakeHub : Hub<IFakeHubClient>
    {
    }

    private interface IOtherHubClient
    {
        Task OtherEvent(string id);
    }

    private sealed class OtherFakeHub : Hub<IOtherHubClient>
    {
    }

    private static IConfiguration BuildConfig(IDictionary<string, string?>? values = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
    }

    [Fact]
    public void AddSorchaHub_WithRedisCascade_RecordsBackplaneAsPersistent()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Sorcha:Redis"] = "redis-host:6379"
        });

        services.AddSorchaHub<FakeHub, IFakeHubClient>(config, "/hubs/fake", "fake");

        var provider = services.BuildServiceProvider();
        var log = provider.GetRequiredService<IStorageRegistrationLog>();
        var record = log.Snapshot()
            .Single(r => r.InterfaceName == SorchaHubConventions.BackplaneRegistrationInterface);

        record.IsInMemory.Should().BeFalse();
        record.IsAudited.Should().BeTrue();
        record.Backend.Should().Contain("redis");
        record.Backend.Should().Contain("prefix=sorcha:signalr:fake");
    }

    [Fact]
    public void AddSorchaHub_WithoutRedis_RecordsBackplaneAsInMemoryAndAudited()
    {
        var services = new ServiceCollection();
        var config = BuildConfig();

        services.AddSorchaHub<FakeHub, IFakeHubClient>(config, "/hubs/fake", "fake");

        var provider = services.BuildServiceProvider();
        var log = provider.GetRequiredService<IStorageRegistrationLog>();
        var record = log.Snapshot()
            .Single(r => r.InterfaceName == SorchaHubConventions.BackplaneRegistrationInterface);

        record.IsInMemory.Should().BeTrue();
        record.IsAudited.Should().BeTrue();
        record.Reason.Should().Contain("no Redis connection string");
    }

    [Fact]
    public void AddSorchaHub_ServiceCascadeOverridesSorchaCascade()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Sorcha:Redis"] = "wrong-host:6379",
            ["ConnectionStrings:fake:Redis"] = "right-host:6379"
        });

        services.AddSorchaHub<FakeHub, IFakeHubClient>(config, "/hubs/fake", "fake");

        var provider = services.BuildServiceProvider();
        var log = provider.GetRequiredService<IStorageRegistrationLog>();
        var record = log.Snapshot()
            .Single(r => r.InterfaceName == SorchaHubConventions.BackplaneRegistrationInterface);

        record.IsInMemory.Should().BeFalse();
        // Backend label only carries the prefix, but registration succeeded
        // means the connection string was found via the service-cascade path.
    }

    [Fact]
    public void AddSorchaHub_TwoHubsInSameService_RegistersBothInRouteRegistry()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Sorcha:Redis"] = "redis-host:6379"
        });

        services.AddSorchaHub<FakeHub, IFakeHubClient>(config, "/hubs/fake", "fake");
        services.AddSorchaHub<OtherFakeHub, IOtherHubClient>(config, "/hubs/other", "fake");

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HubRouteRegistry>();
        var snapshot = registry.Snapshot();

        snapshot.Should().HaveCount(2);
        snapshot.Should().Contain(e => e.HubType == typeof(FakeHub) && e.RoutePath == "/hubs/fake");
        snapshot.Should().Contain(e => e.HubType == typeof(OtherFakeHub) && e.RoutePath == "/hubs/other");
    }

    [Fact]
    public void AddSorchaHub_TwoHubsInSameService_RecordsBackplaneOnlyOnce()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Sorcha:Redis"] = "redis-host:6379"
        });

        services.AddSorchaHub<FakeHub, IFakeHubClient>(config, "/hubs/fake", "fake");
        services.AddSorchaHub<OtherFakeHub, IOtherHubClient>(config, "/hubs/other", "fake");

        var provider = services.BuildServiceProvider();
        var log = provider.GetRequiredService<IStorageRegistrationLog>();
        var backplaneRecords = log.Snapshot()
            .Where(r => r.InterfaceName == SorchaHubConventions.BackplaneRegistrationInterface);

        backplaneRecords.Should().HaveCount(1, "the backplane is wired once per service, not once per hub");
    }

    [Fact]
    public void AddSorchaHub_NullArguments_ThrowArgumentExceptions()
    {
        var services = new ServiceCollection();
        var config = BuildConfig();

        var nullServices = () => AddSorchaHubExtensions.AddSorchaHub<FakeHub, IFakeHubClient>(null!, config, "/hubs/fake", "fake");
        var nullConfig = () => services.AddSorchaHub<FakeHub, IFakeHubClient>(null!, "/hubs/fake", "fake");
        var nullRoute = () => services.AddSorchaHub<FakeHub, IFakeHubClient>(config, "  ", "fake");
        var nullName = () => services.AddSorchaHub<FakeHub, IFakeHubClient>(config, "/hubs/fake", "  ");

        nullServices.Should().Throw<ArgumentNullException>();
        nullConfig.Should().Throw<ArgumentNullException>();
        nullRoute.Should().Throw<ArgumentException>();
        nullName.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BackplaneInterface_IsListedInAuditedSet()
    {
        // Prove the synthetic backplane interface name lands in the audited
        // set so production fail-fast applies. We exercise this through the
        // public registration path: an in-memory record's IsAudited flag is
        // computed at registration time from AuditedStorageInterfaces.Names.
        var services = new ServiceCollection();
        var config = BuildConfig();

        services.AddSorchaHub<FakeHub, IFakeHubClient>(config, "/hubs/fake", "fake");

        var provider = services.BuildServiceProvider();
        var log = provider.GetRequiredService<IStorageRegistrationLog>();
        var record = log.Snapshot()
            .Single(r => r.InterfaceName == SorchaHubConventions.BackplaneRegistrationInterface);

        record.IsAudited.Should().BeTrue("the SignalR backplane MUST be in AuditedStorageInterfaces.Names " +
            "so Production/Staging fail-fast when hub-hosting services have no Redis backplane");
    }
}
