// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Sorcha.AtomicCache;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Services.Sms;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="SmsPhoneVerificationService"/> (Feature 150 US3): verifying the texted code
/// sets <c>PhoneVerifiedAt</c>; capturing a new number clears the prior verification; enabling
/// requires a verified number. Uses the real OTP service + a capturing fake <see cref="ISmsSender"/>.
/// </summary>
public sealed class SmsPhoneVerifyTests : IDisposable
{
    private sealed class CapturingSms : ISmsSender
    {
        public string? LastMessage { get; private set; }
        public Task SendAsync(string toE164, string message, CancellationToken ct = default)
        {
            LastMessage = message;
            return Task.CompletedTask;
        }
    }

    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly CapturingSms _sms = new();
    private readonly SmsPhoneVerificationService _sut;

    public SmsPhoneVerifyTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlite(_connection).Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();
        var otp = new ServerSentOtpService(new InMemoryAtomicDistributedCache(), new FakeTimeProvider());
        _sut = new SmsPhoneVerificationService(_db, otp, _sms);
    }

    private async Task<Guid> SeedUserAsync()
    {
        var u = new PlatformUser { Id = Guid.NewGuid(), Email = "a@test.com", DisplayName = "A" };
        _db.PlatformUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    private string LastCode() => Regex.Match(_sms.LastMessage ?? "", @"\d{6}").Value;

    [Fact]
    public async Task CaptureThenVerify_SetsPhoneVerifiedAt()
    {
        var userId = await SeedUserAsync();

        (await _sut.CapturePhoneAsync(userId, "+14155550123")).Should().Be(SmsFlowOutcome.Ok);
        (await _sut.VerifyPhoneAsync(userId, LastCode())).Should().Be(SmsFlowOutcome.Ok);

        var user = await _db.PlatformUsers.AsNoTracking().FirstAsync(u => u.Id == userId);
        user.PhoneNumber.Should().Be("+14155550123");
        user.PhoneVerifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task EnableBeforeVerify_ReturnsPhoneNotVerified()
    {
        var userId = await SeedUserAsync();
        await _sut.CapturePhoneAsync(userId, "+14155550123");

        (await _sut.EnableAsync(userId)).Should().Be(SmsFlowOutcome.PhoneNotVerified);
    }

    [Fact]
    public async Task CaptureNewNumber_ClearsPriorVerificationAndDisablesSms()
    {
        var userId = await SeedUserAsync();
        await _sut.CapturePhoneAsync(userId, "+14155550123");
        await _sut.VerifyPhoneAsync(userId, LastCode());
        (await _sut.EnableAsync(userId)).Should().Be(SmsFlowOutcome.Ok);

        // Changing the number must clear verification + disable SMS until re-verified.
        await _sut.CapturePhoneAsync(userId, "+14155559999");

        var user = await _db.PlatformUsers.AsNoTracking().FirstAsync(u => u.Id == userId);
        user.PhoneNumber.Should().Be("+14155559999");
        user.PhoneVerifiedAt.Should().BeNull();
        var tf = await _db.PlatformUserTwoFactors.AsNoTracking().FirstAsync(t => t.PlatformUserId == userId);
        tf.SmsOtpEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureInvalidNumber_ReturnsInvalidPhone()
    {
        var userId = await SeedUserAsync();
        (await _sut.CapturePhoneAsync(userId, "not-a-number")).Should().Be(SmsFlowOutcome.InvalidPhone);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
