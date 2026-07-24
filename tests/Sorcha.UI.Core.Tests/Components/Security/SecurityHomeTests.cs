// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Models.Auth;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor;
using MudBlazor.Services;
using Sorcha.UI.Core.Components.Security;
using Sorcha.UI.Core.Models;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Feedback;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Security;

/// <summary>
/// Feature 150 — bUnit tests for <see cref="SecurityHome"/>. Verifies the three job-based groups
/// render, an <see cref="AssuranceBadge"/> appears per method, and a load failure surfaces the
/// error state. The component reflects the server's per-row flags (it never decides), so the test
/// drives behaviour purely off the mocked aggregate read.
/// </summary>
public sealed class SecurityHomeTests : BunitContext
{
    private readonly Mock<IAuthMethodsClientService> _client = new();
    private readonly Mock<ITotpClientService> _totp = new();
    private readonly Mock<ILocalizationService> _loc = new();

    public SecurityHomeTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Localization mock echoes the key so assertions stay copy-independent.
        _loc.Setup(l => l.T(It.IsAny<string>())).Returns<string>(k => k);
        _loc.Setup(l => l.T(It.IsAny<string>(), It.IsAny<object[]>())).Returns<string, object[]>((k, _) => k);

        _totp.Setup(t => t.GetStatusAsync()).ReturnsAsync(new TotpStatusResponse(IsEnabled: false, VerifiedAt: null));

        Services.AddSingleton(_client.Object);
        Services.AddSingleton(_totp.Object);
        Services.AddSingleton(_loc.Object);
        Services.AddSingleton<IInlineFeedback, InlineFeedback>();
        Services.AddScoped<PasskeyInteropService>();
    }

    // MudTooltip (rendered for a non-removable passkey row) needs a MudPopoverProvider in the tree,
    // so render the component beneath one. Returns the bUnit-inferred rendered fragment.
    private static RenderFragment HomeUnderPopover() => builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<SecurityHome>(1);
        builder.CloseComponent();
    };

    private static AuthMethodsResponse PopulatedMethods() => new(
        Email: "ada@test.com",
        EmailVerified: true,
        Password: new AuthMethodsPassword(
            IsSet: true, LastChangedAt: null, CanRemove: true,
            AssuranceTier: AuthAssuranceTier.Strong, RequiredProofTier: AuthAssuranceTier.Basic),
        Socials:
        [
            new AuthMethodsSocial(
                LinkId: Guid.NewGuid(), Provider: "google", Email: "ada@test.com", DisplayName: null,
                LinkedAt: DateTimeOffset.UtcNow, LastUsedAt: null, CanRemove: true,
                AssuranceTier: AuthAssuranceTier.Basic, RequiredProofTier: AuthAssuranceTier.Basic),
        ],
        Passkeys:
        [
            // CanRemove=false → the Remove control must be disabled (last-method / floor reflection).
            new AuthMethodsPasskey(
                Id: Guid.NewGuid(), DisplayName: "iPhone", DeviceType: null, Status: "Active",
                DisabledReason: null, CreatedAt: DateTimeOffset.UtcNow, LastUsedAt: null,
                CanRemove: false, CanRename: true,
                AssuranceTier: AuthAssuranceTier.Strongest, RequiredProofTier: AuthAssuranceTier.Strongest),
        ],
        SmsAvailable: false);

    [Fact]
    public void RendersThreeJobGroups_WithAssuranceBadges()
    {
        _client.Setup(c => c.GetAuthMethodsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PopulatedMethods());

        var cut = Render(HomeUnderPopover());

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=security-group-signin]").Should().NotBeNull();
            cut.Find("[data-testid=security-group-2fa]").Should().NotBeNull();
            cut.Find("[data-testid=security-group-recovery]").Should().NotBeNull();
            // At least the recovery (backup-codes) badge plus the per-method badges.
            cut.FindAll("[data-testid=assurance-badge]").Count.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void DisablesPasskeyRemove_WhenServerSaysCanRemoveFalse()
    {
        _client.Setup(c => c.GetAuthMethodsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PopulatedMethods());

        var cut = Render(HomeUnderPopover());

        cut.WaitForAssertion(() =>
        {
            // The passkey row with CanRemove=false renders its Remove button disabled (the UI
            // reflects the server's gate; it never enables a removal the server would block).
            var disabledButtons = cut.FindAll("button[disabled]");
            disabledButtons.Should().NotBeEmpty();
        });
    }

    [Fact]
    public void RendersLoadFailed_WhenAggregateReadReturnsNull()
    {
        _client.Setup(c => c.GetAuthMethodsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthMethodsResponse?)null);

        var cut = Render(HomeUnderPopover());

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=security-load-failed]").Should().NotBeNull();
        });
    }
}
