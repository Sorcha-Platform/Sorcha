// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Register;

using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Credentials;

/// <summary>
/// The status-list singletons must not capture a scoped dependency.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the first cut of the ledger reconciler took <c>IRegisterServiceClient</c>
/// directly. That client is registered SCOPED, and the reconciler is consumed by the singleton
/// <c>StatusListManager</c> — a captive dependency. Production DI validation rejects it at startup,
/// so blueprint-service crash-looped on n1 the moment it was deployed.
/// </para>
/// <para>
/// <b>Nothing else caught it.</b> The unit tests construct these types with <c>new</c>, which
/// bypasses DI entirely. The integration suite boots the real host and would in principle catch it,
/// but without the compose stack it fails earlier on <c>ServiceAuth:ClientId not configured</c> and
/// never reaches provider validation — verified by re-introducing the captive dependency and
/// watching that suite report the same environmental error either way.
/// </para>
/// <para>
/// So this test validates the graph directly, with the same lifetimes Program.cs uses, and with
/// <c>ValidateScopes</c> + <c>ValidateOnBuild</c> switched on — the two options that turn a captive
/// dependency from a runtime surprise into a build-time failure.
/// </para>
/// </remarks>
public class StatusListDependencyLifetimeTests
{
    [Fact]
    public void TheStatusListGraphBuildsUnderScopeValidation()
    {
        var services = BuildGraphAsProgramDoes();

        var build = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        build.Should().NotThrow(
            "a singleton in the status-list graph must not capture a scoped service — that is what "
            + "took blueprint-service down on n1");
    }

    [Fact]
    public void TheSingletonManagerResolvesAndCanReachTheScopedRegisterClient()
    {
        // Not just "it builds": the reconciler has to actually get a register client at call time,
        // which is the thing the scope factory exists to make possible.
        using var provider = BuildGraphAsProgramDoes().BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        provider.GetRequiredService<IStatusListManager>().Should().NotBeNull();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>().Should().NotBeNull();
    }

    /// <summary>
    /// Mirrors the lifetimes Program.cs registers for this subgraph. The register client is SCOPED
    /// there (<c>HttpServiceCollectionExtensions</c>), which is the constraint under test.
    /// </summary>
    private static ServiceCollection BuildGraphAsProgramDoes()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IDistributedCache>(new Mock<IDistributedCache>().Object);
        services.AddScoped(_ => new Mock<IRegisterServiceClient>().Object);
        services.AddSingleton<IStatusListStore, InMemoryStatusListStore>();
        services.AddSingleton<StatusListLedgerReconciler>();
        services.AddSingleton(new Sorcha.Blueprint.Service.Configuration.StatusListUrls.Resolved(
            "https://example.test/api/v1/credentials/status-lists",
            "https://example.test/api/v1/credentials/ietf-status-lists"));
        services.AddSingleton<IStatusListManager, StatusListManager>();

        return services;
    }
}
