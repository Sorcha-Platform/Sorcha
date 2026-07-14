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
using Sorcha.UI.Core.Services.Wallet;
using Sorcha.UI.Testing;
using Sorcha.UI.Testing.Builders;
using Xunit;
using MyCredentialsPage = Sorcha.UI.Web.Client.Pages.MyCredentials;

namespace Sorcha.UI.Core.Tests.Pages.Credentials;

/// <summary>
/// bUnit tests for the MyCredentials page's banding logic (Feature: my-credentials
/// redesign). <see cref="CredentialStatus"/> has six values (Active, Suspended,
/// Revoked, Expired, Consumed, Declined) plus PendingAcceptance, but the three
/// bands used to only cover PendingAcceptance, Active, and Expired|Revoked — and
/// the empty state was gated on the total credential count, not on the bands
/// actually being empty. A citizen with exactly one pending offer who clicks
/// Decline landed on NEITHER the empty state NOR any band: a blank page one
/// click into the live AIAS demo.
/// </summary>
public sealed class MyCredentialsTests : ComponentTestFixture
{
    private readonly Mock<ICredentialApiService> _credentialApi = new();
    private readonly Mock<IWalletApiService> _walletApi = new();
    private readonly Mock<IWorkflowService> _workflowService = new();
    private readonly Mock<IDialogService> _dialogService = new();

    private const string WalletAddress = "0x0000000000000000000000000000000000000001";

    public MyCredentialsTests()
    {
        Services.AddSingleton(_credentialApi.Object);
        Services.AddSingleton(_walletApi.Object);
        Services.AddSingleton(_workflowService.Object);
        // Overrides the real MudBlazor DialogService registered by AddMudServices —
        // the page's Decline confirmation goes through this mock, not a rendered dialog.
        Services.AddSingleton(_dialogService.Object);
        Services.AddSingleton(Mock.Of<IInlineFeedback>());
        Services.AddSingleton(new WalletHubConnection(
            "https://localhost",
            Mock.Of<Sorcha.UI.Core.Services.Authentication.IAuthenticationService>(),
            Mock.Of<Sorcha.UI.Core.Services.Configuration.IConfigurationService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WalletHubConnection>.Instance));

        _walletApi.Setup(w => w.GetWalletsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalletDto>
            {
                new WalletDtoBuilder().WithAddress(WalletAddress).Build(),
            });

        _dialogService.Setup(d => d.ShowMessageBoxAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(true);
    }

    private static CredentialCardViewModel PendingCredential() => new()
    {
        CredentialId = "cred-1",
        Type = "AssuredIdentityCredential",
        DisplayName = "Assured Identity",
        IssuerOrgName = "Acme Identity Assurance",
        Status = CredentialStatus.PendingAcceptance,
        IsPending = true,
        IssuedAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
        // No IssuanceInstanceId/ClaimActionId — the decline path's WorkflowService
        // reject-task branch is deliberately not exercised by this test.
    };

    private static RenderFragment Page = builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<MyCredentialsPage>(1);
        builder.CloseComponent();
    };

    [Fact]
    public void DeclineOnlyPendingCredential_NeverRendersBlankPage()
    {
        // First load: one pending offer. After decline, the server-confirmed
        // reload returns the SAME credential now Declined — exactly what the
        // live Wallet Service does; MyCredentials must re-fetch, not just prune
        // its local pending list.
        var pending = PendingCredential();
        var declined = new CredentialCardViewModel
        {
            CredentialId = pending.CredentialId,
            Type = pending.Type,
            DisplayName = pending.DisplayName,
            IssuerOrgName = pending.IssuerOrgName,
            Status = CredentialStatus.Declined,
            IsPending = false,
            IssuedAt = pending.IssuedAt,
        };

        var callCount = 0;
        _credentialApi.Setup(c => c.GetCredentialsAsync(WalletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new List<CredentialCardViewModel> { pending }
                    : new List<CredentialCardViewModel> { declined };
            });
        _credentialApi.Setup(c => c.DeclineCredentialAsync(WalletAddress, "cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var cut = Render(Page);

        // Sanity: the pending band rendered before we decline anything.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Needs you"));

        var declineButton = cut.FindAll("button")
            .First(b => b.TextContent.Trim().Equals("Decline", StringComparison.Ordinal));
        declineButton.Click();

        // The whole point: a citizen must never see header-only markup after a
        // one-click decline. Either the empty state or the Archive band must
        // render — anything but a page with no bands and no empty-state message.
        cut.WaitForAssertion(() =>
        {
            var hasEmptyState = cut.Markup.Contains("No credentials yet");
            var hasArchiveBand = cut.Markup.Contains("Archive");
            var hasPendingBand = cut.Markup.Contains("Needs you");
            (hasEmptyState || hasArchiveBand || hasPendingBand).Should().BeTrue(
                "the page must render the empty state or a band — never header-only markup");
        });

        // The decisive assertion: the declined credential must not vanish into a
        // band-less limbo. It is "not currently usable" — Archive is where it belongs.
        cut.Markup.Should().Contain("Archive");
        cut.Markup.Should().NotContain("Needs you");
    }
}
