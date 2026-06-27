// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.UI.Components.User.Extensions;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.Verifier.Engine;
using Xunit;

namespace Sorcha.UI.Core.Tests.Verification;

/// <summary>
/// DI resolution tests for the shared verify seams registered by
/// <c>AddSorchaUserComponents</c> (Feature 163, US4, SC-002, R-006).
/// Proves all three seams resolve to their concrete defaults from a single extension call,
/// and that a host-registered override wins over the defaults.
/// </summary>
public class SharedVerifyRegistrationTests
{
    private static IServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(config);
        configure?.Invoke(services);
        services.AddSorchaUserComponents(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddSorchaUserComponents_ResolvesPresetCatalogueAsDefaultImpl()
    {
        // US4 scenario 1 — catalogue resolves to the config-driven default.
        var sp = BuildProvider();

        sp.GetRequiredService<IVerificationPresetCatalogue>()
            .Should().BeOfType<DefaultPresetCatalogue>();
    }

    [Fact]
    public void AddSorchaUserComponents_ResolvesTransportAsNotConfiguredStub()
    {
        // US4 scenario 2 — transport resolves to the not-configured stub.
        var sp = BuildProvider();

        sp.GetRequiredService<IVerificationTransport>()
            .Should().BeOfType<NotConfiguredVerificationTransport>();
    }

    [Fact]
    public void AddSorchaUserComponents_ResolvesRegisterAnchorClientAsHttpImpl()
    {
        // US4 scenario 2 — anchor client resolves to the HTTP implementation.
        var sp = BuildProvider();

        sp.GetRequiredService<IRegisterAnchorClient>()
            .Should().BeOfType<RegisterAnchorClient>();
    }

    [Fact]
    public void AddSorchaUserComponents_HostTransportOverrideWins()
    {
        // US4 scenario 3 — a host-registered transport registered BEFORE AddSorchaUserComponents
        // is not replaced by the stub (TryAdd* semantics).
        var mockTransport = new Mock<IVerificationTransport>().Object;

        var sp = BuildProvider(services =>
            services.AddSingleton<IVerificationTransport>(mockTransport));

        sp.GetRequiredService<IVerificationTransport>()
            .Should().BeSameAs(mockTransport);
    }

    [Fact]
    public void AddSorchaUserComponents_HostCatalogueOverrideWins()
    {
        // US4 scenario 3 — host catalogue override wins (TryAdd* semantics).
        var mockCatalogue = new Mock<IVerificationPresetCatalogue>().Object;

        var sp = BuildProvider(services =>
            services.AddSingleton<IVerificationPresetCatalogue>(mockCatalogue));

        sp.GetRequiredService<IVerificationPresetCatalogue>()
            .Should().BeSameAs(mockCatalogue);
    }
}
