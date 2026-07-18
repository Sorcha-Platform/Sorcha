// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.Tenant.Service.Endpoints;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Unit tests for the passkey-signup verification-email dispatch helper
/// (<see cref="PublicPasskeyEndpoints.SendPasskeySignupVerificationEmailAsync"/>).
///
/// Passkey-first signup does not establish email ownership (unlike email+password,
/// which verifies via the emailed token, or social login, which trusts the IdP).
/// The helper must therefore send the same verification email as the password path,
/// but only for a REAL, not-yet-verified email — and it must never let an email
/// failure bubble up and fail the registration.
/// </summary>
public class PublicPasskeySignupVerificationEmailTests
{
    private static (UserIdentity identity, PlatformUser user) MakePair(string email, bool emailVerified)
    {
        var platformUser = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = "Passkey Person",
            EmailVerified = emailVerified
        };
        var identity = new UserIdentity
        {
            PlatformUserId = platformUser.Id,
            Email = email,
            DisplayName = "Passkey Person"
        };
        return (identity, platformUser);
    }

    [Fact]
    public async Task SendPasskeySignupVerificationEmailAsync_RealUnverifiedUser_SendsVerification()
    {
        var (identity, user) = MakePair("citizen@example.com", emailVerified: false);
        var svc = new Mock<IEmailVerificationService>();
        svc.Setup(s => s.GenerateAndSendVerificationAsync(It.IsAny<UserIdentity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("token");

        await PublicPasskeyEndpoints.SendPasskeySignupVerificationEmailAsync(
            identity, user, svc.Object, NullLogger.Instance, CancellationToken.None);

        svc.Verify(s => s.GenerateAndSendVerificationAsync(identity, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendPasskeySignupVerificationEmailAsync_PlaceholderEmail_DoesNotSend()
    {
        var (identity, user) = MakePair($"passkey-{Guid.NewGuid():N}@placeholder.local", emailVerified: false);
        var svc = new Mock<IEmailVerificationService>();

        await PublicPasskeyEndpoints.SendPasskeySignupVerificationEmailAsync(
            identity, user, svc.Object, NullLogger.Instance, CancellationToken.None);

        svc.Verify(s => s.GenerateAndSendVerificationAsync(It.IsAny<UserIdentity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendPasskeySignupVerificationEmailAsync_AlreadyVerifiedUser_DoesNotSend()
    {
        var (identity, user) = MakePair("verified@example.com", emailVerified: true);
        var svc = new Mock<IEmailVerificationService>();

        await PublicPasskeyEndpoints.SendPasskeySignupVerificationEmailAsync(
            identity, user, svc.Object, NullLogger.Instance, CancellationToken.None);

        svc.Verify(s => s.GenerateAndSendVerificationAsync(It.IsAny<UserIdentity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendPasskeySignupVerificationEmailAsync_ServiceThrows_SwallowsAndDoesNotRethrow()
    {
        var (identity, user) = MakePair("citizen@example.com", emailVerified: false);
        var svc = new Mock<IEmailVerificationService>();
        svc.Setup(s => s.GenerateAndSendVerificationAsync(It.IsAny<UserIdentity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        var act = async () => await PublicPasskeyEndpoints.SendPasskeySignupVerificationEmailAsync(
            identity, user, svc.Object, NullLogger.Instance, CancellationToken.None);

        await act.Should().NotThrowAsync();
        svc.Verify(s => s.GenerateAndSendVerificationAsync(identity, It.IsAny<CancellationToken>()), Times.Once);
    }
}
