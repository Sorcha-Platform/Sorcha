// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using Xunit;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// #1704 follow-up — the receipt services must be registered by the REAL host, not only by a test's
/// own <see cref="ServiceCollection"/>.
/// </summary>
/// <remarks>
/// The publisher was registered inside <c>AddDocketDistributor</c>, whose only caller is
/// <c>AddServiceIntegration</c>, which nothing calls. Every unit test built its own container that
/// included the publisher, so all of them passed while the deployed validator threw "No service for
/// type IReceiptPublisher" after every docket write. This test resolves from <c>Program</c>'s graph.
/// </remarks>
public class ReceiptServicesCompositionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ReceiptServicesCompositionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
            // Only the outbound clients are replaced — the registrations under test are left alone.
            services.RemoveAll<IWalletServiceClient>();
            services.RemoveAll<IRegisterServiceClient>();
            services.AddScoped(_ => new Mock<IWalletServiceClient>().Object);
            services.AddScoped(_ => new Mock<IRegisterServiceClient>().Object);
        }));
    }

    [Theory]
    [InlineData(typeof(IReceiptPublisher))]
    [InlineData(typeof(IReceiptGenerator))]
    public void TheHostResolves(Type serviceType)
    {
        using var scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetService(serviceType).Should().NotBeNull(
            $"{serviceType.Name} is resolved on every docket write; the host must register it");
    }
}
