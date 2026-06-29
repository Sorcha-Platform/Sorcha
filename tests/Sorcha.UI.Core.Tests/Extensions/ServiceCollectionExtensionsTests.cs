// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Services.Credentials;
using Xunit;

namespace Sorcha.UI.Core.Tests.Extensions;

/// <summary>
/// Spot-check that <see cref="ServiceCollectionExtensions.AddCoreServices"/> registers
/// <see cref="IHaipOfferService"/> (backed by <see cref="HaipOfferService"/>).
/// The implementation factory in AddCoreServices manually wraps the client with
/// <c>AuthenticatedHttpMessageHandler</c> — this test asserts the registration is present
/// and scoped (not singleton), which is the minimum signal that the handler chain is in play.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCoreServices_RegistersIHaipOfferService_AsScopedRegistration()
    {
        var services = new ServiceCollection();

        // AddCoreServices registers IHaipOfferService via a factory lambda;
        // we only inspect the ServiceDescriptor — we do not resolve the service
        // (which would require the full Blazor/JS infrastructure).
        services.AddCoreServices("https://api.example.com");

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IHaipOfferService));

        descriptor.Should().NotBeNull(
            "AddCoreServices must register IHaipOfferService");
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped,
            "user-JWT clients are scoped to avoid sharing auth state across requests");
    }
}
