// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests the strict link policy applied by
/// <see cref="PlatformUserService.ResolveOrCreateSocialUserAsync"/> for
/// feature 115. Three policy gates: returning-user pass-through with
/// display-name refresh; email-collision linking gated on both-sides
/// verified; new-user creation gated on provider-asserted verification.
/// </summary>
public class SocialLoginPolicyTests : IDisposable
{
    private readonly TenantDbContext _db;
    private readonly PlatformUserService _service;

    public SocialLoginPolicyTests()
    {
        _db = InMemoryDbContextFactory.Create();
        _service = new PlatformUserService(
            _db,
            NullLogger<PlatformUserService>.Instance,
            new ConfigurationBuilder().Build());
    }

    private static SocialAuthCallbackResult Claim(
        string provider = "Google",
        string subject = "sub-123",
        string? email = "user@example.com",
        string? displayName = "User",
        bool emailVerified = true) =>
        new(Success: true, Error: null,
            Subject: subject, Email: email, DisplayName: displayName,
            EmailVerified: emailVerified, Provider: provider);

    // --- Scenario A: returning user (provider+subject already linked) ---

    [Fact]
    public async Task ReturningUser_NoVerificationRecheck_RefreshesDisplayNameAndLastUsed()
    {
        // Arrange — existing user with prior link, current display name "Old Name"
        var existing = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            DisplayName = "Old Name",
            EmailVerified = true,
            Status = PlatformUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        };
        _db.PlatformUsers.Add(existing);
        _db.PlatformSocialLogins.Add(new PlatformSocialLogin
        {
            Id = Guid.NewGuid(),
            PlatformUserId = existing.Id,
            Provider = "Google",
            Subject = "sub-123",
            Email = "user@example.com",
            DisplayName = "Old Name",
            LinkedAt = DateTimeOffset.UtcNow.AddDays(-30),
            LastUsedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();

        // Provider returns email_verified=false this time — should be ignored
        // because the link was established under the strict gate previously.
        var claim = Claim(emailVerified: false, displayName: "New Name From Google");

        // Act
        var result = await _service.ResolveOrCreateSocialUserAsync(claim, allowCreate: true, CancellationToken.None);

        // Assert
        result.Refusal.Should().Be(SocialLoginRefusal.None);
        result.IsNew.Should().BeFalse();
        result.User.Should().NotBeNull();
        result.User!.Id.Should().Be(existing.Id);

        var refreshed = await _db.PlatformUsers.FindAsync(existing.Id);
        refreshed!.DisplayName.Should().Be("New Name From Google");

        var link = await _db.PlatformSocialLogins.SingleAsync(s => s.PlatformUserId == existing.Id);
        link.LastUsedAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task ReturningUser_EmptyDisplayName_DoesNotOverwriteExisting()
    {
        // Arrange
        var existing = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            DisplayName = "Original Name",
            EmailVerified = true,
            Status = PlatformUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _db.PlatformUsers.Add(existing);
        _db.PlatformSocialLogins.Add(new PlatformSocialLogin
        {
            Id = Guid.NewGuid(),
            PlatformUserId = existing.Id,
            Provider = "Google",
            Subject = "sub-empty",
        });
        await _db.SaveChangesAsync();

        // Act — provider returned no display name
        var result = await _service.ResolveOrCreateSocialUserAsync(
            Claim(subject: "sub-empty", displayName: null), allowCreate: true, CancellationToken.None);

        // Assert
        result.Refusal.Should().Be(SocialLoginRefusal.None);
        var refreshed = await _db.PlatformUsers.FindAsync(existing.Id);
        refreshed!.DisplayName.Should().Be("Original Name");
    }

    // --- Scenario B: email-collision (existing user, new provider link) ---

    [Fact]
    public async Task EmailCollision_BothVerified_ReturnsLinkRequired()
    {
        // Feature 168: an unconnected social identity whose verified email matches an existing verified
        // account no longer silently auto-links. Instead, a LinkRequired outcome is returned so the
        // caller can mint a link-pending token and require step-up proof from the user.

        // Arrange — verified existing user, no provider link yet
        var existing = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "alice@example.com",
            DisplayName = "Alice",
            EmailVerified = true,
            EmailVerifiedAt = DateTimeOffset.UtcNow.AddDays(-7),
            Status = PlatformUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-7),
        };
        _db.PlatformUsers.Add(existing);
        await _db.SaveChangesAsync();

        // Act — Google returns the same verified email
        var result = await _service.ResolveOrCreateSocialUserAsync(
            Claim(email: "alice@example.com", emailVerified: true, displayName: "Alice G"),
            allowCreate: true, CancellationToken.None);

        // Assert — LinkRequired, no session, no link row
        result.User.Should().BeNull("no session is issued until step-up is proven");
        result.IsNew.Should().BeFalse();
        result.Refusal.Should().Be(SocialLoginRefusal.None);

        result.LinkRequired.Should().NotBeNull();
        result.LinkRequired!.TargetAccountId.Should().Be(existing.Id);
        result.LinkRequired.Provider.Should().Be("Google");
        result.LinkRequired.Subject.Should().Be("sub-123");
        result.LinkRequired.SocialEmail.Should().Be("alice@example.com");

        var linkCount = await _db.PlatformSocialLogins.CountAsync(s => s.PlatformUserId == existing.Id);
        linkCount.Should().Be(0, "no link row is created until link-confirm succeeds");
    }

