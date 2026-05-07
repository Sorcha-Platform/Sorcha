// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Credentials;

namespace Sorcha.Wallet.Service.Tests.Credentials;

public class CredentialStoreTests : IDisposable
{
    private readonly TestCredentialDbContext _db;
    private readonly CredentialStore _store;

    public CredentialStoreTests()
    {
        var options = new DbContextOptionsBuilder<TestCredentialDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new TestCredentialDbContext(options);
        _store = new CredentialStore(
            _db,
            Moq.Mock.Of<Sorcha.Wallet.Service.Services.Interfaces.ICitizenInboxProjector>(),
            NullLogger<CredentialStore>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private static CredentialEntity CreateCredential(
        string id = "cred-1",
        string walletAddress = "wallet-1",
        string type = "LicenseCredential",
        string issuerDid = "did:sorcha:issuer:gov",
        CredentialStatus status = CredentialStatus.Active,
        DateTimeOffset? expiresAt = null)
    {
        return new CredentialEntity
        {
            Id = id,
            Type = type,
            IssuerDid = issuerDid,
            SubjectDid = "did:sorcha:subject:alice",
            ClaimsJson = """{"licenseType":"A"}""",
            IssuedAt = DateTimeOffset.UtcNow.AddDays(-30),
            ExpiresAt = expiresAt,
            RawToken = "dummy-token",
            Status = status,
            WalletAddress = walletAddress,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public async Task StoreAsync_NewCredential_Persists()
    {
        var credential = CreateCredential();

        await _store.StoreAsync(credential);

        var stored = await _db.Credentials
            .FirstOrDefaultAsync(c => c.Id == "cred-1" && c.WalletAddress == "wallet-1");
        stored.Should().NotBeNull();
        stored!.Type.Should().Be("LicenseCredential");
        stored.IssuerDid.Should().Be("did:sorcha:issuer:gov");
    }

    [Fact]
    public async Task StoreAsync_ExistingCredential_Updates()
    {
        var credential = CreateCredential();
        await _store.StoreAsync(credential);

        var updated = CreateCredential();
        updated.Status = CredentialStatus.Revoked;
        await _store.StoreAsync(updated);

        var stored = await _db.Credentials
            .FirstOrDefaultAsync(c => c.Id == "cred-1" && c.WalletAddress == "wallet-1");
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(CredentialStatus.Revoked);
    }

    [Fact]
    public async Task StoreAsync_NullCredential_ThrowsArgumentNullException()
    {
        var act = () => _store.StoreAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetByWalletAsync_ReturnsAllStatuses()
    {
        await _store.StoreAsync(CreateCredential("cred-1", status: CredentialStatus.Active));
        await _store.StoreAsync(CreateCredential("cred-2", status: CredentialStatus.Revoked));
        await _store.StoreAsync(CreateCredential("cred-3", status: CredentialStatus.Active));

        var results = await _store.GetByWalletAsync("wallet-1");

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetByWalletAsync_DifferentWallet_ReturnsEmpty()
    {
        await _store.StoreAsync(CreateCredential("cred-1", walletAddress: "wallet-1"));

        var results = await _store.GetByWalletAsync("wallet-2");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsCredential()
    {
        await _store.StoreAsync(CreateCredential("cred-1"));

        var result = await _store.GetByIdAsync("cred-1");

        result.Should().NotBeNull();
        result!.Id.Should().Be("cred-1");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentId_ReturnsNull()
    {
        var result = await _store.GetByIdAsync("does-not-exist");

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ExistingCredential_ReturnsTrue()
    {
        await _store.StoreAsync(CreateCredential("cred-1", walletAddress: "wallet-1"));

        var deleted = await _store.DeleteAsync("cred-1", "wallet-1");

        deleted.Should().BeTrue();
        var afterDelete = await _db.Credentials
            .FirstOrDefaultAsync(c => c.Id == "cred-1" && c.WalletAddress == "wallet-1");
        afterDelete.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonExistentCredential_ReturnsFalse()
    {
        var deleted = await _store.DeleteAsync("does-not-exist", "wallet-1");

        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_WrongWallet_ReturnsFalseAndLeavesOtherRow()
    {
        await _store.StoreAsync(CreateCredential("cred-1", walletAddress: "wallet-issuer"));

        var deleted = await _store.DeleteAsync("cred-1", "wallet-other");

        deleted.Should().BeFalse();
        var issuerRow = await _db.Credentials
            .FirstOrDefaultAsync(c => c.Id == "cred-1" && c.WalletAddress == "wallet-issuer");
        issuerRow.Should().NotBeNull();
    }

    [Fact]
    public async Task MatchAsync_FilterByType_ReturnsMatching()
    {
        await _store.StoreAsync(CreateCredential("cred-1", type: "LicenseCredential"));
        await _store.StoreAsync(CreateCredential("cred-2", type: "IdentityAttestation"));

        var results = await _store.MatchAsync("wallet-1", type: "LicenseCredential");

        results.Should().ContainSingle();
        results[0].Type.Should().Be("LicenseCredential");
    }

    [Fact]
    public async Task MatchAsync_FilterByIssuer_ReturnsMatching()
    {
        await _store.StoreAsync(CreateCredential("cred-1", issuerDid: "did:sorcha:issuer:gov"));
        await _store.StoreAsync(CreateCredential("cred-2", issuerDid: "did:sorcha:issuer:untrusted"));

        var results = await _store.MatchAsync(
            "wallet-1",
            acceptedIssuers: ["did:sorcha:issuer:gov"]);

        results.Should().ContainSingle();
        results[0].IssuerDid.Should().Be("did:sorcha:issuer:gov");
    }

    [Fact]
    public async Task MatchAsync_ExcludesExpired()
    {
        await _store.StoreAsync(CreateCredential("cred-1", expiresAt: DateTimeOffset.UtcNow.AddDays(30)));
        await _store.StoreAsync(CreateCredential("cred-2", expiresAt: DateTimeOffset.UtcNow.AddDays(-1)));

        var results = await _store.MatchAsync("wallet-1");

        results.Should().ContainSingle();
        results[0].Id.Should().Be("cred-1");
    }

    [Fact]
    public async Task MatchAsync_NoFilters_ReturnsAllActive()
    {
        await _store.StoreAsync(CreateCredential("cred-1"));
        await _store.StoreAsync(CreateCredential("cred-2", type: "IdentityAttestation"));

        var results = await _store.MatchAsync("wallet-1");

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task MatchAsync_NullExpiresAt_TreatedAsNoExpiry()
    {
        await _store.StoreAsync(CreateCredential("cred-1", expiresAt: null));

        var results = await _store.MatchAsync("wallet-1");

        results.Should().ContainSingle();
    }

    // ===== Feature 106 disambiguation — same credential id held by issuer and recipient =====

    [Fact]
    public async Task DeleteAsync_DualWallet_OnlyDeletesTargetRow()
    {
        // Issuer stores "cred-dup" as Active; recipient receives it as PendingAcceptance.
        await _store.StoreAsync(CreateCredential("cred-dup", walletAddress: "wallet-issuer"));
        await _store.StoreAsync(CreateCredential("cred-dup", walletAddress: "wallet-recipient",
            status: CredentialStatus.PendingAcceptance));

        var deleted = await _store.DeleteAsync("cred-dup", "wallet-recipient");

        deleted.Should().BeTrue();
        var issuerRow = await _db.Credentials
            .FirstOrDefaultAsync(c => c.Id == "cred-dup" && c.WalletAddress == "wallet-issuer");
        issuerRow.Should().NotBeNull("issuer row must survive the recipient's delete");
        var recipientRow = await _db.Credentials
            .FirstOrDefaultAsync(c => c.Id == "cred-dup" && c.WalletAddress == "wallet-recipient");
        recipientRow.Should().BeNull("recipient row should have been removed");
    }

    [Fact]
    public async Task UpdateStatusAsync_DualWallet_OnlyUpdatesTargetRow()
    {
        await _store.StoreAsync(CreateCredential("cred-dup", walletAddress: "wallet-issuer"));
        await _store.StoreAsync(CreateCredential("cred-dup", walletAddress: "wallet-recipient"));

        var updated = await _store.UpdateStatusAsync("cred-dup", "wallet-recipient", CredentialStatus.Revoked);

        updated.Should().BeTrue();
        var issuerRow = await _db.Credentials
            .FirstOrDefaultAsync(c => c.Id == "cred-dup" && c.WalletAddress == "wallet-issuer");
        issuerRow!.Status.Should().Be(CredentialStatus.Active, "issuer row must be unaffected");
        var recipientRow = await _db.Credentials
            .FirstOrDefaultAsync(c => c.Id == "cred-dup" && c.WalletAddress == "wallet-recipient");
        recipientRow!.Status.Should().Be(CredentialStatus.Revoked, "recipient row should be revoked");
    }

    [Fact]
    public async Task GetByIdAsync_DualWallet_PrefersActiveRow()
    {
        // Issuer's copy is Active; recipient's copy is PendingAcceptance.
        await _store.StoreAsync(CreateCredential("cred-dup", walletAddress: "wallet-issuer"));
        await _store.StoreAsync(CreateCredential("cred-dup", walletAddress: "wallet-recipient",
            status: CredentialStatus.PendingAcceptance));

        var result = await _store.GetByIdAsync("cred-dup");

        result.Should().NotBeNull();
        result!.Status.Should().Be(CredentialStatus.Active,
            "GetByIdAsync must prefer the Active row when multiple exist");
        result.WalletAddress.Should().Be("wallet-issuer");
    }

    [Fact]
    public async Task RecordPresentationAsync_DualWallet_OperatesOnActiveRow()
    {
        // Both wallets have Active copies of the same credential.
        await _store.StoreAsync(CreateCredential("cred-dup", walletAddress: "wallet-issuer",
            type: "TradeFinanceCertificate"));
        await _store.StoreAsync(CreateCredential("cred-dup", walletAddress: "wallet-recipient",
            type: "TradeFinanceCertificate"));

        var consumed = await _store.RecordPresentationAsync("cred-dup");

        // At least one Active row must have its count incremented.
        var rows = await _db.Credentials
            .Where(c => c.Id == "cred-dup")
            .ToListAsync();
        rows.Sum(r => r.PresentationCount).Should().Be(1,
            "exactly one row should have been incremented");
        consumed.Should().BeFalse("Reusable policy never consumes");
    }
}

/// <summary>
/// Minimal test DbContext that only configures the Credentials entity,
/// avoiding Wallet entity's Npgsql-specific jsonb column mappings
/// that are incompatible with the EF Core InMemory provider.
/// </summary>
internal class TestCredentialDbContext : WalletDbContext
{
    public TestCredentialDbContext(DbContextOptions<TestCredentialDbContext> options)
        : base((DbContextOptions)options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Ignore Wallet-related entities that use Npgsql-specific jsonb columns
        // incompatible with the EF Core InMemory provider
        modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.Wallet>();
        modelBuilder.Ignore<WalletAddress>();
        modelBuilder.Ignore<WalletAccess>();
        modelBuilder.Ignore<WalletTransaction>();

        // Only configure the Credential entity. Use the same composite key as the real schema
        // so that disambiguation tests can store two rows sharing an Id across different wallets.
        modelBuilder.Entity<CredentialEntity>(entity =>
        {
            entity.HasKey(e => new { e.Id, e.WalletAddress });
            entity.Property(e => e.Type).IsRequired();
            entity.Property(e => e.IssuerDid).IsRequired();
            entity.Property(e => e.SubjectDid).IsRequired();
            entity.Property(e => e.ClaimsJson).IsRequired();
            entity.Property(e => e.RawToken).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasConversion<string>().HasDefaultValue(CredentialStatus.Active);
            entity.Property(e => e.WalletAddress).IsRequired();
        });
    }
}
