// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="SecurityChangeNotifier"/> (Feature 150 always-notify, FR-009/FR-011).
/// The notifier writes an F118 inbox entry and sends a Sorcha-branded email, and BOTH legs are
/// best-effort: a failure in either must be swallowed so it can never roll back or block the
/// underlying security operation. SQLite in-memory so the PlatformUsers lookup runs against a
/// real provider.
/// </summary>
public sealed class SecurityChangeNotifierTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly Mock<ITenantSecurityInboxWriter> _inbox = new();
    private readonly Mock<ITransactionalEmailService> _email = new();

    public SecurityChangeNotifierTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlite(_connection).Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();
    }

    private SecurityChangeNotifier CreateSut() => new(
        _inbox.Object,
        _email.Object,
        _db,
        Options.Create(new EmailSettings { BaseUrl = "https://sorcha.test" }),
        NullLogger<SecurityChangeNotifier>.Instance);

    private async Task<Guid> SeedUserAsync()
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "ada@test.com",
            DisplayName = "Ada",
        };
        _db.PlatformUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task NotifyAsync_WritesInboxEntryAndSendsEmail()
    {
        var userId = await SeedUserAsync();
        var sut = CreateSut();

        await sut.NotifyAsync(userId, SecurityChangeKind.PasskeyRemoved);

        _inbox.Verify(i => i.WriteSecurityChangeAsync(
            userId, "passkey-removed", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<InboxSeverity>(), It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(e => e.SendSecurityChangeAsync(
            It.Is<SecurityChangeDispatch>(d => d.ToEmail == "ada@test.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyAsync_EmailThrows_DoesNotPropagate()
    {
        var userId = await SeedUserAsync();
        _email
            .Setup(e => e.SendSecurityChangeAsync(It.IsAny<SecurityChangeDispatch>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));
        var sut = CreateSut();

        var act = () => sut.NotifyAsync(userId, SecurityChangeKind.PasswordChanged);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyAsync_UnknownUser_NoEmailSent_StillWritesInbox()
    {
        var sut = CreateSut();

        await sut.NotifyAsync(Guid.NewGuid(), SecurityChangeKind.TwoFactorDisabled);

        // The inbox writer is invoked regardless (it is itself fail-safe); the email leg
        // short-circuits when the user/email cannot be resolved.
        _inbox.Verify(i => i.WriteSecurityChangeAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<InboxSeverity>(), It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(e => e.SendSecurityChangeAsync(
            It.IsAny<SecurityChangeDispatch>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
