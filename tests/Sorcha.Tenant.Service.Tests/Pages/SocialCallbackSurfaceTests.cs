// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Pages.Auth;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Pages;

public class SocialCallbackSurfaceTests
{
    [Theory]
    [InlineData("wallet", true)]
    [InlineData("WALLET", true)]
    [InlineData("app", false)]
    [InlineData(null, false)]
    public void IsWalletSurface_DetectsWalletReturn(string? surface, bool expected)
        => SocialCallbackModel.IsWalletSurface(surface).Should().Be(expected);

    [Fact]
    public void BuildWalletRedirect_PacksTokenRefreshExpiry()
    {
        var url = SocialCallbackModel.BuildWalletRedirect("AT", "RT", 3600, returnUrl: null);
        url.Should().StartWith("/wallet/#");
        url.Should().Contain("token=AT");
        url.Should().Contain("refresh=RT");
        url.Should().Contain("expires_in=3600");
    }

    [Fact]
    public void BuildWalletSignInError_RoutesToPwaSignIn()
        => SocialCallbackModel.BuildWalletSignInError("no_account")
            .Should().Be("/wallet/signin?authError=no_account");
}
