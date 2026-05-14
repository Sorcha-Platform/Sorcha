// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Tenant.Models.Persona;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Services.Interfaces;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="PersonaService"/>. Feature 092 / T044.
/// Uses in-memory SQLite so the EF cascade and unique-key behaviour are
/// exercised for real, with a mocked Wallet crypto client and activity log.
/// </summary>
public sealed class PersonaServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly Mock<IPersonaCryptoClient> _crypto = new();
    private readonly Mock<IEventService> _events = new();
    private readonly PersonaService _sut;
    private readonly Guid _userId = Guid.NewGuid();
    private const string WalletAddress = "sorcha1testwalletaddress";

    public PersonaServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();

        // Seed a PlatformUser so the persona FK is satisfied, then a
        // UserPreferences row pointing at a primary wallet so
        // ResolvePrimaryWalletAsync returns a non-null address.
        _db.PlatformUsers.Add(new PlatformUser
        {
            Id = _userId,
            Email = "ada@example.com",
            DisplayName = "Ada Lovelace",
        });
        _db.UserPreferences.Add(new UserPreferences
        {
            UserId = _userId,
            DefaultWalletAddress = WalletAddress,
        });
        _db.SaveChanges();

        var logger = Mock.Of<ILogger<PersonaService>>();
        _sut = new PersonaService(_db, _crypto.Object, _events.Object, logger);

        // Activity log writes are verified selectively — default to success.
        _events
            .Setup(e => e.CreateEventAsync(It.IsAny<ActivityEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActivityEvent e, CancellationToken _) => e);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // --- GetAsync ---

    [Fact]
    public async Task GetAsync_NewUser_ReturnsEmptyPersona()
    {
        var result = await _sut.GetAsync(_userId);

        result.Should().NotBeNull();
        result.GivenName.Should().BeNull();
        result.AllEmails.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_ActingAsOther_ThrowsValidationException()
    {
        var act = async () => await _sut.GetAsync(_userId, options: new PersonaReadOptions { ActingAs = "delegate" });

        var ex = await act.Should().ThrowAsync<PersonaValidationException>();
        ex.Which.Errors.Should().ContainKey("actingAs");
    }

    // --- ReplaceAsync happy path ---

    [Fact]
    public async Task ReplaceAsync_ValidPayload_CallsCryptoAndUpsertsRow()
    {
        _crypto
            .Setup(c => c.EncryptAsync(WalletAddress, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonaCryptoRemoteResult(
                Ciphertext: [1, 2, 3],
                Nonce: new byte[24],
                WrappedKeyRef: WalletAddress));

        var payload = new PersonaAttributesV1
        {
            GivenName = "Ada",
            FamilyName = "Lovelace",
            Emails = [new PersonaEmail("ada@example.com", true)],
        };

        var result = await _sut.ReplaceAsync(_userId, WalletAddress, payload);

        result.GivenName!.Value.Should().Be("Ada");
        result.DefaultEmail!.Value.Value.Should().Be("ada@example.com");

        // Feature 125 composite key: (PlatformUserId, ContextOrgId). Personal context = Guid.Empty.
        var row = await _db.PlatformUserPersonas.FindAsync(_userId, Guid.Empty);
        row.Should().NotBeNull();
        row!.CiphertextBlob.Should().Equal(1, 2, 3);
        row.WrappedKeyRef.Should().Be(WalletAddress);

        // Activity log: exactly one "persona.replaced" event on success.
        _events.Verify(
            e => e.CreateEventAsync(
                It.Is<ActivityEvent>(a => a.EventType == "persona.replaced" && a.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- Feature 103 US2: MiddleName round-trip ---

    [Fact]
    public async Task ReplaceAsync_WithMiddleName_PropagatesToReadModel()
    {
        // Feature 103 US2: PersonaAttributesV1 gained an optional MiddleName
        // field so the PersonName/v1 core primitive can autofill it. Verify
        // the write-side value flows through ReplaceAsync into the returned
        // read model and is retained in the encrypted plaintext the service
        // hands to the crypto client.
        byte[]? capturedPlaintext = null;
        _crypto
            .Setup(c => c.EncryptAsync(WalletAddress, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], CancellationToken>((_, plaintext, _) => capturedPlaintext = plaintext)
            .ReturnsAsync(new PersonaCryptoRemoteResult(
                Ciphertext: [1, 2, 3],
                Nonce: new byte[24],
                WrappedKeyRef: WalletAddress));

        var payload = new PersonaAttributesV1
        {
            GivenName = "Alice",
            MiddleName = "Maeve",
            FamilyName = "O'Brien",
            Emails = [new PersonaEmail("alice@example.com", true)],
        };

        var result = await _sut.ReplaceAsync(_userId, WalletAddress, payload);

        result.GivenName!.Value.Should().Be("Alice");
        result.MiddleName.Should().NotBeNull();
        result.MiddleName!.Value.Should().Be("Maeve");
        result.FamilyName!.Value.Should().Be("O'Brien");

        // The plaintext handed to the crypto client must round-trip MiddleName.
        // Property names use System.Text.Json's default camelCase policy.
        capturedPlaintext.Should().NotBeNull();
        var plaintextJson = System.Text.Encoding.UTF8.GetString(capturedPlaintext!);
        plaintextJson.Should().Contain("\"middleName\"");
        plaintextJson.Should().Contain("\"Maeve\"");
    }

    [Fact]
    public async Task ReplaceAsync_WithoutMiddleName_ReadModelMiddleNameIsNull()
    {
        // Pre-Feature-103 personas and users who don't set a middle name
        // must continue to read as MiddleName == null, not as an error.
        _crypto
            .Setup(c => c.EncryptAsync(WalletAddress, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonaCryptoRemoteResult(
                Ciphertext: [1, 2, 3],
                Nonce: new byte[24],
                WrappedKeyRef: WalletAddress));

        var payload = new PersonaAttributesV1
        {
            GivenName = "Ada",
            FamilyName = "Lovelace",
            // MiddleName intentionally omitted
        };

        var result = await _sut.ReplaceAsync(_userId, WalletAddress, payload);

        result.GivenName!.Value.Should().Be("Ada");
        result.MiddleName.Should().BeNull();
    }

    [Fact]
    public async Task ReplaceAsync_NoWalletAddress_Throws409WalletNotProvisioned()
    {
        // Passing a null or empty wallet address — the endpoint does this
        // when the caller's JWT carries no wallet_address claim — must fail
        // fast with PersonaWalletNotProvisionedException so the UI can
        // surface a "provision a wallet first" recovery prompt.
        var act = async () => await _sut.ReplaceAsync(
            _userId,
            walletAddress: null,
            new PersonaAttributesV1 { GivenName = "Grace" });

        await act.Should().ThrowAsync<PersonaWalletNotProvisionedException>();
    }

    // --- Validation failures ---

    [Fact]
    public async Task ReplaceAsync_ListCapExceeded_ThrowsValidation()
    {
        var emails = Enumerable.Range(0, 6)
            .Select(i => new PersonaEmail($"a{i}@example.com", i == 0))
            .ToList();

        var act = async () => await _sut.ReplaceAsync(_userId, WalletAddress, new PersonaAttributesV1 { Emails = emails });

        var ex = await act.Should().ThrowAsync<PersonaValidationException>();
        ex.Which.Errors.Should().ContainKey("emails");
        ex.Which.Errors["emails"].Should().Contain("list_cap_exceeded");
    }

    [Fact]
    public async Task ReplaceAsync_MultipleDefaults_ThrowsValidation()
    {
        var payload = new PersonaAttributesV1
        {
            Emails =
            [
                new PersonaEmail("a@example.com", true),
                new PersonaEmail("b@example.com", true),
            ]
        };

        var act = async () => await _sut.ReplaceAsync(_userId, WalletAddress, payload);

        var ex = await act.Should().ThrowAsync<PersonaValidationException>();
        ex.Which.Errors["emails"].Should().Contain("multiple_defaults");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("a@")]
    [InlineData("a@example")]
    public async Task ReplaceAsync_InvalidEmail_ThrowsValidation(string bad)
    {
        var payload = new PersonaAttributesV1
        {
            Emails = [new PersonaEmail(bad, true)]
        };

        var act = async () => await _sut.ReplaceAsync(_userId, WalletAddress, payload);

        var ex = await act.Should().ThrowAsync<PersonaValidationException>();
        ex.Which.Errors.Should().ContainKey("emails[0].value");
        ex.Which.Errors["emails[0].value"].Should().Contain("invalid_email");
    }

    [Fact]
    public async Task ReplaceAsync_InvalidPhone_ThrowsValidation()
    {
        var payload = new PersonaAttributesV1
        {
            Phones = [new PersonaPhone("not-e164", true)]
        };

        var act = async () => await _sut.ReplaceAsync(_userId, WalletAddress, payload);

        var ex = await act.Should().ThrowAsync<PersonaValidationException>();
        ex.Which.Errors["phones[0].value"].Should().Contain("invalid_phone");
    }

    [Fact]
    public async Task ReplaceAsync_LowercaseCountryCode_IsNormalisedAndAccepted()
    {
        _crypto
            .Setup(c => c.EncryptAsync(WalletAddress, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonaCryptoRemoteResult(
                Ciphertext: [1], Nonce: new byte[24], WrappedKeyRef: WalletAddress));

        var payload = new PersonaAttributesV1
        {
            Addresses =
            [
                new PersonaAddress(
                    Line1: "1 Analytical Way",
                    Line2: null,
                    City: "London",
                    Region: null,
                    PostalCode: "SW1A 1AA",
                    Country: "gb", // lowercase!
                    IsDefault: true)
            ]
        };

        var result = await _sut.ReplaceAsync(_userId, WalletAddress, payload);

        result.DefaultAddress!.Value.Country.Should().Be("GB");
    }

    // --- DeleteAsync ---

    [Fact]
    public async Task DeleteAsync_NoRow_IsIdempotentAndSkipsAudit()
    {
        await _sut.DeleteAsync(_userId);

        _events.Verify(
            e => e.CreateEventAsync(It.IsAny<ActivityEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ExistingRow_RemovesAndWritesAudit()
    {
        _db.PlatformUserPersonas.Add(new PlatformUserPersona
        {
            PlatformUserId = _userId,
            CiphertextBlob = [1, 2, 3],
            Nonce = new byte[24],
            WrappedKeyRef = WalletAddress,
            SchemaVersion = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        await _sut.DeleteAsync(_userId);

        // Feature 125 composite key: (PlatformUserId, ContextOrgId). Personal context = Guid.Empty.
        var row = await _db.PlatformUserPersonas.FindAsync(_userId, Guid.Empty);
        row.Should().BeNull();

        _events.Verify(
            e => e.CreateEventAsync(
                It.Is<ActivityEvent>(a => a.EventType == "persona.deleted" && a.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
