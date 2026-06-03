// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OtpNet;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Feature 146 / US1 — verifies TOTP secrets are protected at rest via
/// <see cref="ISecretProtectionProvider"/> (no plaintext/Base64), round-trip through
/// setup→verify→validate, and that a tampered stored secret fails safely (invalid code, not an error).
/// </summary>
public sealed class TotpServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly Mock<IIdentityRepository> _identity = new();
    private readonly Mock<ITenantSecurityInboxWriter> _securityInbox = new();
    private readonly ISecretProtectionProvider _protection;
    private readonly byte[] _loginKey;
    private readonly TotpService _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public TotpServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();

        var key = new byte[32];
        for (var i = 0; i < key.Length; i++) key[i] = (byte)(i + 1);
        _protection = new SoftwareSecretProtectionProvider(key, "test-key-v1");

        _identity
            .Setup(r => r.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentity { Id = _userId, Email = "ada@example.com" });
        _securityInbox
            .Setup(s => s.WriteTwoFactorEnabledAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _loginKey = new byte[32];
        for (var i = 0; i < _loginKey.Length; i++) _loginKey[i] = (byte)(i + 100);

        _sut = new TotpService(_db, _identity.Object, _securityInbox.Object, _protection,
            new LoginTokenSigningKey(_loginKey), NullLogger<TotpService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SetupAsync_StoresEncryptedSecret_NotPlaintextOrBase64()
    {
        var result = await _sut.SetupAsync(_userId);

        var stored = await _db.TotpConfigurations.AsNoTracking().SingleAsync(t => t.UserId == _userId);

        stored.EncryptionKeyId.Should().Be("test-key-v1");
        // The stored bytes must not be the plaintext Base32 secret, nor the old "v1:"-Base64 form.
        stored.EncryptedSecret.Should().NotEqual(Encoding.UTF8.GetBytes(result.Secret));
        var asText = Encoding.UTF8.GetString(stored.EncryptedSecret);
        asText.Should().NotContain(result.Secret);
        asText.Should().NotStartWith("v1:");
    }

    [Fact]
    public async Task SetupThenVerify_WithValidCode_EnablesAndSubsequentlyValidates()
    {
        var result = await _sut.SetupAsync(_userId);
        var code = new Totp(Base32Encoding.ToBytes(result.Secret), step: 30, totpSize: 6).ComputeTotp();

        var verified = await _sut.VerifyAndEnableAsync(_userId, code);
        verified.Should().BeTrue();

        (await _db.TotpConfigurations.AsNoTracking().SingleAsync(t => t.UserId == _userId))
            .IsEnabled.Should().BeTrue();

        var validated = await _sut.ValidateCodeAsync(_userId, code);
        validated.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateCodeAsync_TamperedStoredSecret_ReturnsFalse_DoesNotThrow()
    {
        var result = await _sut.SetupAsync(_userId);
        var code = new Totp(Base32Encoding.ToBytes(result.Secret), step: 30, totpSize: 6).ComputeTotp();
        (await _sut.VerifyAndEnableAsync(_userId, code)).Should().BeTrue();

        // Corrupt the stored ciphertext envelope. Assign a NEW array — EF Core does not detect
        // in-place byte[] mutations, so a fresh reference is required for the change to persist.
        var config = await _db.TotpConfigurations.SingleAsync(t => t.UserId == _userId);
        var corrupted = (byte[])config.EncryptedSecret.Clone();
        corrupted[^1] ^= 0xFF;
        config.EncryptedSecret = corrupted;
        await _db.SaveChangesAsync();

        // Must not throw (decrypt failure is handled), and must report invalid.
        var validated = await _sut.ValidateCodeAsync(_userId, code);

        validated.Should().BeFalse();
    }

    [Fact]
    public async Task LoginToken_GeneratedOnOneInstance_ValidatesOnAnotherWithSameDerivedKey()
    {
        // Two "replicas" sharing the same derived login-token key (different array instances).
        var replicaA = NewServiceWithLoginKey(_loginKey);
        var replicaB = NewServiceWithLoginKey((byte[])_loginKey.Clone());

        var token = await replicaA.GenerateLoginTokenAsync(_userId);
        var validated = await replicaB.ValidateLoginTokenAsync(token);

        validated.Should().Be(_userId); // stable across replicas/restarts (was per-process random pre-F146)
    }

    [Fact]
    public async Task LoginToken_ValidatedWithDifferentKey_ReturnsNull()
    {
        var replicaA = NewServiceWithLoginKey(_loginKey);
        var differentKey = new byte[32];
        differentKey[0] = 0xAB;
        var replicaWrongKey = NewServiceWithLoginKey(differentKey);

        var token = await replicaA.GenerateLoginTokenAsync(_userId);
        var validated = await replicaWrongKey.ValidateLoginTokenAsync(token);

        validated.Should().BeNull();
    }

    private TotpService NewServiceWithLoginKey(byte[] loginKey) =>
        new(_db, _identity.Object, _securityInbox.Object, _protection,
            new LoginTokenSigningKey(loginKey), NullLogger<TotpService>.Instance);
}
