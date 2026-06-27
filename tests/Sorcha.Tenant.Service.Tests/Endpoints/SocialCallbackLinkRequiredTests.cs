// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Integration tests for the Feature 168 LinkRequired branch of the social callback endpoint.
/// T017: verified email collision → outcome=LinkRequired, no JWT, no link row.
/// T024: no-match (new user) → new account created normally.
/// T025: already-linked → direct sign-in, no LinkRequired branch.
/// </summary>
public class SocialCallbackLinkRequiredTests
    : IClassFixture<SocialCallbackLinkRequiredTests.SocialCallbackTestFactory>
{
    /// <summary>
    /// Derived factory that overrides <see cref="ISocialLoginService"/> with a
    /// configurable mock so tests can drive <c>ExchangeCodeAsync</c> outcomes
    /// without wiring up real OAuth provider state.
    /// </summary>
    public sealed class SocialCallbackTestFactory : TenantServiceWebApplicationFactory
    {
        public Mock<ISocialLoginService> SocialLoginServiceMock { get; } = new(MockBehavior.Strict);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISocialLoginService>();
                services.AddSingleton(SocialLoginServiceMock.Object);
            });
        }
    }

    private readonly SocialCallbackTestFactory _factory;

    public SocialCallbackLinkRequiredTests(SocialCallbackTestFactory factory)
    {
        _factory = factory;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void SetupExchange(SocialAuthCallbackResult result)
    {
        _factory.SocialLoginServiceMock
            .Setup(s => s.ExchangeCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    private static SocialAuthCallbackResult SuccessResult(
        string email, string subject, bool emailVerified = true,
        string provider = "google", string? displayName = "Test User",
        string? surface = null) =>
        new(Success: true, Error: null,
            Subject: subject, Email: email, DisplayName: displayName,
            EmailVerified: emailVerified, Provider: provider, Surface: surface);

    private async Task<Guid> SeedVerifiedUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = "Existing User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
            EmailVerified = true,
            EmailVerifiedAt = DateTimeOffset.UtcNow.AddDays(-7),
            Status = PlatformUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-7)
        };
        db.PlatformUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task SeedLinkedUserAsync(
        Guid platformUserId, string provider, string subject, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        db.PlatformSocialLogins.Add(new PlatformSocialLogin
        {
            Id = Guid.NewGuid(),
            PlatformUserId = platformUserId,
            Provider = provider,
            Subject = subject,
            Email = email,
            LinkedAt = DateTimeOffset.UtcNow.AddDays(-1),
            LastUsedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
    }

    // ── T017: verified email collision → LinkRequired ────────────────────────

    [Fact]
    public async Task Callback_VerifiedEmailMatchesExistingVerifiedAccount_ReturnsLinkRequired()
    {
        // Arrange — existing verified user; social provider returns same verified email but
        // no prior link row. Feature 168 replaces the old silent auto-link with a LinkRequired
        // outcome so the user must prove ownership via step-up before the link is persisted.
        await _factory.SeedTestDataAsync();
        const string email = "alice-link-required@example.com";
        var existingUserId = await SeedVerifiedUserAsync(email);

        SetupExchange(SuccessResult(email, subject: "google-sub-alice", emailVerified: true));

        using var client = _factory.CreateUnauthenticatedClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/social/callback",
            new { provider = "google", code = "auth-code", state = "state-token" });

        // Assert — 200 with LinkRequired outcome and a link-pending token
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("outcome").GetString().Should().Be("LinkRequired",
            "Feature 168 requires step-up proof before creating any link row");
        payload.GetProperty("linkPendingToken").GetString().Should().NotBeNullOrEmpty(
            "client needs the token to drive the challenge/confirm flow");

        // No JWT issued
        payload.TryGetProperty("token", out _).Should().BeFalse(
            "no session token is returned until the user completes step-up");
        payload.TryGetProperty("accessToken", out _).Should().BeFalse();

        // No PlatformSocialLogin row created
        var linkCount = await db.PlatformSocialLogins.CountAsync(l => l.PlatformUserId == existingUserId);
        linkCount.Should().Be(0, "no link row is created until link-confirm succeeds");
    }

    [Fact]
    public async Task Callback_VerifiedEmailCollision_LinkPendingTokenTargetsCorrectAccount()
    {
        // The link-pending token must embed the correct TargetAccountId so the
        // step-up challenge is scoped to the right user.
        await _factory.SeedTestDataAsync();
        const string email = "bob-link-target@example.com";
        var existingUserId = await SeedVerifiedUserAsync(email);

        SetupExchange(SuccessResult(email, subject: "google-sub-bob", emailVerified: true));

        using var client = _factory.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/auth/social/callback",
            new { provider = "google", code = "auth-code", state = "state-token" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("outcome").GetString().Should().Be("LinkRequired");

        // Verify the token by resolving ILinkPendingTokenService from DI
        using var scope = _factory.Services.CreateScope();
        var tokenSvc = scope.ServiceProvider.GetRequiredService<ILinkPendingTokenService>();
        var raw = payload.GetProperty("linkPendingToken").GetString()!;
        tokenSvc.TryVerify(raw, out var linkToken, out _).Should().BeTrue();
        linkToken.TargetAccountId.Should().Be(existingUserId,
            "the token must target the existing account whose email was matched");
        linkToken.Provider.Should().Be("google");
        linkToken.Subject.Should().Be("google-sub-bob");
        linkToken.SocialEmail.Should().Be(email);
    }

    // ── Bug regression: wallet surface (surface=wallet in state) ────────────

    /// <summary>
    /// Regression for Bug 1 (F168): JSON API callback was hardcoded to allowCreate: true,
    /// ignoring the wallet surface. A wallet-originated callback for an unknown social
    /// identity must return a 400 NoExistingAccount refusal — not silently create an account.
    /// </summary>
    [Fact]
    public async Task Callback_WalletSurface_UnknownIdentity_RefusesWithoutCreatingAccount()
    {
        await _factory.SeedTestDataAsync();
        const string email = "newcomer-wallet-bug1@example.com";

        // Surface="wallet" in the callback result mirrors what the state-cache stores when
        // the citizen PWA initiates the OAuth flow with surface=wallet.
        SetupExchange(SuccessResult(email, subject: "google-sub-wallet-new", emailVerified: true,
            surface: "wallet"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var beforeCount = await db.PlatformUsers.CountAsync();

        using var client = _factory.CreateUnauthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/auth/social/callback",
            new { provider = "google", code = "auth-code", state = "state-token" });

        // Wallet surface is login-only — unknown identity must be refused, not created.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "wallet social callback must refuse unknown identities (allowCreate: false)");

        var afterCount = await db.PlatformUsers.CountAsync();
        afterCount.Should().Be(beforeCount,
            "no PlatformUser should be created on a wallet-surface callback for an unknown identity");
    }

    /// <summary>
    /// Regression for Bug 2 (F168): JSON API callback defaulted to Platform-tier token regardless
    /// of surface. A wallet-originated callback for a returning (already-linked) user must issue a
    /// Consumer-tier JWT (aud=test:consumer), not a Platform-tier one.
    /// </summary>
    [Fact]
    public async Task Callback_WalletSurface_AlreadyLinkedIdentity_IssuesConsumerTierToken()
    {
        await _factory.SeedTestDataAsync();
        const string email = "returning-wallet-bug2@example.com";
        const string subject = "google-sub-wallet-returning";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var platformUser = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = "Wallet Returning User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
            EmailVerified = true,
            Status = PlatformUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };
        db.PlatformUsers.Add(platformUser);

        var userIdentity = new UserIdentity
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = "Wallet Returning User",
            PlatformUserId = platformUser.Id,
            Status = IdentityStatus.Active,
            Roles = [UserRole.Consumer],
            OrganizationId = TestDataSeeder.PublicOrganizationId,
            ProfileCompleted = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };
        db.UserIdentities.Add(userIdentity);
        await db.SaveChangesAsync();

        await SeedLinkedUserAsync(platformUser.Id, "google", subject, email);

        SetupExchange(SuccessResult(email, subject, emailVerified: true, provider: "google",
            surface: "wallet"));

        using var client = _factory.CreateUnauthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/auth/social/callback",
            new { provider = "google", code = "auth-code", state = "state-token" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("outcome", out _).Should().BeFalse("should be a direct JWT response");

        var accessToken = payload.GetProperty("access_token").GetString()!;
        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        decoded.Audiences.Should().ContainSingle().Which.Should().Be("test:consumer",
            "wallet-surface social sign-in must issue a Consumer-tier token, not Platform-tier");
    }

    // ── T024: no-match → new account (unchanged behaviour) ──────────────────

    [Fact]
    public async Task Callback_NewEmail_CreatesUserNormallyWithoutLinkRequired()
    {
        // T024: no existing account matches → create a new PlatformUser as before.
        // The LinkRequired branch must NOT fire for genuinely new identities.
        await _factory.SeedTestDataAsync();
        const string email = "newcomer-t024@example.com";

        SetupExchange(SuccessResult(email, subject: "google-sub-newcomer", emailVerified: true));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var beforeCount = await db.PlatformUsers.CountAsync();

        using var client = _factory.CreateUnauthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/auth/social/callback",
            new { provider = "google", code = "auth-code", state = "state-token" });

        // Assert — new user created, JWT issued, no LinkRequired
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("outcome", out var outcomeProp).Should().BeFalse(
            "a new user should get a JWT response, not a LinkRequired response");

        var afterCount = await db.PlatformUsers.CountAsync();
        afterCount.Should().Be(beforeCount + 1, "a new PlatformUser should be created");

        var newUser = await db.PlatformUsers.FirstOrDefaultAsync(u => u.Email == email);
        newUser.Should().NotBeNull();

        var linkRow = await db.PlatformSocialLogins.FirstOrDefaultAsync(
            l => l.Subject == "google-sub-newcomer");
        linkRow.Should().NotBeNull("a PlatformSocialLogin row is created for new users immediately");
    }

    // ── T025: already-linked → direct sign-in (unchanged behaviour) ─────────

    [Fact]
    public async Task Callback_AlreadyLinkedIdentity_SignsInDirectlyWithoutLinkRequired()
    {
        // T025: provider+subject already has a PlatformSocialLogin row → returning-user
        // path. No LinkRequired should be generated — the user is already linked.
        await _factory.SeedTestDataAsync();
        const string email = "returning-t025@example.com";
        const string subject = "google-sub-returning";

        // Seed an existing user with a linked social identity
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var platformUser = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = "Returning User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
            EmailVerified = true,
            Status = PlatformUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };
        db.PlatformUsers.Add(platformUser);

        // The UserIdentity in the public org is required for JWT issuance
        var userIdentity = new UserIdentity
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = "Returning User",
            PlatformUserId = platformUser.Id,
            Status = IdentityStatus.Active,
            Roles = [UserRole.Consumer],
            OrganizationId = TestDataSeeder.PublicOrganizationId,
            ProfileCompleted = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };
        db.UserIdentities.Add(userIdentity);
        await db.SaveChangesAsync();

        await SeedLinkedUserAsync(platformUser.Id, "google", subject, email);

        SetupExchange(SuccessResult(email, subject, emailVerified: true, provider: "google"));

        using var client = _factory.CreateUnauthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/auth/social/callback",
            new { provider = "google", code = "auth-code", state = "state-token" });

        // Assert — direct sign-in JWT response, no LinkRequired
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("outcome", out var outcomeProp).Should().BeFalse(
            "a returning (already-linked) user should get a JWT response, not LinkRequired");

        // LastUsedAt should be refreshed
        var link = await db.PlatformSocialLogins
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.PlatformUserId == platformUser.Id && l.Subject == subject);
        link.Should().NotBeNull();
        link!.LastUsedAt.Should().NotBeNull();
        link.LastUsedAt!.Value.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-1));
    }
}
