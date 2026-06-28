// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Requests;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Integration tests for the step-up social link confirm and challenge endpoints (Feature 168).
///
/// T018 (happy-path initiate → verify → confirm) is partially covered here:
/// the initiate/verify paths exercise the challenge ladder; the full confirm
/// path (including <c>TryConsumeAsync</c>) is gated by the EF Core InMemory
/// provider not supporting <c>ExecuteUpdateAsync</c>. The rejection matrix
/// (T019) and expiry path (T023) are fully exercised — they all reject before
/// the <c>TryConsumeAsync</c> call.
/// </summary>
public class SocialLinkConfirmTests : IClassFixture<TenantServiceWebApplicationFactory>
{
    private readonly TenantServiceWebApplicationFactory _factory;

    public SocialLinkConfirmTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private string MintValidToken(Guid targetAccountId, DateTimeOffset? expiresAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ILinkPendingTokenService>();
        return svc.Mint(new LinkPendingToken(
            Provider: "google",
            Subject: "sub-f168",
            SocialEmail: "social@example.com",
            DisplayName: "Social User",
            TargetAccountId: targetAccountId,
            ExpiresAt: expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    private async Task<(Guid PlatformUserId, Guid UserIdentityId)> SeedTargetUserAsync(
        string email = "target@example.com")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        var platformUser = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = "Target User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
            EmailVerified = true,
            Status = PlatformUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.PlatformUsers.Add(platformUser);

        // UserIdentity in the PUBLIC org — required by ResolveChallengeContextAsync and
        // link-confirm session issuance (both look up the identity in WellKnownIds.PublicOrgId).
        var userIdentity = new UserIdentity
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = "Target User",
            PlatformUserId = platformUser.Id,
            Status = IdentityStatus.Active,
            Roles = [UserRole.Consumer],
            OrganizationId = TestDataSeeder.PublicOrganizationId,
            ProfileCompleted = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.UserIdentities.Add(userIdentity);
        await db.SaveChangesAsync();

        return (platformUser.Id, userIdentity.Id);
    }

    private static async Task InsertChallengeTokenAsync(
        TenantDbContext db,
        string rawToken,
        Guid platformUserId,
        ScopedOperation operation,
        DateTimeOffset? expiresAt = null)
    {
        var hash = ComputeSha256Hex(rawToken);
        db.AuthChallengeTokens.Add(new AuthChallengeToken
        {
            Id = Guid.NewGuid(),
            PlatformUserId = platformUserId,
            TokenHash = hash,
            Method = ChallengeMethod.Password,
            ScopedOperation = operation,
            IssuedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(5),
        });
        await db.SaveChangesAsync();
    }

    private static string ComputeSha256Hex(string raw)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(raw), hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ── T018: challenge initiate / verify ────────────────────────────────────

    [Fact]
    public async Task Initiate_WithTotpEnrolledUser_OffersTotpMethod()
    {
        // Arrange — seed user + enabled TOTP config so the ladder returns TOTP (Strong ≥ Strong floor)
        await _factory.SeedTestDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var (platformUserId, userIdentityId) = await SeedTargetUserAsync("totp-user@example.com");

        db.TotpConfigurations.Add(new TotpConfiguration
        {
            Id = Guid.NewGuid(),
            UserId = userIdentityId,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var linkToken = MintValidToken(platformUserId);
        using var client = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/auth/social/link/challenge/initiate",
            new SocialLinkChallengeInitiateRequest(linkToken, null));

        // Assert — TOTP offered (Strong satisfies the Strong floor for LinkSocial)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ChallengeInitiateResponse>();
        payload.Should().NotBeNull();
        payload!.Method.Should().Be(ChallengeMethod.Totp,
            "TOTP (Strong) is the strongest available method and satisfies the Strong floor; "
            + "bare password (Basic) does not");
    }

    [Fact]
    public async Task Initiate_PasswordOnlyUser_Returns400NoMethodAvailable()
    {
        // Arrange — password-only user: no TOTP, no passkey, no social → nothing at Strong or above.
        // The AssurancePolicy floor for LinkSocial is Strong; the PickForFloor ladder returns
        // NoMethodAvailable, which the initiate endpoint surfaces as 400.
        await _factory.SeedTestDataAsync();
        var (platformUserId, _) = await SeedTargetUserAsync("password-only@example.com");
        var linkToken = MintValidToken(platformUserId);
        using var client = _factory.CreateUnauthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/auth/social/link/challenge/initiate",
            new SocialLinkChallengeInitiateRequest(linkToken, null));

        // Assert — 400 NoMethodAvailable
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "password-only accounts have no method at Strong tier or above; "
            + "the user must enrol TOTP or a passkey before linking a social identity (FR-010 §4)");
    }

    [Fact]
    public async Task Verify_BarePasswordForTotpEnrolledUser_Returns403ProofTierInsufficient()
    {
        // Arrange — user has TOTP enrolled; submitting password at verify violates the Strong floor.
        // AuthChallengeService.VerifyAsync checks CanProofSatisfy(Password, LinkSocial) →
        // Basic < Strong → ProofTierInsufficient → 403.
        await _factory.SeedTestDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var (platformUserId, userIdentityId) = await SeedTargetUserAsync("totp-verify@example.com");

        db.TotpConfigurations.Add(new TotpConfiguration
        {
            Id = Guid.NewGuid(),
            UserId = userIdentityId,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var linkToken = MintValidToken(platformUserId);
        using var client = _factory.CreateUnauthenticatedClient();
        var proof = JsonDocument.Parse("""{"password":"Password123!"}""").RootElement;

        // Act — submit password proof even though TOTP is enrolled and would be required
        var response = await client.PostAsJsonAsync(
            "/api/auth/social/link/challenge/verify",
            new SocialLinkChallengeVerifyRequest(linkToken, ChallengeMethod.Password, proof));

        // Assert — 403 ProofTierInsufficient: password (Basic) < Strong floor for LinkSocial (FR-010)
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Initiate_ExpiredLinkPendingToken_Returns401()
    {
        await _factory.SeedTestDataAsync();
        var (platformUserId, _) = await SeedTargetUserAsync("expire-initiate@example.com");
        var expiredToken = MintValidToken(platformUserId, DateTimeOffset.UtcNow.AddMinutes(-1));
        using var client = _factory.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/social/link/challenge/initiate",
            new SocialLinkChallengeInitiateRequest(expiredToken, null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── T019: link-confirm rejection matrix ─────────────────────────────────

    [Fact]
    public async Task Confirm_NoXAuthChallengeHeader_Returns401()
    {
        // T019 case (a): no challenge header → 401 (checked at step 2, after link-pending verified)
        await _factory.SeedTestDataAsync();
        var (platformUserId, _) = await SeedTargetUserAsync("no-challenge@example.com");
        var linkToken = MintValidToken(platformUserId);
        using var client = _factory.CreateUnauthenticatedClient();

        // No X-Auth-Challenge header added
        var response = await client.PostAsJsonAsync(
            "/api/auth/social/link/confirm",
            new LinkConfirmRequest(linkToken));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Confirm_ExpiredLinkPendingToken_Returns401()
    {
        // T019 case (b): expired link-pending token → 401 at step 1
        await _factory.SeedTestDataAsync();
        var (platformUserId, _) = await SeedTargetUserAsync("expired-token@example.com");
        var expiredToken = MintValidToken(platformUserId, DateTimeOffset.UtcNow.AddMinutes(-1));
        using var client = _factory.CreateUnauthenticatedClient();

        client.DefaultRequestHeaders.Add("X-Auth-Challenge", "ch_placeholder_does_not_matter");
        var response = await client.PostAsJsonAsync(
            "/api/auth/social/link/confirm",
            new LinkConfirmRequest(expiredToken));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Confirm_TamperedLinkPendingToken_Returns401()
    {
        // T019 case (e): HMAC-tampered token → 401 at step 1 (constant-time signature check fails)
        await _factory.SeedTestDataAsync();
        var (platformUserId, _) = await SeedTargetUserAsync("tampered-token@example.com");
        var validToken = MintValidToken(platformUserId);

        // Flip one character in the HMAC segment (last pipe-delimited part)
        var parts = validToken.Split('|');
        var lastSegment = parts[^1];
        parts[^1] = lastSegment[..^1] + (lastSegment[^1] == 'a' ? 'b' : 'a');
        var tamperedToken = string.Join('|', parts);

        using var client = _factory.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Add("X-Auth-Challenge", "ch_placeholder");
        var response = await client.PostAsJsonAsync(
            "/api/auth/social/link/confirm",
            new LinkConfirmRequest(tamperedToken));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Confirm_ChallengeForWrongOperation_Returns403()
    {
        // T019 case (c): challenge is scoped to ChangePassword, not LinkSocial →
        // step 3b detects the mismatch and returns 403 (wrong operation, per contract table).
        await _factory.SeedTestDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var (platformUserId, _) = await SeedTargetUserAsync("wrong-op@example.com");

        const string rawChallenge = "ch_wrong_op_unique_token_f168_test_1";
        await InsertChallengeTokenAsync(db, rawChallenge, platformUserId, ScopedOperation.ChangePassword);

        var linkToken = MintValidToken(platformUserId);
        using var client = _factory.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Add("X-Auth-Challenge", rawChallenge);

        var response = await client.PostAsJsonAsync(
            "/api/auth/social/link/confirm",
            new LinkConfirmRequest(linkToken));

        // Wrong operation → 403 (credentials are valid but operation scope is wrong)
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var linkCount = await db.PlatformSocialLogins.CountAsync(l => l.PlatformUserId == platformUserId);
        linkCount.Should().Be(0, "no link row is created when the challenge operation does not match");
    }

    [Fact]
    public async Task Confirm_ChallengeBoundToDifferentAccount_Returns403()
    {
        // T019 case (d): challenge is bound to a different PlatformUser → 403 at step 4.
        // 403 rather than 401 because the credentials are valid but the account assertion fails.
        await _factory.SeedTestDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var (targetId, _) = await SeedTargetUserAsync("correct-account@example.com");
        var (differentId, _) = await SeedTargetUserAsync("attacker-account@example.com");

        // Challenge bound to differentId, but link token targets targetId
        const string rawChallenge = "ch_wrong_account_unique_token_f168_test_2";
        await InsertChallengeTokenAsync(db, rawChallenge, differentId, ScopedOperation.LinkSocial);

        var linkToken = MintValidToken(targetId);
        using var client = _factory.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Add("X-Auth-Challenge", rawChallenge);

        var response = await client.PostAsJsonAsync(
            "/api/auth/social/link/confirm",
            new LinkConfirmRequest(linkToken));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var linkCount = await db.PlatformSocialLogins.CountAsync(
            l => l.PlatformUserId == targetId || l.PlatformUserId == differentId);
        linkCount.Should().Be(0, "no link created when challenge account ≠ token target account");
    }

    [Fact]
    public async Task Confirm_UnknownChallengeToken_Returns401()
    {
        // Challenge token not in the DB (never issued, already consumed, or pruned) → 401 at step 3.
        await _factory.SeedTestDataAsync();
        var (platformUserId, _) = await SeedTargetUserAsync("unknown-challenge@example.com");
        var linkToken = MintValidToken(platformUserId);
        using var client = _factory.CreateUnauthenticatedClient();

        client.DefaultRequestHeaders.Add("X-Auth-Challenge", "ch_no_such_token_in_db_f168");
        var response = await client.PostAsJsonAsync(
            "/api/auth/social/link/confirm",
            new LinkConfirmRequest(linkToken));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Confirm_ExpiredChallengeToken_Returns401()
    {
        // T019 supplemental: challenge token exists but is expired → 401 at step 3c.
        await _factory.SeedTestDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var (platformUserId, _) = await SeedTargetUserAsync("expired-challenge@example.com");

        const string rawChallenge = "ch_expired_challenge_token_f168_test_3";
        await InsertChallengeTokenAsync(
            db, rawChallenge, platformUserId, ScopedOperation.LinkSocial,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var linkToken = MintValidToken(platformUserId);
        using var client = _factory.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Add("X-Auth-Challenge", rawChallenge);

        var response = await client.PostAsJsonAsync(
            "/api/auth/social/link/confirm",
            new LinkConfirmRequest(linkToken));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── T023: abandon / expiry path ──────────────────────────────────────────

    [Fact]
    public async Task Confirm_AfterLinkPendingTokenExpiry_Returns401()
    {
        // T023: confirming after the 5-minute link-pending window closes → 401.
        // The valid challenge token is present but the link-pending token already expired.
        await _factory.SeedTestDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var (platformUserId, _) = await SeedTargetUserAsync("expire-confirm@example.com");

        var expiredLinkToken = MintValidToken(platformUserId, DateTimeOffset.UtcNow.AddSeconds(-1));

        const string rawChallenge = "ch_valid_challenge_expired_link_f168_t023";
        await InsertChallengeTokenAsync(db, rawChallenge, platformUserId, ScopedOperation.LinkSocial);

        using var client = _factory.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Add("X-Auth-Challenge", rawChallenge);

        var response = await client.PostAsJsonAsync(
            "/api/auth/social/link/confirm",
            new LinkConfirmRequest(expiredLinkToken));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var linkCount = await db.PlatformSocialLogins.CountAsync(l => l.PlatformUserId == platformUserId);
        linkCount.Should().Be(0, "no link row created when the link-pending token is expired");
    }

    [Fact]
    public async Task MintAndAbandon_LeavesNoPlatformSocialLoginRow()
    {
        // T023: obtaining a link-pending token without ever calling confirm → no side-effects.
        // The token is stateless (HMAC-only), so simply not calling confirm leaves no DB state.
        await _factory.SeedTestDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var (platformUserId, _) = await SeedTargetUserAsync("abandon-flow@example.com");

        // Just mint — never call initiate/verify/confirm
        _ = MintValidToken(platformUserId);

        var linkCount = await db.PlatformSocialLogins.CountAsync(l => l.PlatformUserId == platformUserId);
        linkCount.Should().Be(0, "no DB state is created by minting a link-pending token; it is stateless");
    }
}
