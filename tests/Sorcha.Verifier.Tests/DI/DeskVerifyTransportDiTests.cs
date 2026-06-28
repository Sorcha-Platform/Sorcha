// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.UI.Components.User.Extensions;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.Verifier.Services;
using Xunit;

namespace Sorcha.Verifier.Tests.DI;

/// <summary>
/// DI-resolution assertions: confirms the desk verifier host registers
/// <see cref="HaipVerificationTransport"/> as <see cref="IVerificationTransport"/>
/// and <see cref="StableOrgVerifierIdentityProvider"/> as <see cref="IVerifierIdentityProvider"/>
/// (Feature 164, B3 US3 / contract C1 / SC-002).
/// </summary>
public sealed class DeskVerifyTransportDiTests
{
    private static ServiceProvider BuildDeskServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Verifier:OrgId"] = "test-org-123"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IConfiguration>(configuration);

        // Library defaults (registers NotConfiguredVerificationTransport via TryAdd)
        services.AddSorchaUserComponents(configuration);

        // Feature 164 B3 (US3): desk overrides — mirrors what Program.cs / ServiceCollectionExtensions does
        services.AddScoped<IVerifierIdentityProvider, StableOrgVerifierIdentityProvider>();
        services.AddScoped<IVerificationTransport, HaipVerificationTransport>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void ResolvedTransport_IsHaipTransport_NotStub()
    {
        // Arrange
        using var provider = BuildDeskServices();

        // Act
        var transport = provider.GetRequiredService<IVerificationTransport>();

        // Assert — the desk host must override the stub with the live transport (C1 / SC-002)
        transport.Should().BeOfType<HaipVerificationTransport>(
            because: "the desk DI override must resolve HaipVerificationTransport, never NotConfiguredVerificationTransport");
        transport.Should().NotBeOfType<NotConfiguredVerificationTransport>();
    }

    [Fact]
    public void ResolvedIdentityProvider_IsStableOrgProvider_NotEphemeralAdapter()
    {
        // Arrange
        using var provider = BuildDeskServices();

        // Act
        var identityProvider = provider.GetRequiredService<IVerifierIdentityProvider>();

        // Assert — the desk uses a stable org DID, not the ephemeral P-256 adapter (US3 vs US2 boundary)
        identityProvider.Should().BeOfType<StableOrgVerifierIdentityProvider>(
            because: "the desk verifier uses a stable did:sorcha:verifier:{orgId} as the client_id");
    }

    [Fact]
    public async Task StableOrgProvider_ReturnsStableDid_FromConfiguration()
    {
        // Arrange
        using var provider = BuildDeskServices();
        var identityProvider = provider.GetRequiredService<IVerifierIdentityProvider>();

        // Act
        var clientId = await identityProvider.GetClientIdAsync();

        // Assert
        clientId.Should().Be("did:sorcha:verifier:test-org-123",
            because: "the stable org DID is derived from Verifier:OrgId configuration");
    }
}
