// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor;
using Sorcha.UI.Core.Models.Credentials;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Credentials;
using Sorcha.UI.Core.Services.Feedback;
using Sorcha.UI.Core.Services.User.Devices;
using Sorcha.UI.Core.Services.Wallet;
using Sorcha.UI.Testing;
using Sorcha.UI.Testing.Builders;
using Xunit;
using MyCredentialsPage = Sorcha.UI.Web.Client.Pages.MyCredentials;

namespace Sorcha.UI.Core.Tests.Pages.Credentials;

/// <summary>
/// Issue #1280 (UT-017) — pressing Present on the web told the citizen that verifiable-credential
/// presentation was "planned for a future release". It ships on the wallet PWA; Sorcha is
/// companion-first, so the web is deliberately not the presenting surface. The affordance must
/// therefore signpost the phone, not deny the feature exists.
/// </summary>
public sealed class MyCredentialsPresentTests : ComponentTestFixture
{
    private readonly Mock<ICredentialApiService> _credentialApi = new();
    private readonly Mock<IWalletApiService> _walletApi = new();
    private readonly Mock<IWorkflowService> _workflowService = new();
    private readonly Mock<IDialogService> _dialogService = new();
    private readonly Mock<IHasPairedDeviceProbe> _probe = new();

    private const string WalletAddress = "0x0000000000000000000000000000000000000001";

    public MyCredentialsPresentTests()
    {
        Services.AddSingleton(_credentialApi.Object);
        Services.AddSingleton(_walletApi.Object);
        Services.AddSingleton(_workflowService.Object);
        Services.AddSingleton(_dialogService.Object);
        Services.AddSingleton(Mock.Of<IInlineFeedback>());
        Services.AddSingleton(_probe.Object);
        Services.AddSingleton(new WalletHubConnection(
            "https://localhost",
            Mock.Of<Sorcha.UI.Core.Services.Authentication.IAuthenticationService>(),
            Mock.Of<Sorcha.UI.Core.Services.Configuration.IConfigurationService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WalletHubConnection>.Instance));

        _probe.SetupGet(p => p.HasAnyDevice).Returns(true);

        _walletApi.Setup(w => w.GetWalletsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalletDto>
            {
                new WalletDtoBuilder().WithAddress(WalletAddress).Build(),
            });

        _credentialApi.Setup(c => c.GetCredentialsAsync(WalletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CredentialCardViewModel> { ActiveCredential() });
    }

    private static CredentialCardViewModel ActiveCredential() => new()
    {
        CredentialId = "cred-1",
        Type = "https://sorcha.dev/vc/assured-identity/v1",
        DisplayName = "Assured Identity",
        IssuerOrgName = "Acme Identity Assurance",
        Status = CredentialStatus.Active,
        IsPending = false,
        IssuedAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
        AvailableActions = new List<string> { "Present" },
    };

    /// <summary>
    /// bUnit's default WaitForAssertion timeout is 1s. This page's OnInitializedAsync awaits
    /// <c>WalletHubConnection.StartAsync</c> against an unreachable host before it loads anything,
    /// and on a Windows dev box that handshake alone can outlast the default — the wait then
    /// reports the loading skeleton rather than the real assertion. An explicit budget keeps the
    /// failure message about the behaviour under test.
    /// </summary>
    private static readonly TimeSpan LoadBudget = TimeSpan.FromSeconds(30);

    private static readonly RenderFragment Page = builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<MyCredentialsPage>(1);
        builder.CloseComponent();
    };

    [Fact]
    public void PressingPresent_SignpostsThePhone_NeverClaimsPresentationIsUnbuilt()
    {
        var cut = Render(Page);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Your credentials"), LoadBudget);

        var present = cut.FindAll("button")
            .First(b => b.TextContent.Trim().Equals("Present", StringComparison.Ordinal));
        present.Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='present-on-phone-notice']").Should().ContainSingle(
                "the citizen asked to present — they must be told where presenting happens");
            cut.Markup.Should().NotContain("future release");
        });
    }

    [Fact]
    public void TheNotice_NamesTheCredentialTheCitizenPickedFrom()
    {
        var cut = Render(Page);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Your credentials"), LoadBudget);

        cut.FindAll("button")
            .First(b => b.TextContent.Trim().Equals("Present", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='present-on-phone-notice']").TextContent
                .Should().Contain("Assured Identity"));
    }

    [Fact]
    public void DismissingTheNotice_RetractsIt()
    {
        var cut = Render(Page);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Your credentials"), LoadBudget);

        cut.FindAll("button")
            .First(b => b.TextContent.Trim().Equals("Present", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='present-on-phone-dismiss']").Should().ContainSingle());
        cut.Find("[data-testid='present-on-phone-dismiss']").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='present-on-phone-notice']").Should().BeEmpty());
    }
}
