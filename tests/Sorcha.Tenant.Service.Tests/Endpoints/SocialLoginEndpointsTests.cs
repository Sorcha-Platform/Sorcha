// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Endpoints;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Regression-guard tests for <see cref="SocialLoginEndpoints"/>. The full
/// initiate/callback handlers are exercised by the manual smoke walkthrough
/// in <c>specs/115-social-signup/quickstart.md</c>; this fast-unit class
/// pins the contract that providers depend on so a future refactor cannot
/// silently re-break the redirect URI (feature 115 FR-021).
/// </summary>
public class SocialLoginEndpointsTests
{
    [Fact]
    public void CallbackPath_MatchesRazorPageRoute()
    {
        // The Razor page at Pages/Auth/SocialCallback.cshtml declares
        // @page "/auth/social/callback" — provider OAuth apps are registered
        // with this exact path. Changing the constant requires updating every
        // OAuth-app registration in every environment, so it must not move
        // without a coordinated rollout. See docs/guides/SOCIAL-LOGIN-SETUP.md.
        SocialLoginEndpoints.CallbackPath.Should().Be("/auth/social/callback");
    }

    [Fact]
    public void CallbackPath_DoesNotUseDefunctApiRedirectPath()
    {
        // The original (broken) value was /api/auth/social/callback-redirect,
        // which had no handler — providers 404'd users after consent. Pin the
        // negative case so the regression cannot recur even by accident.
        SocialLoginEndpoints.CallbackPath.Should().NotContain("callback-redirect");
        SocialLoginEndpoints.CallbackPath.Should().NotStartWith("/api/");
    }
}
