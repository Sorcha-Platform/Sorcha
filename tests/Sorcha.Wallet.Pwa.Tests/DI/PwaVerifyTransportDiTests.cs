// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.UI.Components.User.Extensions;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.Wallet.Pwa.Services.Signing;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.DI;

/// <summary>
/// DI-resolution assertions: confirms the PWA host registers <see cref="HaipVerificationTransport"/>
/// as <see cref="IVerificationTransport"/> and <see cref="EphemeralVerifierIdentityAdapter"/> as
/// <see cref="IVerifierIdentityProvider"/> (Feature 164, B3 US2 / contract C1 / SC-002).
/// </summary>
public sealed class PwaVerifyTransportDiTests
{
    /// <summary>
    /// Builds a minimal ServiceCollection that mirrors the PWA's DI configuration
    /// without requiring IJSRuntime (which is WebAssembly-only).
    /// </summary>
    private static ServiceProvider BuildPwaServices()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();

        // Library defaults (registers NotConfiguredVerificationTransport via TryAdd)
        services.AddSorchaUserComponents(configuration);

        // PWA-specific dependencies needed by EphemeralVerifierIdentityAdapter
        var mockEphemeralService = new Mock<Sorcha.UI.Components.User.Services.Signing.IEphemeralVerifierIdentityService>();
        services.AddScoped<Sorcha.UI.Components.User.Services.Signing.IEphemeralVerifierIdentityService>(_ => mockEphemeralService.Object);

        // Feature 164 B3 (US2): PWA overrides — must appear after AddSorchaUserComponents
        services.AddScoped<IVerifierIdentityProvider, EphemeralVerifierIdentityAdapter>();
        services.AddScoped<IVerificationTransport, HaipVerificationTransport>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void ResolvedTransport_IsPwaHaipTransport_NotStub()
    {
        // Arrange
        using var provider = BuildPwaServices();

        // Act
        var transport = provider.GetRequiredService<IVerificationTransport>();

        // Assert — the PWA host must override the stub with the live transport (C1 / SC-002)
        transport.Should().BeOfType<HaipVerificationTransport>(
            because: "the PWA's DI override must resolve HaipVerificationTransport, never NotConfiguredVerificationTransport");
        transport.Should().NotBeOfType<NotConfiguredVerificationTransport>();
    }

    [Fact]
    public void ResolvedIdentityProvider_IsEphemeralAdapter_NotStableOrg()
    {
        // Arrange
        using var provider = BuildPwaServices();

        // Act
        var identityProvider = provider.GetRequiredService<IVerifierIdentityProvider>();

        // Assert — the PWA uses ephemeral P-256 identity, not a stable org DID (US2 vs US3 boundary)
        identityProvider.Should().BeOfType<EphemeralVerifierIdentityAdapter>(
            because: "the PWA uses an ephemeral P-256 JWK thumbprint as the client_id, not a stable org DID");
    }
}