    [Fact]
    public async Task EmailCollision_ExistingUnverified_RefusesWithExistingUnverified()
    {
        // Arrange — UNverified existing user (the hijack-risk scenario)
        var existing = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "alice@example.com",
            DisplayName = "Alice",
            EmailVerified = false,            // never confirmed
            Status = PlatformUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _db.PlatformUsers.Add(existing);
        await _db.SaveChangesAsync();

        // Act — provider says verified, but existing isn't
        var result = await _service.ResolveOrCreateSocialUserAsync(
            Claim(email: "alice@example.com", emailVerified: true),
            allowCreate: true, CancellationToken.None);

        // Assert
        result.Refusal.Should().Be(SocialLoginRefusal.ExistingUnverified);
        result.User.Should().BeNull();
        result.IsNew.Should().BeFalse();

        // No link should have been created
        var linkCount = await _db.PlatformSocialLogins.CountAsync(s => s.PlatformUserId == existing.Id);
        linkCount.Should().Be(0);
    }

    [Fact]
    public async Task EmailCollision_ProviderUnverified_RefusesWithProviderUnverified()
    {
        // Arrange — VERIFIED existing user, but provider asserts unverified.
        // The refusal must point the user at the provider (where the verification
        // step is missing), NOT at their already-verified Sorcha account.
        var existing = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "alice@example.com",
            DisplayName = "Alice",
            EmailVerified = true,
            Status = PlatformUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-7),
        };
        _db.PlatformUsers.Add(existing);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ResolveOrCreateSocialUserAsync(
            Claim(email: "alice@example.com", emailVerified: false),
            allowCreate: true, CancellationToken.None);

        // Assert — provider is the unverified party, so refusal directs the user there
        result.Refusal.Should().Be(SocialLoginRefusal.ProviderUnverified);
        result.User.Should().BeNull();

        // No link should be created
        var linkCount = await _db.PlatformSocialLogins.CountAsync(s => s.PlatformUserId == existing.Id);
        linkCount.Should().Be(0);
    }

    // --- Scenario C: genuinely new user ---

    [Fact]
    public async Task NewUser_ProviderVerified_CreatesUserWithEmailVerifiedTrue()
    {
        // Act
        var result = await _service.ResolveOrCreateSocialUserAsync(
            Claim(email: "newcomer@example.com", subject: "sub-newcomer", emailVerified: true),
            allowCreate: true, CancellationToken.None);

        // Assert
        result.Refusal.Should().Be(SocialLoginRefusal.None);
        result.IsNew.Should().BeTrue();
        result.User.Should().NotBeNull();

        var created = await _db.PlatformUsers.FindAsync(result.User!.Id);
        created!.Email.Should().Be("newcomer@example.com");
        created.EmailVerified.Should().BeTrue();
        created.EmailVerifiedAt.Should().NotBeNull();

        var link = await _db.PlatformSocialLogins.SingleAsync(s => s.PlatformUserId == created.Id);
        link.Provider.Should().Be("Google");
    }

    [Fact]
    public async Task NewUser_GitHubZeroVerifiedEmails_RefusesWithProviderUnverified()
    {
        // GitHub edge case: the user has a GitHub account but no primary email
        // marked verified. ExtractGitHubClaimsAsync surfaces this as
        // EmailVerified=false with a null/empty Email. The strict-link gate
        // refuses the signup at Scenario C — no PlatformUser is created,
        // no PlatformSocialLogin row appears.
        var beforeCount = await _db.PlatformUsers.CountAsync();

        // Act — simulate a GitHub claim with no verified primary email
        var result = await _service.ResolveOrCreateSocialUserAsync(
            Claim(provider: "GitHub", subject: "github-noverify-123",
                  email: null, displayName: "octouser",
                  emailVerified: false),
            allowCreate: true, CancellationToken.None);

        // Assert
        result.Refusal.Should().Be(SocialLoginRefusal.ProviderUnverified);
        result.User.Should().BeNull();
        (await _db.PlatformUsers.CountAsync()).Should().Be(beforeCount);
        (await _db.PlatformSocialLogins.CountAsync(s => s.Subject == "github-noverify-123")).Should().Be(0);
    }

    [Fact]
    public async Task NewUser_ProviderUnverified_RefusesWithProviderUnverified_NoUserCreated()
    {
        var beforeCount = await _db.PlatformUsers.CountAsync();

        // Act
        var result = await _service.ResolveOrCreateSocialUserAsync(
            Claim(email: "untrusted@example.com", subject: "sub-untrusted", emailVerified: false),
            allowCreate: true, CancellationToken.None);

        // Assert
        result.Refusal.Should().Be(SocialLoginRefusal.ProviderUnverified);
        result.User.Should().BeNull();
        result.IsNew.Should().BeFalse();

        var afterCount = await _db.PlatformUsers.CountAsync();
        afterCount.Should().Be(beforeCount, "no user record should be created on a refused signup");
    }

    // --- Scenario D: login-only surface (allowCreate: false) ---

    [Fact]
    public async Task AllowCreateFalse_UnknownIdentity_RefusesWithNoExistingAccount_NothingPersisted()
    {
        // Arrange — no pre-existing user by provider+subject or email
        var beforeCount = await _db.PlatformUsers.CountAsync();

        // Act — wallet (login-only) surface presents a brand-new social identity
        var result = await _service.ResolveOrCreateSocialUserAsync(
            Claim(email: "newwallet@example.com", subject: "sub-wallet-new", emailVerified: true),
            allowCreate: false, CancellationToken.None);

        // Assert — refused with the login-only gate
        result.Refusal.Should().Be(SocialLoginRefusal.NoExistingAccount);
        result.User.Should().BeNull();
        result.IsNew.Should().BeFalse();

        // No PlatformUser should have been created
        var afterCount = await _db.PlatformUsers.CountAsync();
        afterCount.Should().Be(beforeCount, "login-only surface must not create a PlatformUser for an unknown identity");

        // No PlatformSocialLogin should have been created
        var linkCount = await _db.PlatformSocialLogins.CountAsync(s => s.Subject == "sub-wallet-new");
        linkCount.Should().Be(0, "no social link row should be persisted on login-only refusal");
    }

    [Fact]
    public async Task AllowCreateTrue_UnknownIdentity_CreatesUser()
    {
        // Companion assertion: the gate is the ONLY difference — allowCreate:true still creates the user.
        var beforeCount = await _db.PlatformUsers.CountAsync();

        var result = await _service.ResolveOrCreateSocialUserAsync(
            Claim(email: "newwebuser@example.com", subject: "sub-web-new", emailVerified: true),
            allowCreate: true, CancellationToken.None);

        result.Refusal.Should().Be(SocialLoginRefusal.None);
        result.User.Should().NotBeNull();
        result.IsNew.Should().BeTrue();

        var afterCount = await _db.PlatformUsers.CountAsync();
        afterCount.Should().Be(beforeCount + 1, "allowCreate:true should create a new PlatformUser");
    }

    [Fact]
    public async Task AllowCreateFalse_ExistingUserByProvider_AllowsLogin()
    {
        // Step 1 (returning user) must still work on a login-only surface.
        var existing = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "wallet-returning@example.com",
            DisplayName = "Wallet User",
            EmailVerified = true,
            Status = PlatformUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
        };
        _db.PlatformUsers.Add(existing);
        _db.PlatformSocialLogins.Add(new PlatformSocialLogin
        {
            Id = Guid.NewGuid(),
            PlatformUserId = existing.Id,
            Provider = "Google",
            Subject = "sub-wallet-returning",
            Email = "wallet-returning@example.com",
            LinkedAt = DateTimeOffset.UtcNow.AddDays(-5),
        });
        await _db.SaveChangesAsync();

        var result = await _service.ResolveOrCreateSocialUserAsync(
            Claim(subject: "sub-wallet-returning", email: "wallet-returning@example.com", emailVerified: true),
            allowCreate: false, CancellationToken.None);

        result.Refusal.Should().Be(SocialLoginRefusal.None);
        result.User.Should().NotBeNull();
        result.User!.Id.Should().Be(existing.Id);
        result.IsNew.Should().BeFalse();
    }

    [Fact]
    public void RecordRefusal_DoesNotThrow_ForKnownReasons()
    {
        // Smoke test: the refusal counter accepts both reason values without
        // throwing. Used by SocialCallback rendering and SocialLoginEndpoints
        // API path.
        SocialLoginMetrics.RecordRefusal("Google", SocialLoginRefusal.ProviderUnverified);
        SocialLoginMetrics.RecordRefusal("GitHub", SocialLoginRefusal.ExistingUnverified);

        // None is treated as a no-op
        SocialLoginMetrics.RecordRefusal("Google", SocialLoginRefusal.None);

        // Upstream-failure variant for state/code-exchange paths
        SocialLoginMetrics.RecordUpstreamFailure("Google", "code_exchange_failed");
        SocialLoginMetrics.RecordUpstreamFailure(string.Empty, "state_invalid");
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
