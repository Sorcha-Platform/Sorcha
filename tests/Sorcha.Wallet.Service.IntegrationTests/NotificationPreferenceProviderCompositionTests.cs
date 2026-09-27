// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Wallet.Service.IntegrationTests.Fixtures;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.IntegrationTests;

/// <summary>
/// #1694 — the preference provider gained an <c>IServiceAuthClient</c> dependency. A unit test
/// constructs it by hand and cannot see whether the real host can build it; this resolves it from
/// the Wallet's own <c>Program</c> graph (the #1704 lesson: the unit tests there were all green while
/// the deployed validator could not resolve its receipt publisher).
/// </summary>
[Collection("WalletService")]
public class NotificationPreferenceProviderCompositionTests
{
    private readonly WalletServiceWebApplicationFactory _factory;

    public NotificationPreferenceProviderCompositionTests(WalletServiceWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public void TheHost_ResolvesTheTenantBackedPreferenceProvider()
    {
        using var scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<INotificationPreferenceProvider>()
            .Should().BeOfType<TenantNotificationPreferenceProvider>();
    }
}
