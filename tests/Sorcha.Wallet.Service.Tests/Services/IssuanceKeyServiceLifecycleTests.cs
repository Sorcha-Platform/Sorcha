// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.ServiceClients.OrgDidDocument;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain.Enums;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Implementation;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Feature 120 US6 — covers <see cref="IssuanceKeyService"/>'s rotate/revoke lifecycle
/// without exercising the full Feature 083 derivation chain (mocked).
/// </summary>
public sealed class IssuanceKeyServiceLifecycleTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WalletDbContext _db;
    private readonly Mock<IOrgKeyDerivationService> _orgKey = new();
    private readonly Mock<IOrgDidDocumentClient> _didClient = new();
    private readonly Mock<IOrgKeyProtectionProvider> _protection = new();
    private readonly IssuanceKeyService _sut;
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly string _walletAddress = "ws11qpdemoissuancewallet";

    public IssuanceKeyServiceLifecycleTests()
    {
        // Sqlite in-memory under a test-only DbContext that ignores Postgres-specific
        // jsonb mappings. The real WalletDbContext maps Wallet.Metadata as
        // Dictionary<string, string> → jsonb which neither InMemory nor Sqlite accepts.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new TestWalletDbContext(options);
        _db.Database.EnsureCreated();

        // Idempotent re-derivation (returns the same wallet address each call).
        _orgKey.Setup(x => x.DeriveUserKeyAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<uint>(), KeyUsage.VCIssuance, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DerivedKeyResult(
                Guid.NewGuid(), _walletAddress, "m/test", KeyUsage.VCIssuance, 0,
                "Active", "Custodial", DateTime.UtcNow));

        _didClient.Setup(x => x.RegenerateAsync(It.IsAny<OrgDidRegenerateRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sut = new IssuanceKeyService(
            _db, _orgKey.Object, _didClient.Object, _protection.Object,
            NullLogger<IssuanceKeyService>.Instance);
    }

    private void SeedActiveKey(int rotationIndex = 1)
    {
        var publicKey = new byte[32];
        new Random(42).NextBytes(publicKey);

        _db.IssuanceKeyStates.Add(new IssuanceKeyState
        {
            Id = Guid.NewGuid(),
            OrganizationId = _orgId,
            Slot = 1,
            RotationIndex = rotationIndex,
            Status = IssuanceKeyStatus.Active,
            PublicKey = publicKey,
            Algorithm = "ED25519",
            Thumbprint = "PLACEHOLDER0000000000000000000000000000Aaa",
            DerivedAt = DateTimeOffset.UtcNow
        });
        _db.DerivedKeyRecords.Add(new DerivedKeyRecord
        {
            Id = Guid.NewGuid(),
            OrgMasterKeyId = Guid.NewGuid(),
            OrganizationId = _orgId.ToString(),
            UserId = _orgId.ToString(),
            DepartmentId = 0,
            KeyUsage = KeyUsage.VCIssuance,
            KeyIndex = 0,
            DerivationPath = "m/test",
            WalletAddress = _walletAddress,
            Status = DerivedKeyStatus.Active,
            CustodyMode = CustodyMode.Custodial
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task RotateAsync_NoActiveKey_ReturnsNull()
    {
        var result = await _sut.RotateAsync(_orgId, Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task RotateAsync_MovesActiveToRotated_AndCreatesNewActive()
    {
        SeedActiveKey(rotationIndex: 1);

        var newRow = await _sut.RotateAsync(_orgId, Guid.NewGuid());

        newRow.Should().NotBeNull();
        newRow!.RotationIndex.Should().Be(2);
        newRow.Status.Should().Be(IssuanceKeyStatus.Active);

        var rows = await _db.IssuanceKeyStates
            .Where(k => k.OrganizationId == _orgId).OrderBy(k => k.RotationIndex).ToListAsync();
        rows.Should().HaveCount(2);
        rows[0].RotationIndex.Should().Be(1);
        rows[0].Status.Should().Be(IssuanceKeyStatus.Rotated);
        rows[0].RotatedAt.Should().NotBeNull();
        rows[1].RotationIndex.Should().Be(2);
        rows[1].Status.Should().Be(IssuanceKeyStatus.Active);
    }

    [Fact]
    public async Task RotateAsync_TriggersDidDocumentRegeneration()
    {
        SeedActiveKey();
        await _sut.RotateAsync(_orgId, Guid.NewGuid());
        _didClient.Verify(x => x.RegenerateAsync(
            It.Is<OrgDidRegenerateRequest>(r => r.KeyEventReason == "IssuanceKeyRotated"
                                             && r.OrganizationId == _orgId),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_UnknownRotation_ReturnsNull()
    {
        SeedActiveKey();
        var result = await _sut.RevokeAsync(_orgId, rotationIndex: 99, "test", Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task RevokeAsync_MarksRevokedWithReasonAndOpId()
    {
        SeedActiveKey();
        var govOp = Guid.NewGuid();

        var revoked = await _sut.RevokeAsync(_orgId, 1, "key compromise", govOp);

        revoked.Should().NotBeNull();
        revoked!.Status.Should().Be(IssuanceKeyStatus.Revoked);
        revoked.RevokedAt.Should().NotBeNull();
        revoked.RevocationReason.Should().Be("key compromise");
        revoked.RevokedByGovernanceOpId.Should().Be(govOp);
    }

    [Fact]
    public async Task RevokeAsync_AlreadyRevoked_IsIdempotent()
    {
        SeedActiveKey();
        await _sut.RevokeAsync(_orgId, 1, "first revoke", Guid.NewGuid());
        var second = await _sut.RevokeAsync(_orgId, 1, "second revoke", Guid.NewGuid());

        // Idempotent — second call returns the existing Revoked row unchanged.
        second.Should().NotBeNull();
        second!.Status.Should().Be(IssuanceKeyStatus.Revoked);
        second.RevocationReason.Should().Be("first revoke");
    }

    [Fact]
    public async Task RevokeAsync_RevokedRotatedKey_Permitted()
    {
        SeedActiveKey(rotationIndex: 1);
        // Rotate first so rotation 1 becomes Rotated.
        await _sut.RotateAsync(_orgId, Guid.NewGuid());
        // Now revoke the rotated rotation 1 — permitted per data-model state machine.
        var revoked = await _sut.RevokeAsync(_orgId, 1, "rotated then compromised", Guid.NewGuid());
        revoked.Should().NotBeNull();
        revoked!.Status.Should().Be(IssuanceKeyStatus.Revoked);
    }

    [Fact]
    public async Task ListAllAsync_ReturnsRowsOrderedByRotationIndex()
    {
        SeedActiveKey(rotationIndex: 1);
        await _sut.RotateAsync(_orgId, Guid.NewGuid()); // creates rotation 2
        await _sut.RevokeAsync(_orgId, 2, "test", Guid.NewGuid());

        var all = await _sut.ListAllAsync(_orgId);

        all.Should().HaveCount(2);
        all[0].RotationIndex.Should().Be(1);
        all[0].Status.Should().Be(IssuanceKeyStatus.Rotated);
        all[1].RotationIndex.Should().Be(2);
        all[1].Status.Should().Be(IssuanceKeyStatus.Revoked);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Test DbContext that ignores Postgres-specific jsonb columns + columns with
    /// Postgres-only defaults (e.g., gen_random_uuid()) so EF model validation passes
    /// under Sqlite. Retains the IssuanceKeyStates / DerivedKeyRecords mappings the
    /// service-under-test exercises.
    /// </summary>
    /// <summary>
    /// Minimal Sqlite-friendly DbContext exposing only the entities IssuanceKeyService
    /// touches. The real WalletDbContext maps several Dictionary&lt;string,string&gt; →
    /// jsonb columns that neither InMemory nor Sqlite providers accept.
    /// </summary>
    private sealed class TestWalletDbContext : WalletDbContext
    {
        public TestWalletDbContext(DbContextOptions<WalletDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("wallet");

            // Ignore every other entity type — they bring jsonb / Postgres-specific
            // mappings the relational test provider can't satisfy. We only need
            // IssuanceKeyState + DerivedKeyRecord for IssuanceKeyService coverage.
            modelBuilder.Ignore<WalletEntity>();
            modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.WalletAddress>();
            modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.WalletAccess>();
            modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.WalletTransaction>();
            modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.CredentialEntity>();
            modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.RecoveryKeyWrap>();
            modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.RecoveryAuditLog>();
            modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.OrgMasterKey>();
            modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.ThresholdKeyGroup>();
            modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.SigningKeyShare>();
            modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.SigningSession>();
            modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.CitizenDeviceStatusList>();
            modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.CitizenWalletSyncCursor>();
            modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.CitizenHolderIndex>();
            modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.CitizenCredentialEventLog>();

            modelBuilder.Entity<IssuanceKeyState>(e =>
            {
                e.ToTable("IssuanceKeyStates");
                e.HasKey(x => x.Id);
                e.Property(x => x.Status).HasConversion<int>().IsRequired();
            });

            modelBuilder.Entity<DerivedKeyRecord>(e =>
            {
                e.ToTable("DerivedKeyRecords");
                e.HasKey(x => x.Id);
                e.Property(x => x.KeyUsage).HasConversion<string>().IsRequired();
                e.Property(x => x.Status).HasConversion<string>().IsRequired();
                e.Property(x => x.CustodyMode).HasConversion<string>().IsRequired();
                e.Ignore(x => x.OrgMasterKey);
                e.Ignore(x => x.Wallet);
            });
        }
    }
}
