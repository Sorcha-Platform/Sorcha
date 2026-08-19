// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.ServiceClients.OrgDidDocument;
using Sorcha.ServiceClients.OrgInfo;
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
    private readonly Mock<IOrgInfoClient> _orgInfo = new();
    private readonly Mock<IOrgKeyProtectionProvider> _protection = new();
    private readonly IssuanceKeyService _sut;
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly string _walletAddress = "ws11qpdemoissuancewallet";
    // Feature 149: the canonical operational wallet (A) the issuer DID anchors on —
    // distinct from the derived vc-issuance child wallet (_walletAddress = C).
    private readonly string _canonicalAddress = "ws11qpcanonicaloperationalwallet";

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
            .ReturnsAsync(true);

        // Feature 149: resolve the org's canonical operational wallet (A) for DID anchoring.
        _orgInfo.Setup(x => x.ResolveCanonicalWalletAddressAsync(_orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_canonicalAddress);

        _sut = new IssuanceKeyService(
            _db, _orgKey.Object, _didClient.Object, _orgInfo.Object, _protection.Object,
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
        // Feature 149: the published document is anchored on the canonical operational wallet (A),
        // NOT the derived vc-issuance child wallet (C, == _walletAddress).
        _didClient.Verify(x => x.RegenerateAsync(
            It.Is<OrgDidRegenerateRequest>(r => r.KeyEventReason == "IssuanceKeyRotated"
                                             && r.OrganizationId == _orgId
                                             && r.WalletAddress == _canonicalAddress),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOrDeriveAsync_KeyAlreadyExists_StillPublishesTheDidDocument()
    {
        // Issue #1518. The eager publish on the derive branch races Tenant's
        // OrgWalletReconciliationService, which provisions the org's canonical wallet
        // asynchronously — for a brand-new org the derive wins by a few seconds and the publish is
        // skipped. The early return here then meant NOTHING re-attempted it, so did.json stayed 404
        // until the org's first signature (which does self-heal, and fails closed).
        //
        // Measured on n1: `ensure` returned 200 in 3.8 ms with no publish attempt at all.
        SeedActiveKey();

        var state = await _sut.GetOrDeriveAsync(_orgId);

        state.Should().NotBeNull("the existing key is still returned");
        _didClient.Verify(x => x.RegenerateAsync(
            It.IsAny<OrgDidRegenerateRequest>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "an existing key says nothing about whether its DID document was ever published");
    }

    [Fact]
    public async Task GetOrDeriveAsync_KeyExistsButPublishFails_StillReturnsTheKey()
    {
        // Best-effort, deliberately: this is a lookup, and a Tenant write failure must not turn it
        // into a failure. Signing is where publication is enforced and fails closed.
        SeedActiveKey();
        _didClient.Setup(x => x.RegenerateAsync(
                It.IsAny<OrgDidRegenerateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var state = await _sut.GetOrDeriveAsync(_orgId);

        state.Should().NotBeNull();
    }

    [Fact]
    public async Task GetOrDeriveAsync_KeyExistsButNoCanonicalAddressYet_ReturnsTheKeyWithoutPublishing()
    {
        // The window itself: the wallet genuinely does not exist yet, so there is nothing to anchor
        // on. Skip quietly and return the key — the next call, or the first signature, will publish.
        SeedActiveKey();
        _orgInfo.Setup(x => x.ResolveCanonicalWalletAddressAsync(_orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var state = await _sut.GetOrDeriveAsync(_orgId);

        state.Should().NotBeNull();
        _didClient.Verify(x => x.RegenerateAsync(
            It.IsAny<OrgDidRegenerateRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RotateAsync_NoCanonicalAddress_SkipsDidDocumentRegeneration()
    {
        // Feature 149: with no resolvable canonical operational wallet (A), the published
        // document cannot be anchored — skip regeneration (issuance fails closed elsewhere).
        SeedActiveKey();
        _orgInfo.Setup(x => x.ResolveCanonicalWalletAddressAsync(_orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var newRow = await _sut.RotateAsync(_orgId, Guid.NewGuid());

        newRow.Should().NotBeNull(); // rotation itself still succeeds
        _didClient.Verify(x => x.RegenerateAsync(
            It.IsAny<OrgDidRegenerateRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Seeds the wallet row holding the issuance key's private material, so
    /// <see cref="IssuanceKeyService.GetActiveSigningMaterialAsync"/> reaches the
    /// DID-document gate instead of bailing on a missing/undecryptable wallet.
    /// </summary>
    private void SeedIssuanceWallet()
    {
        _db.Wallets.Add(new WalletEntity
        {
            Address = _walletAddress,
            Algorithm = "ED25519",
            PublicKey = Convert.ToBase64String(new byte[32]),
            EncryptedPrivateKey = Convert.ToBase64String(new byte[48]),
            EncryptionKeyId = "test-key",
            Owner = "org",
            Tenant = "system",
            Name = "issuance-key-wallet"
        });
        _db.SaveChanges();

        _protection.Setup(x => x.DecryptSeedAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[32]);
    }

    [Fact]
    public async Task GetActiveSigningMaterialAsync_RepublishesDidDocument_WhenKeyAlreadyExists()
    {
        // The repair path. Publication used to fire ONLY on first key derivation, so a
        // document that failed to publish (or was lost) stayed missing forever while
        // issuance carried on minting credentials no verifier could check.
        SeedActiveKey();
        SeedIssuanceWallet();

        var material = await _sut.GetActiveSigningMaterialAsync(_orgId);

        material.Should().NotBeNull();
        _didClient.Verify(x => x.RegenerateAsync(
            It.Is<OrgDidRegenerateRequest>(r => r.OrganizationId == _orgId
                                             && r.WalletAddress == _canonicalAddress),
            It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "signing must ensure the issuer's DID document is published, not assume it");
    }

    [Fact]
    public async Task GetActiveSigningMaterialAsync_UnpublishableDidDocument_ReturnsNull()
    {
        // Fail closed. Signing already refuses when the canonical address is unresolvable;
        // it must equally refuse when the document backing the kid cannot be published,
        // rather than mint a credential whose issuer DID resolves to nothing.
        SeedActiveKey();
        SeedIssuanceWallet();
        _didClient.Setup(x => x.RegenerateAsync(
                It.IsAny<OrgDidRegenerateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _didClient.Setup(x => x.ResolveCanonicalDidAsync(_orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null); // and none was previously published

        var material = await _sut.GetActiveSigningMaterialAsync(_orgId);

        material.Should().BeNull(
            "minting against an unpublishable issuer DID produces an unverifiable credential");
    }

    [Fact]
    public async Task GetActiveSigningMaterialAsync_PublishFailsButDocumentAlreadyPublished_ReturnsMaterial()
    {
        // Availability guard: a transient Tenant write failure must NOT block issuance when
        // a correctly-anchored document is already published and serving.
        SeedActiveKey();
        SeedIssuanceWallet();
        _didClient.Setup(x => x.RegenerateAsync(
                It.IsAny<OrgDidRegenerateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _didClient.Setup(x => x.ResolveCanonicalDidAsync(_orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync($"did:sorcha:org:{_canonicalAddress}");

        var material = await _sut.GetActiveSigningMaterialAsync(_orgId);

        material.Should().NotBeNull();
        material!.IssuerDid.Should().Be($"did:sorcha:org:{_canonicalAddress}");
    }

    [Fact]
    public async Task GetActiveSigningMaterialAsync_PublishedDocumentAnchoredElsewhere_ReturnsNull()
    {
        // Drift guard: a stale document anchored on a DIFFERENT address does not back the kid
        // we are about to sign with, so it must not be accepted as confirmation.
        SeedActiveKey();
        SeedIssuanceWallet();
        _didClient.Setup(x => x.RegenerateAsync(
                It.IsAny<OrgDidRegenerateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _didClient.Setup(x => x.ResolveCanonicalDidAsync(_orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("did:sorcha:org:ws11qpsomeotherwalletentirely");

        var material = await _sut.GetActiveSigningMaterialAsync(_orgId);

        material.Should().BeNull();
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
            // Wallet IS mapped (minimally): GetActiveSigningMaterialAsync reads it for the
            // issuance private key, so ignoring it left that whole method uncovered. Only the
            // jsonb dictionaries and navigations are dropped — they are what the relational
            // test provider cannot satisfy, not the scalar columns we assert on.
            modelBuilder.Entity<WalletEntity>(e =>
            {
                e.ToTable("Wallets");
                e.HasKey(w => w.Address);
                e.Ignore(w => w.Metadata);
                e.Ignore(w => w.Tags);
                e.Ignore(w => w.Addresses);
                e.Ignore(w => w.Delegates);
                e.Ignore(w => w.Transactions);
                e.Ignore(w => w.RecoveryKeyWraps);
            });
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
