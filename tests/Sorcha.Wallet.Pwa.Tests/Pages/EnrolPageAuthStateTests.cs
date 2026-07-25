// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.UI.Testing;
using Sorcha.Wallet.Pwa.Services;
using Sorcha.Wallet.Pwa.Services.Enrolment;
using Xunit;
using EnrolPage = Sorcha.Wallet.Pwa.Pages.Enrol;

namespace Sorcha.Wallet.Pwa.Tests.Pages;

/// <summary>
/// Guards issue #1265, found on a real citizen's phone (iOS TestFlight): the PWA's Enrol wizard
/// told a signed-in citizen "You need to sign in first" while Settings, one tap away, correctly
/// read "You're signed in." — blocking every downstream credential delivery.
/// <para>
/// The cause is the same defect class already fixed in Settings (see <see cref="SettingsPageTests"/>):
/// <see cref="AccessTokenRecord.Email"/> is "captured at sign-in for display only" and is null on the
/// passkey and social paths, so deriving auth state from email presence reports a passkey citizen as
/// signed out. Enrol must gate on the same
/// <see cref="Microsoft.AspNetCore.Components.Authorization.AuthorizeView"/> signal (token presence
/// and expiry via <see cref="WalletAuthenticationStateProvider"/>) the rest of the shell uses.
/// </para>
/// <para>
/// No HTTP request is involved in this check, so the ambient-HttpClient-without-bearer hypothesis
/// recorded on the issue is not the cause — this is a purely local mis-derivation.
/// </para>
/// </summary>
public sealed class EnrolPageAuthStateTests : ComponentTestFixture
{
    private readonly Mock<IAuthService> _auth = new();

    public EnrolPageAuthStateTests()
    {
        Services.AddSingleton(_auth.Object);
        Services.AddSingleton(new Mock<IEnrolmentService>().Object);
        Services.AddSingleton<IAccessTokenStore>(new InMemoryAccessTokenStore());
        Services.AddSingleton(new Mock<IEnrolSessionRedeemer>().Object);
        Services.AddSingleton(new WalletAuthenticationStateProvider(new InMemoryAccessTokenStore()));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<EnrolPage>>(
            NullLogger<EnrolPage>.Instance);
    }

    /// <summary>
    /// The reported bug, exactly: a valid non-expired token with no email — the passkey shape.
    /// Enrol must treat this citizen as signed in.
    /// </summary>
    [Fact]
    public void SignedIn_WithNullEmail_DoesNotDemandSignIn()
    {
        _auth.Setup(a => a.GetSignedInEmailAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var authContext = AddAuthorization();
        authContext.SetAuthorized("citizen"); // authenticated, no email claim — passkey / social

        var cut = Render<EnrolPage>();

        cut.Markup.Should().NotContain(
            "You need to sign in first",
            "a citizen holding a valid token must never be told to sign in — this blocks all "
            + "credential delivery to the wallet (#1265)");
        cut.FindAll("[data-testid=enrol-open-settings-link]").Should().BeEmpty(
            "the Open Settings escape hatch belongs only on the genuinely-signed-out path");
        cut.FindAll("[data-testid=enrol-continue-button]").Should().ContainSingle(
            "a signed-in citizen must be able to proceed through the wizard");
    }

    /// <summary>An email-and-password citizen keeps the existing "Signed in as x" affordance.</summary>
    [Fact]
    public void SignedIn_WithEmail_ShowsEmailAndAllowsContinue()
    {
        _auth.Setup(a => a.GetSignedInEmailAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("citizen@example.test");
        var authContext = AddAuthorization();
        authContext.SetAuthorized("citizen@example.test");

        var cut = Render<EnrolPage>();

        cut.Markup.Should().Contain("citizen@example.test");
        cut.Markup.Should().NotContain("You need to sign in first");
        cut.FindAll("[data-testid=enrol-continue-button]").Should().ContainSingle();
    }

    /// <summary>
    /// The genuinely-signed-out path must still refuse and route to sign-in — the fix must not
    /// simply drop the gate.
    /// </summary>
    [Fact]
    public void NotSignedIn_DemandsSignIn()
    {
        _auth.Setup(a => a.GetSignedInEmailAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var authContext = AddAuthorization();
        authContext.SetNotAuthorized();

        var cut = Render<EnrolPage>();

        cut.Markup.Should().Contain("You need to sign in first");
        cut.FindAll("[data-testid=enrol-continue-button]").Should().BeEmpty(
            "a signed-out visitor must not be able to start the enrolment ceremony");
    }

    /// <summary>
    /// #1265 second half: the wizard described what it registers as "this browser". In the
    /// TestFlight / installed-app shell that is wrong and reads as if the wrong thing is being
    /// enrolled.
    /// </summary>
    [Fact]
    public void WelcomeCopy_DescribesADevice_NotABrowser()
    {
        _auth.Setup(a => a.GetSignedInEmailAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var authContext = AddAuthorization();
        authContext.SetAuthorized("citizen");

        var cut = Render<EnrolPage>();

        cut.Markup.Should().NotContain(
            "registers this browser",
            "the PWA also runs as an installed app, where 'browser' is wrong (#1265)");
    }
}
