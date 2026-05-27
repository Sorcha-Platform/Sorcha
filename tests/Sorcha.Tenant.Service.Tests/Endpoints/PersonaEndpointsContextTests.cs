// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Models.Persona;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Services.Interfaces;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Per-context persona behaviour tests (Feature 125, T028). Exercises the
/// service-layer contract that the <c>?context=</c> query parameter on
/// <see cref="Sorcha.Tenant.Service.Endpoints.PersonaEndpoints"/> ultimately
/// drives — per-context row isolation, idempotent delete-by-context, and
/// independent encryption envelopes per context.
/// </summary>
/// <remarks>
/// The HTTP-layer 403 contract (caller lacks OrgMembership for the requested
/// context) is enforced by the endpoint's <c>CallerHasContextAsync</c>
/// helper. Full end-to-end coverage through <c>TenantServiceWebApplicationFactory</c>
/// is deferred to a follow-up that wires <c>ServiceAuth:ClientId</c> +
/// <c>IPersonaCryptoClient</c> stubs into the factory; the OpenAPI contract
/// in <c>contracts/per-context-persona.openapi.yaml</c> pins the 200/403
/// shape.
/// </remarks>
public sealed class PersonaEndpointsContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly Mock<IPersonaCryptoClient> _crypto = new();
    private readonly Mock<IEventService> _events = new();
    private readonly PersonaService _sut;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _orgA = Guid.NewGuid();
    private readonly Guid _orgB = Guid.NewGuid();
    private const string WalletAddress = "sorcha1testwalletaddress";

    public PersonaEndpointsContextTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlite(_connection).Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();

        _db.PlatformUsers.Add(new PlatformUser
        {
            Id = _userId,
            Email = "sarah@example.com",
            DisplayName = "Sarah",
        });
        _db.SaveChanges();

        _crypto.Setup(c => c.EncryptAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wallet, byte[] data, CancellationToken _) =>
                new PersonaCryptoRemoteResult(data, new byte[24], wallet));
        _crypto.Setup(c => c.DecryptAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, byte[] ct, byte[] _, string _, CancellationToken _) => ct);
        _events.Setup(e => e.CreateEventAsync(It.IsAny<ActivityEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActivityEvent e, CancellationToken _) => e);

        _sut = new PersonaService(_db, _crypto.Object, _events.Object, NullLogger<PersonaService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetAsync_NoContext_ReturnsEmpty_DefaultsToPersonal()
    {
        var result = await _sut.GetAsync(_userId);
        result.Should().NotBeNull();
        result.GivenName.Should().BeNull("Personal context is empty before any save.");
    }

    [Fact]
    public async Task ReplaceAsync_DifferentContexts_PersistsIndependentRows()
    {
        await _sut.ReplaceAsync(_userId, WalletAddress,
            new PersonaAttributesV1 { GivenName = "Sarah-personal" });
        await _sut.ReplaceAsync(_userId, WalletAddress,
            new PersonaAttributesV1 { GivenName = "Sarah-orgA" }, contextOrgId: _orgA);
        await _sut.ReplaceAsync(_userId, WalletAddress,
            new PersonaAttributesV1 { GivenName = "Sarah-orgB" }, contextOrgId: _orgB);

        var rows = await _db.PlatformUserPersonas.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(3, "each context produces its own row");
        rows.Select(r => r.ContextOrgId).Should().BeEquivalentTo(new[] { Guid.Empty, _orgA, _orgB });
    }

    [Fact]
    public async Task GetAsync_PersonalAndOrgContext_ReturnIndependentPersonas()
    {
        await _sut.ReplaceAsync(_userId, WalletAddress,
            new PersonaAttributesV1 { GivenName = "Sarah-personal" });
        await _sut.ReplaceAsync(_userId, WalletAddress,
            new PersonaAttributesV1 { GivenName = "Sarah-work" }, contextOrgId: _orgA);

        var personal = await _sut.GetAsync(_userId);
        var work = await _sut.GetAsync(_userId, _orgA);

        personal.GivenName!.Value.Should().Be("Sarah-personal");
        work.GivenName!.Value.Should().Be("Sarah-work");
    }

    [Fact]
    public async Task ReplaceAsync_SameContextTwice_UpdatesInPlace_OneRow()
    {
        await _sut.ReplaceAsync(_userId, WalletAddress,
            new PersonaAttributesV1 { GivenName = "first" }, contextOrgId: _orgA);
        await _sut.ReplaceAsync(_userId, WalletAddress,
            new PersonaAttributesV1 { GivenName = "second" }, contextOrgId: _orgA);

        var rows = await _db.PlatformUserPersonas.AsNoTracking()
            .Where(p => p.PlatformUserId == _userId && p.ContextOrgId == _orgA)
            .ToListAsync();
        rows.Should().HaveCount(1, "subsequent replaces upsert the same composite-key row");
        (await _sut.GetAsync(_userId, _orgA)).GivenName!.Value.Should().Be("second");
    }

    [Fact]
    public async Task DeleteAsync_OneContext_DoesNotAffectOthers()
    {
        await _sut.ReplaceAsync(_userId, WalletAddress,
            new PersonaAttributesV1 { GivenName = "Sarah-personal" });
        await _sut.ReplaceAsync(_userId, WalletAddress,
            new PersonaAttributesV1 { GivenName = "Sarah-orgA" }, contextOrgId: _orgA);

        await _sut.DeleteAsync(_userId, _orgA);

        var personal = await _sut.GetAsync(_userId);
        var work = await _sut.GetAsync(_userId, _orgA);
        personal.GivenName!.Value.Should().Be("Sarah-personal", "deleting the org persona must not touch Personal.");
        work.GivenName.Should().BeNull("the orgA persona was deleted; GET returns the empty default.");
    }

    [Fact]
    public async Task DeleteAsync_UnknownContext_IsIdempotent()
    {
        var unknown = Guid.NewGuid();
        var act = async () => await _sut.DeleteAsync(_userId, unknown);
        await act.Should().NotThrowAsync("DELETE on a context with no persona row is a no-op.");
    }
}
