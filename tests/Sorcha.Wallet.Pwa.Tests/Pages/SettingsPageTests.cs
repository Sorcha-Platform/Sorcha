// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.UI.Testing;
using Sorcha.Wallet.Pwa.Services;
using Xunit;
using SettingsPage = Sorcha.Wallet.Pwa.Pages.Settings;

namespace Sorcha.Wallet.Pwa.Tests.Pages;

/// <summary>
/// Guards a bug found on a real citizen's phone (iOS TestFlight): a passkey/social sign-in never
/// carries an email (see <see cref="AccessTokenRecord"/> — Email is "captured at sign-in for
/// display only", and is explicitly null on those paths), so gating Settings → Account on email
/// presence showed a signed-in citizen a "Sign in" button and hid their only Sign out control.
/// The fix gates on the same <see cref="Microsoft.AspNetCore.Components.Authorization.AuthorizeView"/>
/// signal (token presence/expiry via <see cref="WalletAuthenticationStateProvider"/>) the rest of
/// the shell uses.
/// </summary>
public sealed class SettingsPageTests : ComponentTestFixture
{
    private readonly Mock<IAuthService> _auth = new();

    public SettingsPageTests()
    {
        Services.AddSingleton(_auth.Object);
        Services.AddSingleton(new WalletAuthenticationStateProvider(new InMemoryAccessTokenStore()));
    }

    // The bug: passkey/social sign-in leaves AccessTokenRecord.Email null even though the citizen
    // holds a valid, non-expired token. This is exactly that shape — authenticated, no email.
    [Fact]
    public void SignedIn_WithNullEmail_RendersSignOut_NotSignIn()
    {
        _auth.Setup(a => a.GetSignedInEmailAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var authContext = AddAuthorization();
        authContext.SetAuthorized("citizen"); // authenticated identity, no email claim — the passkey shape

        var cut = Render<SettingsPage>();

        cut.FindAll("[data-testid=settings-signout]").Should().ContainSingle(
            "a signed-in citizen (even with no known email) must see Sign out");
        cut.FindAll("[data-testid=settings-signin-link]").Should().BeEmpty(
            "Settings must never offer Sign in to a user who is already signed in");
    }

    [Fact]
    public void SignedIn_WithEmail_ShowsEmailAndSignOut()
    {
        _auth.Setup(a => a.GetSignedInEmailAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("citizen@example.test");
        var authContext = AddAuthorization();
        authContext.SetAuthorized("citizen@example.test");

        var cut = Render<SettingsPage>();

        cut.Markup.Should().Contain("citizen@example.test");
        cut.FindAll("[data-testid=settings-signout]").Should().ContainSingle();
    }

    [Fact]
    public void NotSignedIn_RendersSignIn_NotSignOut()
    {
        _auth.Setup(a => a.GetSignedInEmailAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var authContext = AddAuthorization();
        authContext.SetNotAuthorized();

        var cut = Render<SettingsPage>();

        cut.FindAll("[data-testid=settings-signin-link]").Should().ContainSingle();
        cut.FindAll("[data-testid=settings-signout]").Should().BeEmpty();
    }
}
