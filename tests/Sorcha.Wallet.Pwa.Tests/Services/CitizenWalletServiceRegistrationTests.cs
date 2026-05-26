// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using Sorcha.UI.Core.Services;
using Sorcha.Wallet.Pwa.Extensions;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Guards the PWA's DI composition root (<see cref="ServiceCollectionExtensions.AddCitizenWalletServices"/>).
/// Feature 141 shipped a regression: <c>MainLayout</c> began injecting
/// <c>IThemeService</c> to drive dark mode, but that service was only
/// registered in <c>Sorcha.UI.Core</c>'s web-host extension — so the citizen
/// wallet crashed on first load with "no registered service of type
/// 'Sorcha.UI.Core.Services.IThemeService'". These tests resolve the shell's
/// theme dependencies from the real registration to keep that from recurring.
/// </summary>
public class CitizenWalletServiceRegistrationTests
{
    // Mirrors Program.cs: the framework provides IJSRuntime + logging and an
    // HttpClient is registered before AddCitizenWalletServices runs.
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new HttpClient { BaseAddress = new Uri("http://localhost/") });
        services.AddSingleton(new Mock<IJSRuntime>().Object);
        services.AddCitizenWalletServices("http://localhost/");
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddCitizenWalletServices_ResolvesThemeService()
    {
        using var sp = BuildProvider();

        sp.GetService<IThemeService>()
            .Should().NotBeNull("MainLayout @injects IThemeService; a missing "
                + "registration crashed the PWA shell on load (Feature 141 regression).");
    }

    [Fact]
    public void AddCitizenWalletServices_ResolvesUserPreferencesService()
    {
        using var sp = BuildProvider();

        sp.GetService<IUserPreferencesService>()
            .Should().NotBeNull("ThemeService depends on IUserPreferencesService.");
    }
}
