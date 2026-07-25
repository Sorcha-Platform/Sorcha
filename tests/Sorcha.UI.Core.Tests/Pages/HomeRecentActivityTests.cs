// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Models.Dashboard;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Wallet;
using Sorcha.UI.Testing;
using Sorcha.UI.Testing.Builders;
using Xunit;
using HomePage = Sorcha.UI.Web.Client.Pages.Home;

namespace Sorcha.UI.Core.Tests.Pages;

/// <summary>
/// Issue #1267 (UT-004) — decision notices were invisible to the citizen on every web surface.
/// The bell-badge half is fixed (the count now includes <c>Workflow</c>+<c>Warning</c> entries,
/// which is the shape F184 writes a rejection as). This is the remaining half: the web home page
/// rendered a hard-coded "no recent activity" placeholder that was never wired to anything, so a
/// citizen whose identity application had just been REJECTED landed on a dashboard cheerfully
/// telling them nothing had happened.
/// <para>
/// The placeholder was not merely empty — it was wrong. The inbox entry existed, the PWA feed
/// listed it, and the web <c>/activity</c> route rendered it. Only home lied.
/// </para>
/// </summary>
public sealed class HomeRecentActivityTests : ComponentTestFixture
{
    private readonly Mock<IInboxApiService> _inbox = new();
    private readonly Mock<IWalletApiService> _walletApi = new();
    private readonly Mock<IWalletPreferenceService> _walletPrefs = new();
    private readonly Mock<IDashboardService> _dashboard = new();
    private readonly Mock<IAlertService> _alerts = new();
    private readonly Mock<IAlertDismissalService> _alertDismissal = new();
    private readonly Mock<ILocalizationService> _loc = new();

    private const string WalletAddress = "0x0000000000000000000000000000000000000001";

    public HomeRecentActivityTests()
    {
        Services.AddLogging();
        Services.AddSingleton(_inbox.Object);
        Services.AddSingleton(_walletApi.Object);
        Services.AddSingleton(_walletPrefs.Object);
        Services.AddSingleton(_dashboard.Object);
        Services.AddSingleton(_alerts.Object);
        Services.AddSingleton(_alertDismissal.Object);
        Services.AddSingleton(_loc.Object);
        Services.AddSingleton(new TenantHubConnection(
            "http://test.local",
            () => Task.FromResult<string?>(null),
            NullLogger<TenantHubConnection>.Instance,
            _inbox.Object));

        // A plain signed-in citizen: no admin roles, so the stats grid and AlertsPanel stay out of
        // the way and the assertions are about the activity region only.
        AddAuthorization().SetAuthorized("citizen@example.test");

        // Localisation is a pass-through here — the keys ARE the assertion targets elsewhere, and
        // resolving them to themselves keeps this test about the activity region.
        _loc.Setup(l => l.T(It.IsAny<string>())).Returns((string k) => k);
        _loc.Setup(l => l.T(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string k, object[] _) => k);

        var wallet = new WalletDtoBuilder().WithAddress(WalletAddress).Build();
        _walletApi.Setup(w => w.GetWalletsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalletDto> { wallet });
        _walletPrefs.Setup(p => p.GetSmartDefaultAsync(It.IsAny<List<WalletDto>>()))
            .ReturnsAsync(WalletAddress);

        _dashboard.Setup(d => d.GetDashboardStatsAsync(It.IsAny<string>()))
            .ReturnsAsync(new DashboardStatsViewModel());
        _alerts.Setup(a => a.GetAlertsAsync()).ReturnsAsync(new AlertsResponse());
        _alertDismissal.Setup(a => a.FilterDismissedAsync(It.IsAny<IReadOnlyList<ServiceAlert>>()))
            .ReturnsAsync((IReadOnlyList<ServiceAlert> a) => a);
    }

    private static InboxEntryDto Rejection() => new()
    {
        Id = Guid.NewGuid(),
        PlatformUserId = Guid.NewGuid(),
        // Exactly the shape F184 writes a decision notice as — this pairing is what the bell badge
        // used to miss, and it is what home must not miss either.
        Category = "workflow",
        Severity = "warning",
        IconKey = "workflow.rejected",
        CorrelationKey = "k",
        DetailHref = "",
        SourceEventId = Guid.NewGuid(),
        OccurredAt = DateTimeOffset.UtcNow,
        Title = "Your Assured Identity application was not approved",
        Summary = "Confirm your email address and reapply.",
    };

    private void InboxReturns(params InboxEntryDto[] entries) =>
        _inbox.Setup(a => a.ListAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxPageDto(entries, 1, 50, entries.Length));

    private static readonly TimeSpan LoadBudget = TimeSpan.FromSeconds(30);

    [Fact]
    public void ARejectionInTheInbox_IsVisibleOnHome()
    {
        InboxReturns(Rejection());

        var cut = Render<HomePage>();

        cut.WaitForAssertion(
            () => cut.Markup.Should().Contain("Your Assured Identity application was not approved",
                "a citizen whose application was rejected must learn that from their home page"),
            LoadBudget);
    }

    [Fact]
    public void HomeNoLongerRendersAHardCodedEmptyPlaceholder()
    {
        InboxReturns(Rejection());

        var cut = Render<HomePage>();

        cut.WaitForAssertion(
            () => cut.FindAll("[data-testid='activity-feed']").Should().ContainSingle(
                "home must render the real inbox-backed feed, not a static panel"),
            LoadBudget);

        // The old placeholder resolved this key unconditionally — even with entries present.
        cut.Markup.Should().NotContain("dashboard.noRecentActivity");
    }

    [Fact]
    public void WithAnEmptyInbox_HomeSaysSo_ViaTheFeedsOwnEmptyState()
    {
        InboxReturns();

        var cut = Render<HomePage>();

        cut.WaitForAssertion(
            () => cut.FindAll("[data-testid='activity-feed-empty']").Should().ContainSingle(
                "an empty inbox is a real state — but it must come from the feed, not a hard-coded panel"),
            LoadBudget);
    }

    [Fact]
    public void HomeOffersARouteToTheFullHistory()
    {
        InboxReturns(Rejection());

        var cut = Render<HomePage>();

        cut.WaitForAssertion(
            () => cut.FindAll("[data-testid='home-activity-see-all']").Should().ContainSingle(
                "home shows a slice — the citizen needs a way to the full history"),
            LoadBudget);
    }
}
