// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Models;

/// <summary>
/// Property-level tests for <see cref="SocialAuthCallbackResult"/>'s
/// <c>EmailVerified</c> field added in feature 115. Behaviour-based
/// coverage of the <c>email_verified</c> claim parsing lives in the
/// <c>SocialLoginService</c> integration tests; these tests pin the DTO
/// shape itself (presence, default value, equality semantics) so any
/// accidental signature change is caught.
/// </summary>
public class SocialAuthCallbackResultTests
{
    [Fact]
    public void Construction_WithEmailVerifiedTrue_SetsField()
    {
        var result = new SocialAuthCallbackResult(
            Success: true, Error: null,
            Subject: "sub-1", Email: "x@y.com", DisplayName: "X",
            EmailVerified: true, Provider: "Google");

        result.EmailVerified.Should().BeTrue();
    }

    [Fact]
    public void Construction_WithEmailVerifiedFalse_SetsField()
    {
        var result = new SocialAuthCallbackResult(
            Success: true, Error: null,
            Subject: "sub-1", Email: "x@y.com", DisplayName: "X",
            EmailVerified: false, Provider: "Google");

        result.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public void Equality_DistinguishesByEmailVerified()
    {
        var verified = new SocialAuthCallbackResult(true, null, "sub", "x@y.com", "X", true, "Google");
        var unverified = new SocialAuthCallbackResult(true, null, "sub", "x@y.com", "X", false, "Google");

        verified.Should().NotBe(unverified);
    }

    [Fact]
    public void FailureResult_WithEmailVerifiedFalse_IsTheConventionalShape()
    {
        // Failure paths in SocialLoginService construct the result with
        // emailVerified=false. Pin that shape so callers don't have to
        // null-guard a separate field.
        var result = new SocialAuthCallbackResult(
            Success: false, Error: "Token exchange failed.",
            Subject: null, Email: null, DisplayName: null,
            EmailVerified: false, Provider: "Google");

        result.Success.Should().BeFalse();
        result.EmailVerified.Should().BeFalse();
    }
}
