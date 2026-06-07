// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.UI.Core.Services.User.Devices;
using Sorcha.UI.Testing;
using Sorcha.Wallet.Pwa.Components;
using Sorcha.Wallet.Pwa.Services;
using Sorcha.Wallet.Pwa.Services.Enrolment;
using Sorcha.Wallet.Pwa.Services.Wallet;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Components;

/// <summary>
/// Feature 149 — bUnit tests for the wallet-aware <see cref="PairingTakeover"/>.
/// Covers the three-state machine: walletless → create-wallet body (US1);
/// has-wallet → existing pair body (US2); has-device → hidden (US2); and the
/// no-flash rule while the wallet check is in flight (US3).
/// </summary>
public sealed class PairingTakeoverTests : ComponentTestFixture
{
    private const string CreateWallet = "[data-testid=pairing-takeover-create-wallet]";
    private const string CreateWalletButton = "[data-testid=pairing-takeover-create-wallet-button]";
    private const string PrimaryButton = "[data-testid=pairing-takeover-primary-button]";
    private const string Overlay = "[data-testid=pairing-takeover]";

    private readonly FakeHasPairedDeviceProbe _deviceProbe = new();
    private readonly FakeHasWalletProbe _walletProbe = new();

    public PairingTakeoverTests()
    {
        Services.AddSingleton<IHasPairedDeviceProbe>(_deviceProbe);
        Services.AddSingleton<IHasWalletProbe>(_walletProbe);
        ProvideMock<IEnrolmentService>();
        ProvideMock<IPairingShortCodeRedeemer>();
        ProvideMock<IAccessTokenStore>();
        Services.AddSingleton(new CitizenWalletHubConnection(
            "https://test.example", new Mock<IAccessTokenStore>().Object,
            NullLogger<CitizenWalletHubConnection>.Instance));
    }

    // US1 — walletless citizen sees the create-wallet body, not the enrol button.
    [Fact]
    public void WalletlessNoDevice_RendersCreateWalletState_NotEnrolButton()
    {
        _deviceProbe.HasAnyDevice = false;
        _walletProbe.Result = false;

        var cut = Render<PairingTakeover>();

        cut.FindAll(CreateWallet).Should().NotBeEmpty("a walletless citizen must be routed to create a wallet");
        cut.FindAll(CreateWalletButton).Should().NotBeEmpty();
        cut.FindAll(PrimaryButton).Should().BeEmpty("the dead-ending enrol button must not be offered when there is no wallet");
    }

    // US1 — the create-wallet CTA force-loads the web host's wallet-creation
    // page, which lives under the WASM client base path /app (NOT origin-root;
    // that distinction is what the on-n1 browser check caught).
    [Fact]
    public void CreateWalletButton_NavigatesToWebAppWalletCreation()
    {
        _deviceProbe.HasAnyDevice = false;
        _walletProbe.Result = false;
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();

        var cut = Render<PairingTakeover>();
        cut.Find(CreateWalletButton).Click();

        nav.History.Should().NotBeEmpty();
        nav.History.Last().Uri.Should().EndWith("/app/wallets/create");
    }

    // US2 — citizen with a wallet but no device here sees the existing pair flow.
    [Fact]
    public void HasWalletNoDevice_RendersPairState_NotCreateWallet()
    {
        _deviceProbe.HasAnyDevice = false;
        _walletProbe.Result = true;

        var cut = Render<PairingTakeover>();

        cut.FindAll(PrimaryButton).Should().NotBeEmpty("a wallet owner must still get the pair flow");
        cut.FindAll(CreateWallet).Should().BeEmpty();
    }

    // US2 — a device already paired here keeps the takeover hidden.
    [Fact]
    public void HasDevice_RendersNothing()
    {
        _deviceProbe.HasAnyDevice = true;
        _walletProbe.Result = false;

        var cut = Render<PairingTakeover>();

        cut.FindAll(Overlay).Should().BeEmpty();
    }

    // US3 — while the wallet check is in flight, nothing is shown (no flash);
    // once it resolves to walletless, the create-wallet body appears.
    [Fact]
    public void WalletCheckInFlight_RendersNothing_ThenCreateWalletOnResolve()
    {
        _deviceProbe.HasAnyDevice = false;
        _walletProbe.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var cut = Render<PairingTakeover>();

        cut.FindAll(Overlay).Should().BeEmpty("the overlay must not flash while the wallet check is pending");

        _walletProbe.Gate.SetResult(false);

        cut.WaitForAssertion(() => cut.FindAll(CreateWallet).Should().NotBeEmpty());
    }

    private sealed class FakeHasPairedDeviceProbe : IHasPairedDeviceProbe
    {
        public bool? HasAnyDevice { get; set; }
        public DateTimeOffset? LatestEnrolledAt { get; set; }
        public event Action? Changed;
        public Task EnsureLoadedAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RaiseLocalPairCompleted(CancellationToken ct = default)
        {
            HasAnyDevice = true;
            Changed?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHasWalletProbe : IHasWalletProbe
    {
        public bool Result { get; set; }
        public TaskCompletionSource<bool>? Gate { get; set; }
        public Task<bool> HasWalletAsync(CancellationToken ct = default) =>
            Gate is not null ? Gate.Task : Task.FromResult(Result);
    }
}
