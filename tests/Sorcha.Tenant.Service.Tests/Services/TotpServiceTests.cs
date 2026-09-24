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
using Sorcha.Tenant.Models.Identity;

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
            .Setup(s => s.WriteTwoFactorEnabledAsync(It.IsAny<PlatformUserId>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _loginKey = new byte[32];
        for (var i = 0; i < _loginKey.Length; i++) _loginKey[i] = (byte)(i + 100);

        _sut = new TotpService(_db, _identity.Object, _securityInbox.Object, Mock.Of<ISecurityChangeNotifier>(), _protection,
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

    /// <summary>
    /// #1703 — <c>TotpConfiguration.UserId</c> is one-to-one with <c>UserIdentity</c>, not
    /// <c>PlatformUser</c>. Before this fix, <c>ValidateBackupCodeAsync</c> passed that raw
    /// UserIdentity id straight to <c>ITenantSecurityInboxWriter.WriteBackupCodeUsedAsync</c> — a
    /// method whose parameter IS the PlatformUser id the inbox is addressed by — while the two sibling
    /// methods in this same class (<c>VerifyAndEnableAsync</c>, <c>DisableAsync</c>) both correctly
    /// resolve through <c>ResolvePlatformUserIdAsync</c> first. Assert on the id VALUE actually sent,
    /// not just that the writer was called — a call count passes even when every call addresses the
    /// wrong recipient.
    /// </summary>
    [Fact]
    public async Task ValidateBackupCodeAsync_ValidCode_NotifiesWithPlatformUserId_NotUserIdentityId()
    {
        var platformUserId = Guid.NewGuid();
        _identity
            .Setup(r => r.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentity { Id = _userId, PlatformUserId = platformUserId, Email = "ada@example.com" });

        var setup = await _sut.SetupAsync(_userId);
        var config = await _db.TotpConfigurations.SingleAsync(t => t.UserId == _userId);
        config.IsEnabled = true;
        await _db.SaveChangesAsync();

        Guid? notifiedPlatformUserId = null;
        _securityInbox
            .Setup(s => s.WriteBackupCodeUsedAsync(It.IsAny<PlatformUserId>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformUserId, CancellationToken>((id, _) => notifiedPlatformUserId = id.Value)
            .Returns(Task.CompletedTask);

        var consumed = await _sut.ValidateBackupCodeAsync(_userId, setup.BackupCodes[0]);

        consumed.Should().BeTrue();
        notifiedPlatformUserId.Should().NotBeNull();
        notifiedPlatformUserId!.Value.Should().Be(platformUserId,
            "the inbox is addressed by PlatformUser id, not the UserIdentity id backup codes are keyed by");
        notifiedPlatformUserId.Value.Should().NotBe(_userId);
    }

    /// <summary>
    /// When the UserIdentity cannot be resolved to a PlatformUser at all, the notification must be
    /// skipped rather than sent under the wrong (UserIdentity) id — a missing bell is strictly better
    /// than one addressed to nobody.
    /// </summary>
    [Fact]
    public async Task ValidateBackupCodeAsync_IdentityHasNoPlatformUser_SkipsNotification_DoesNotSendWrongId()
    {
        _identity
            .Setup(r => r.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentity { Id = _userId, PlatformUserId = Guid.Empty, Email = "ada@example.com" });

        var setup = await _sut.SetupAsync(_userId);
        var config = await _db.TotpConfigurations.SingleAsync(t => t.UserId == _userId);
        config.IsEnabled = true;
        await _db.SaveChangesAsync();

        var consumed = await _sut.ValidateBackupCodeAsync(_userId, setup.BackupCodes[0]);

        consumed.Should().BeTrue("the code itself is still valid and must still be consumed");
        _securityInbox.Verify(
            s => s.WriteBackupCodeUsedAsync(It.IsAny<PlatformUserId>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "must never notify under the raw UserIdentity id when no PlatformUser resolves");
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
        new(_db, _identity.Object, _securityInbox.Object, Mock.Of<ISecurityChangeNotifier>(), _protection,
            new LoginTokenSigningKey(loginKey), NullLogger<TotpService>.Instance);
}
