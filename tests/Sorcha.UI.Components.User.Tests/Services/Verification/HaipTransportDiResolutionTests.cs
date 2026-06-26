// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.UI.Components.User.Extensions;
using Sorcha.UI.Components.User.Services.Verification;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Services.Verification;

/// <summary>
/// DI-resolution assertion: confirms that a host which overrides the stub with
/// <see cref="HaipVerificationTransport"/> resolves that type — never
/// <see cref="NotConfiguredVerificationTransport"/> (Feature 164, B3 US1 / contract C1 / SC-002).
/// </summary>
public sealed class HaipTransportDiResolutionTests
{
    [Fact]
    public void ResolvedTransport_WithHaipOverride_IsHaipVerificationTransport()
    {
        // Arrange — build a ServiceCollection the same way a host Program.cs would
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSorchaUserComponents(configuration);

        // Override the stub with the live transport + required dependencies (as a host would)
        var mockIdentityProvider = new Mock<IVerifierIdentityProvider>();
        services.AddScoped<IVerifierIdentityProvider>(_ => mockIdentityProvider.Object);
        services.AddScoped<IVerificationTransport, HaipVerificationTransport>();

        var provider = services.BuildServiceProvider();

        // Act
        var transport = provider.GetRequiredService<IVerificationTransport>();

        // Assert — must be the live transport, never the stub
        transport.Should().BeOfType<HaipVerificationTransport>(
            because: "the host DI override must win over AddSorchaUserComponents' TryAddSingleton stub");
        transport.Should().NotBeOfType<NotConfiguredVerificationTransport>();
    }

    [Fact]
    public void ResolvedTransport_WithoutHaipOverride_IsNotConfiguredStub()
    {
        // Arrange — host calls AddSorchaUserComponents but does NOT override the transport
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSorchaUserComponents(configuration);

        var provider = services.BuildServiceProvider();

        // Act
        var transport = provider.GetRequiredService<IVerificationTransport>();

        // Assert — the library default stub must be returned
        transport.Should().BeOfType<NotConfiguredVerificationTransport>(
            because: "without a host override, the TryAddSingleton default is NotConfiguredVerificationTransport");
    }
}
