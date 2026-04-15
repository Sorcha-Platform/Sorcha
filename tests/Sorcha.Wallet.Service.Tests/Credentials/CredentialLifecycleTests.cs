// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Credentials;

namespace Sorcha.Wallet.Service.Tests.Credentials;

public class CredentialLifecycleTests : IDisposable
{
    private readonly TestCredentialDbContext _db;
    private readonly CredentialStore _store;

    public CredentialLifecycleTests()
    {
        var options = new DbContextOptionsBuilder<TestCredentialDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new TestCredentialDbContext(options);
        _store = new CredentialStore(_db, NullLogger<CredentialStore>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private static CredentialEntity CreateCredential(
        string id = "cred-1",
        CredentialStatus status = CredentialStatus.Active,
        string usagePolicy = "Reusable",
        int? maxPresentations = null,
        DateTimeOffset? expiresAt = null)
    {
        return new CredentialEntity
        {
            Id = id,
            Type = "LicenseCredential",
            IssuerDid = "did:sorcha:w:issuer",
            SubjectDid = "did:sorcha:w:holder",
            ClaimsJson = """{"class":"B"}""",
            IssuedAt = DateTimeOffset.UtcNow.AddDays(-30),
            ExpiresAt = expiresAt,
            RawToken = "eyJ.test.token",
            Status = status,
            WalletAddress = "wallet-1",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UsagePolicy = usagePolicy,
            MaxPresentations = maxPresentations,
            PresentationCount = 0
        };
    }

    // ===== State Transition Tests =====

    [Fact]
    public async Task UpdateStatusAsync_ActiveToSuspended_Succeeds()
    {
        var cred = CreateCredential();
        await _store.StoreAsync(cred);

        var result = await _store.UpdateStatusAsync("cred-1", CredentialStatus.Suspended);

        result.Should().BeTrue();
        var updated = await _store.GetByIdAsync("cred-1");
        updated!.Status.Should().Be(CredentialStatus.Suspended);
    }

    [Fact]
    public async Task UpdateStatusAsync_SuspendedToActive_Succeeds()
    {
        var cred = CreateCredential(status: CredentialStatus.Suspended);
        await _store.StoreAsync(cred);

        var result = await _store.UpdateStatusAsync("cred-1", CredentialStatus.Active);

        result.Should().BeTrue();
        var updated = await _store.GetByIdAsync("cred-1");
        updated!.Status.Should().Be(CredentialStatus.Active);
    }

    [Fact]
    public async Task UpdateStatusAsync_ActiveToRevoked_Succeeds()
    {
        var cred = CreateCredential();
        await _store.StoreAsync(cred);

        var result = await _store.UpdateStatusAsync("cred-1", CredentialStatus.Revoked);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateStatusAsync_SuspendedToRevoked_Succeeds()
    {
        var cred = CreateCredential(status: CredentialStatus.Suspended);
        await _store.StoreAsync(cred);

        var result = await _store.UpdateStatusAsync("cred-1", CredentialStatus.Revoked);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateStatusAsync_RevokedToActive_Fails()
    {
        var cred = CreateCredential(status: CredentialStatus.Revoked);
        await _store.StoreAsync(cred);

        var result = await _store.UpdateStatusAsync("cred-1", CredentialStatus.Active);

        result.Should().BeFalse();
        var unchanged = await _store.GetByIdAsync("cred-1");
        unchanged!.Status.Should().Be(CredentialStatus.Revoked);
    }

    [Fact]
    public async Task UpdateStatusAsync_ExpiredToActive_Fails()
    {
        var cred = CreateCredential(status: CredentialStatus.Expired);
        await _store.StoreAsync(cred);

        var result = await _store.UpdateStatusAsync("cred-1", CredentialStatus.Active);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateStatusAsync_ConsumedToActive_Fails()
    {
        var cred = CreateCredential(status: CredentialStatus.Consumed);
        await _store.StoreAsync(cred);

        var result = await _store.UpdateStatusAsync("cred-1", CredentialStatus.Active);

        result.Should().BeFalse();
    }

    // ===== Lazy Expiry Detection Tests =====

    [Fact]
    public async Task GetByIdAsync_ActiveCredentialPastExpiry_TransitionsToExpired()
    {
        var cred = CreateCredential(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        await _store.StoreAsync(cred);

        var result = await _store.GetByIdAsync("cred-1");

        result!.Status.Should().Be(CredentialStatus.Expired);
    }

    [Fact]
    public async Task GetByIdAsync_ActiveCredentialNotExpired_StaysActive()
    {
        var cred = CreateCredential(expiresAt: DateTimeOffset.UtcNow.AddDays(30));
        await _store.StoreAsync(cred);

        var result = await _store.GetByIdAsync("cred-1");

        result!.Status.Should().Be(CredentialStatus.Active);
    }

    [Fact]
    public async Task GetByWalletAsync_ExpiresStaleCredentials()
    {
        var active = CreateCredential(id: "cred-active", expiresAt: DateTimeOffset.UtcNow.AddDays(30));
        var expired = CreateCredential(id: "cred-expired", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        await _store.StoreAsync(active);
        await _store.StoreAsync(expired);

        var results = await _store.GetByWalletAsync("wallet-1");

        var expiredCred = results.First(c => c.Id == "cred-expired");
        expiredCred.Status.Should().Be(CredentialStatus.Expired);
        var activeCred = results.First(c => c.Id == "cred-active");
        activeCred.Status.Should().Be(CredentialStatus.Active);
    }

    // ===== Usage Policy Tests =====

    [Fact]
    public async Task RecordPresentationAsync_SingleUse_ConsumesAfterOnePresentation()
    {
        var cred = CreateCredential(usagePolicy: "SingleUse");
        await _store.StoreAsync(cred);

        var consumed = await _store.RecordPresentationAsync("cred-1");

        consumed.Should().BeTrue();
        var updated = await _store.GetByIdAsync("cred-1");
        updated!.Status.Should().Be(CredentialStatus.Consumed);
        updated.PresentationCount.Should().Be(1);
    }

    [Fact]
    public async Task RecordPresentationAsync_LimitedUse_ConsumesWhenLimitReached()
    {
        var cred = CreateCredential(usagePolicy: "LimitedUse", maxPresentations: 3);
        await _store.StoreAsync(cred);

        // First two presentations don't consume
        (await _store.RecordPresentationAsync("cred-1")).Should().BeFalse();
        (await _store.RecordPresentationAsync("cred-1")).Should().BeFalse();

        // Third presentation consumes
        var consumed = await _store.RecordPresentationAsync("cred-1");
        consumed.Should().BeTrue();

        var updated = await _store.GetByIdAsync("cred-1");
        updated!.Status.Should().Be(CredentialStatus.Consumed);
        updated.PresentationCount.Should().Be(3);
    }

    [Fact]
    public async Task RecordPresentationAsync_Reusable_NeverConsumes()
    {
        var cred = CreateCredential(usagePolicy: "Reusable");
        await _store.StoreAsync(cred);

        for (int i = 0; i < 5; i++)
        {
            var consumed = await _store.RecordPresentationAsync("cred-1");
            consumed.Should().BeFalse();
        }

        var updated = await _store.GetByIdAsync("cred-1");
        updated!.Status.Should().Be(CredentialStatus.Active);
        updated.PresentationCount.Should().Be(5);
    }

    [Fact]
    public async Task RecordPresentationAsync_NonActiveCredential_ReturnsFalse()
    {
        var cred = CreateCredential(status: CredentialStatus.Suspended);
        await _store.StoreAsync(cred);

        var consumed = await _store.RecordPresentationAsync("cred-1");
        consumed.Should().BeFalse();
    }

    // ===== Feature 106 Wave C — PatchStatusAsync state machine =====

    [Fact]
    public async Task PatchStatusAsync_PendingAcceptance_To_Active_Succeeds()
    {
        var cred = CreateCredential(status: CredentialStatus.PendingAcceptance);
        await _store.StoreAsync(cred);

        var result = await _store.PatchStatusAsync("wallet-1", "cred-1", CredentialStatus.Active);

        result.Should().NotBeNull();
        result!.Status.Should().Be(CredentialStatus.Active);
    }

    [Fact]
    public async Task PatchStatusAsync_PendingAcceptance_To_Declined_Succeeds()
    {
        var cred = CreateCredential(status: CredentialStatus.PendingAcceptance);
        await _store.StoreAsync(cred);

        var result = await _store.PatchStatusAsync("wallet-1", "cred-1", CredentialStatus.Declined);

        result.Should().NotBeNull();
        result!.Status.Should().Be(CredentialStatus.Declined);
    }

    [Fact]
    public async Task PatchStatusAsync_IdempotentNoOp_Succeeds()
    {
        // Accept twice — second call is a no-op, not a 409.
        var cred = CreateCredential(status: CredentialStatus.Active);
        await _store.StoreAsync(cred);

        var result = await _store.PatchStatusAsync("wallet-1", "cred-1", CredentialStatus.Active);

        result.Should().NotBeNull();
        result!.Status.Should().Be(CredentialStatus.Active);
    }

    [Fact]
    public async Task PatchStatusAsync_Active_To_PendingAcceptance_Throws()
    {
        // INV-4: once accepted, cannot return to pending.
        var cred = CreateCredential(status: CredentialStatus.Active);
        await _store.StoreAsync(cred);

        var act = async () =>
            await _store.PatchStatusAsync("wallet-1", "cred-1", CredentialStatus.PendingAcceptance);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*invalid-transition*");
    }

    [Fact]
    public async Task PatchStatusAsync_Declined_To_Active_Throws()
    {
        // INV-3: declined is terminal.
        var cred = CreateCredential(status: CredentialStatus.Declined);
        await _store.StoreAsync(cred);

        var act = async () =>
            await _store.PatchStatusAsync("wallet-1", "cred-1", CredentialStatus.Active);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*invalid-transition*");
    }

    [Fact]
    public async Task PatchStatusAsync_WrongWallet_ReturnsNull()
    {
        var cred = CreateCredential(status: CredentialStatus.PendingAcceptance);
        await _store.StoreAsync(cred);

        var result = await _store.PatchStatusAsync("wallet-OTHER", "cred-1", CredentialStatus.Active);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PatchStatusAsync_NotFound_ReturnsNull()
    {
        var result = await _store.PatchStatusAsync("wallet-1", "does-not-exist", CredentialStatus.Active);

        result.Should().BeNull();
    }
}
