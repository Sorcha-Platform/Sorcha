// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Guards issue #1264. A citizen verified their email nine minutes after their access token was
/// minted, submitted five minutes later, and was rejected because the submission carried the
/// token's stale <c>email_verified: false</c>. Verifying updates server state but cannot rewrite
/// an already-issued token, so the authoritative value has to be read live at the point of use.
/// <para>
/// This service is that live read: one batch query by claim name, so a caller resolving several
/// bindings makes one round trip and no new route is needed per attribute.
/// </para>
/// </summary>
public sealed class LivePlatformUserClaimsServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly LivePlatformUserClaimsService _sut;

    public LivePlatformUserClaimsServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new LivePlatformUserClaimsService(_db);
    }

    private async Task<PlatformUser> SeedUserAsync(
        bool emailVerified, string email = "citizen@example.test", string displayName = "Stuart Fraser")
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            EmailVerified = emailVerified,
            EmailVerifiedAt = emailVerified ? DateTimeOffset.UtcNow : null,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.PlatformUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// The exact #1264 scenario: server state says verified. The live read must say so regardless
    /// of what any previously-minted token claims.
    /// </summary>
    [Fact]
    public async Task Resolve_EmailVerifiedTrueInDb_ReturnsTrue()
    {
        var user = await SeedUserAsync(emailVerified: true);

        var claims = await _sut.ResolveAsync(user.Id, new[] { "email_verified" }, CancellationToken.None);

        claims.Should().NotBeNull();
        claims!.Should().ContainKey("email_verified").WhoseValue.Should().Be(
            "true", "the live server state is authoritative, not a token minted before verification");
    }

    /// <summary>A genuinely unverified account must still resolve false — the gate stays real.</summary>
    [Fact]
    public async Task Resolve_EmailVerifiedFalseInDb_ReturnsFalse()
    {
        var user = await SeedUserAsync(emailVerified: false);

        var claims = await _sut.ResolveAsync(user.Id, new[] { "email_verified" }, CancellationToken.None);

        claims!.Should().ContainKey("email_verified").WhoseValue.Should().Be("false");
    }

    /// <summary>One round trip resolves every requested name — the reason this is a batch query.</summary>
    [Fact]
    public async Task Resolve_MultipleNames_ReturnsAllInOneCall()
    {
        var user = await SeedUserAsync(emailVerified: true, email: "live@example.test", displayName: "Live Name");

        var claims = await _sut.ResolveAsync(
            user.Id, new[] { "email_verified", "email", "name" }, CancellationToken.None);

        claims!.Should().HaveCount(3);
        claims["email_verified"].Should().Be("true");
        claims["email"].Should().Be("live@example.test");
        claims["name"].Should().Be("Live Name");
    }

    /// <summary>
    /// Only requested names come back — the request states intent, so the response cannot become a
    /// bulk dump of everything the token would carry.
    /// </summary>
    [Fact]
    public async Task Resolve_ReturnsOnlyRequestedNames()
    {
        var user = await SeedUserAsync(emailVerified: true);

        var claims = await _sut.ResolveAsync(user.Id, new[] { "email_verified" }, CancellationToken.None);

        claims!.Should().HaveCount(1);
        claims.Should().NotContainKey("email");
        claims.Should().NotContainKey("name");
    }

    /// <summary>
    /// An unsupported name is omitted rather than guessed at, so the consumer can fail closed on it
    /// visibly instead of silently receiving a wrong value.
    /// </summary>
    [Fact]
    public async Task Resolve_UnsupportedName_IsOmitted()
    {
        var user = await SeedUserAsync(emailVerified: true);

        var claims = await _sut.ResolveAsync(
            user.Id, new[] { "email_verified", "totally_made_up_claim" }, CancellationToken.None);

        claims!.Should().ContainKey("email_verified");
        claims.Should().NotContainKey("totally_made_up_claim");
    }

    /// <summary>An unknown platform user is null — the endpoint maps that to 404.</summary>
    [Fact]
    public async Task Resolve_UnknownPlatformUser_ReturnsNull()
    {
        var claims = await _sut.ResolveAsync(
            Guid.NewGuid(), new[] { "email_verified" }, CancellationToken.None);

        claims.Should().BeNull();
    }

    /// <summary>No names requested is an empty answer, not a full dump.</summary>
    [Fact]
    public async Task Resolve_NoNames_ReturnsEmpty()
    {
        var user = await SeedUserAsync(emailVerified: true);

        var claims = await _sut.ResolveAsync(user.Id, Array.Empty<string>(), CancellationToken.None);

        claims.Should().NotBeNull().And.BeEmpty();
    }

    /// <summary>
    /// Every supported name must actually resolve. Deriving the expectation from the service's own
    /// declared set means adding a name without teaching it to resolve fails here, rather than
    /// silently omitting it at runtime and fail-closing a citizen's application.
    /// </summary>
    [Fact]
    public async Task Resolve_EverySupportedName_ActuallyResolves()
    {
        var user = await SeedUserAsync(emailVerified: true);

        var claims = await _sut.ResolveAsync(
            user.Id, LivePlatformUserClaimsService.SupportedNames.ToArray(), CancellationToken.None);

        claims!.Keys.Should().BeEquivalentTo(
            LivePlatformUserClaimsService.SupportedNames,
            "a declared-supported name that does not resolve would fail closed silently");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
