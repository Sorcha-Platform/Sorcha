// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="EmailVerificationService"/> after the email-sweep refactor:
/// (a) GenerateAndSendVerificationAsync dispatches a VerifyEmailDispatch through the
/// templated facade, not a plaintext-token SendAsync; (b) VerifyTokenAsync on success
/// invokes WelcomeEmailDispatcher.SendIfPendingAsync so the welcome fires exactly
/// once per user on the email-password signup path.
/// </summary>
public class EmailVerificationServiceTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly Mock<ITransactionalEmailService> _transactional = new();
    private readonly ILogger<EmailVerificationService> _logger = NullLogger<EmailVerificationService>.Instance;

    public EmailVerificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"EmailVerify_{Guid.NewGuid():N}")
            .Options;
        _dbContext = new TenantDbContext(options);
    }

    private EmailVerificationService CreateService()
    {
        var dispatcher = new WelcomeEmailDispatcher(
            _dbContext, _transactional.Object, NullLogger<WelcomeEmailDispatcher>.Instance);
        var settings = Options.Create(new EmailSettings { BaseUrl = "https://sorcha.io" });
        return new EmailVerificationService(
            _dbContext, _transactional.Object, dispatcher, settings, _logger);
    }

    private async Task<(PlatformUser platformUser, UserIdentity user)> SeedUserAsync(
        bool emailVerified = false, DateTimeOffset? welcomeSentAt = null)
    {
        var platformUser = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "user@test.com",
            DisplayName = "Stuart Fraser",
            EmailVerified = emailVerified,
            WelcomeSentAt = welcomeSentAt,
        };
        _dbContext.PlatformUsers.Add(platformUser);

        var userIdentity = new UserIdentity
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            PlatformUserId = platformUser.Id,
            Email = "user@test.com",
            DisplayName = "Stuart Fraser",
            Status = IdentityStatus.Active,
        };
        _dbContext.UserIdentities.Add(userIdentity);

        await _dbContext.SaveChangesAsync();
        return (platformUser, userIdentity);
    }

    [Fact]
    public async Task GenerateAndSendVerificationAsync_DispatchesThroughFacade_WithClickableLink()
    {
        var (_, user) = await SeedUserAsync();
        var service = CreateService();

        var returnedToken = await service.GenerateAndSendVerificationAsync(user, CancellationToken.None);

        returnedToken.Should().NotBeNullOrEmpty();

        _transactional.Verify(t => t.SendVerificationAsync(
            It.Is<VerifyEmailDispatch>(d =>
                d.ToEmail == "user@test.com" &&
                d.DisplayName == "Stuart Fraser" &&
                d.VerifyUrl.Contains("https://sorcha.io/auth/verify-email?token=") &&
                d.VerifyUrl.Contains(returnedToken) &&
                d.ExpiresInHours == 24),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyTokenAsync_ValidToken_MarksVerifiedAndFiresWelcome()
    {
        var (platformUser, user) = await SeedUserAsync();
        var service = CreateService();

        // Generate + send a verification token
        var token = await service.GenerateAndSendVerificationAsync(user, CancellationToken.None);

        // Exercise verification
        var (success, error) = await service.VerifyTokenAsync(token, CancellationToken.None);

        success.Should().BeTrue();
        error.Should().BeNull();

        var reloaded = await _dbContext.PlatformUsers.AsNoTracking().FirstAsync(u => u.Id == platformUser.Id);
        reloaded.EmailVerified.Should().BeTrue();
        reloaded.EmailVerifiedAt.Should().NotBeNull();
        reloaded.WelcomeSentAt.Should().NotBeNull(
            "verify success MUST trigger the welcome dispatcher, which sets WelcomeSentAt on success");

        _transactional.Verify(t => t.SendWelcomeAsync(
            It.Is<WelcomeDispatchContext>(c => c.User.Id == platformUser.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyTokenAsync_WelcomeAlreadySent_DoesNotDispatchWelcomeAgain()
    {
        var alreadyWelcomed = DateTimeOffset.UtcNow.AddDays(-5);
        var (platformUser, user) = await SeedUserAsync(welcomeSentAt: alreadyWelcomed);
        var service = CreateService();
        var token = await service.GenerateAndSendVerificationAsync(user, CancellationToken.None);

        var (success, _) = await service.VerifyTokenAsync(token, CancellationToken.None);

        success.Should().BeTrue();

        // Welcome should not be re-sent; the pre-existing WelcomeSentAt stays put.
        var reloaded = await _dbContext.PlatformUsers.AsNoTracking().FirstAsync(u => u.Id == platformUser.Id);
        reloaded.WelcomeSentAt.Should().Be(alreadyWelcomed);

        _transactional.Verify(t => t.SendWelcomeAsync(
            It.IsAny<WelcomeDispatchContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VerifyTokenAsync_InvalidToken_ReturnsErrorAndDoesNotDispatch()
    {
        var service = CreateService();

        var (success, error) = await service.VerifyTokenAsync("bogus-token", CancellationToken.None);

        success.Should().BeFalse();
        error.Should().NotBeNull();
        _transactional.Verify(t => t.SendWelcomeAsync(
            It.IsAny<WelcomeDispatchContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public void Dispose() => _dbContext.Dispose();
}
