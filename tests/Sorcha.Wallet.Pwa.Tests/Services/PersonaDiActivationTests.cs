// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net.Http;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using Sorcha.Tenant.Models.Persona;
using Sorcha.UI.Core.Components.Persona;
using Sorcha.UI.Core.Services.Persona;
using Sorcha.UI.Testing;
using Sorcha.Wallet.Pwa.Extensions;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Guards the PWA DI composition root against regressions where persona
/// services resolve correctly in the web host but fail on the PWA (Feature 163,
/// FR-014). A missing registration (e.g. <c>ILocalStorageService</c> or
/// <c>IPersonaClient</c>) would crash the PWA shell on first profile page load.
/// </summary>
public sealed class PersonaDiActivationTests : ComponentTestFixture
{
    // ---- DI resolution test: guards the PWA composition root ----

    [Fact]
    public void AddCitizenWalletServices_ResolvesPersonaService()
    {
        using var sp = BuildPwaProvider();

        sp.GetService<IPersonaService>()
            .Should().NotBeNull(
                because: "IPersonaService must be registered in AddCitizenWalletServices "
                    + "or Profile.razor will crash on first load (Feature 163 regression guard).");
    }

    [Fact]
    public void AddCitizenWalletServices_PersonaServiceIsPersonaServiceImpl()
    {
        using var sp = BuildPwaProvider();

        sp.GetRequiredService<IPersonaService>()
            .Should().BeOfType<PersonaService>(
                because: "the registered implementation must be PersonaService, "
                    + "which requires IPersonaClient and ILocalStorageService to resolve.");
    }

    // ---- Component rendering test: proves PersonaEditor activates under PWA DI ----

    [Fact]
    public void PersonaEditor_RendersUnderPwaMockedServices_NoMissingDependencyException()
    {
        Services.AddLogging();

        var personaServiceMock = ProvideMock<IPersonaService>();
        personaServiceMock
            .Setup(s => s.GetAutofillEnabledAsync())
            .ReturnsAsync(true);
        personaServiceMock
            .Setup(s => s.GetAsync(
                It.IsAny<PersonaReadOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonaReadModelV1());

        ProvideMock<Sorcha.UI.Core.Services.Feedback.IInlineFeedback>();

        var act = () => Render(EditorUnderPopover());

        act.Should().NotThrow(
            because: "PersonaEditor must render without throwing under a DI container "
                + "that provides IPersonaService and IInlineFeedback (SC-005).");
    }

    // ---- Helpers ----

    private static Microsoft.AspNetCore.Components.RenderFragment EditorUnderPopover() => builder =>
    {
        builder.OpenComponent<MudBlazor.MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<PersonaEditor>(1);
        builder.CloseComponent();
    };

    private static ServiceProvider BuildPwaProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new HttpClient { BaseAddress = new Uri("http://localhost/") });
        services.AddSingleton(new Mock<IJSRuntime>().Object);
        services.AddCitizenWalletServices("http://localhost/");
        return services.BuildServiceProvider();
    }
}
