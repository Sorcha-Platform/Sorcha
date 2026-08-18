// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Services;

namespace Sorcha.Blueprint.Service.Tests.StatusList;

public class StatusListManagerTests
{
    private readonly StatusListManager _manager;
    private readonly Mock<ILogger<StatusListManager>> _loggerMock = new();

    public StatusListManagerTests()
    {
        var urls = new Sorcha.Blueprint.Service.Configuration.StatusListUrls.Resolved(
            "https://test.example/api/v1/credentials/status-lists",
            "https://test.example/api/v1/credentials/ietf-status-lists");

        // #1482: the manager is now backed by a durable store and reconciles against the register.
        // These tests exercise the in-memory store and an empty register, which is the same
        // behaviour they asserted before — the list starts empty and nothing has been revoked.
        var register = new Mock<Sorcha.ServiceClients.Register.IRegisterServiceClient>();
        register.Setup(r => r.GetTransactionsAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.ServiceClients.Register.TransactionPage
            {
                Page = 1,
                PageSize = 100,
                Total = 0,
                Transactions = []
            });

        var reconciler = new StatusListLedgerReconciler(
            register.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StatusListLedgerReconciler>.Instance);

        _manager = new StatusListManager(
            _loggerMock.Object,
            urls,
            new Sorcha.Blueprint.Service.Storage.InMemoryStatusListStore(),
            reconciler);
    }

    // ===== GetOrCreateListAsync Tests =====

    [Fact]
    public async Task GetOrCreateListAsync_CreatesNewList()
    {
        var list = await _manager.GetOrCreateListAsync("issuer-1", "register-1", "revocation");

        list.Should().NotBeNull();
        list.IssuerWallet.Should().Be("issuer-1");
        list.RegisterId.Should().Be("register-1");
        list.Purpose.Should().Be("revocation");
    }

    [Fact]
    public async Task GetOrCreateListAsync_ReturnsSameListOnSecondCall()
    {
        var list1 = await _manager.GetOrCreateListAsync("issuer-1", "register-1", "revocation");
        var list2 = await _manager.GetOrCreateListAsync("issuer-1", "register-1", "revocation");

        list1.Should().BeSameAs(list2);
    }

    [Fact]
    public async Task GetOrCreateListAsync_DifferentPurpose_CreatesSeparateList()
    {
        var revocation = await _manager.GetOrCreateListAsync("issuer-1", "register-1", "revocation");
        var suspension = await _manager.GetOrCreateListAsync("issuer-1", "register-1", "suspension");

        revocation.Id.Should().NotBe(suspension.Id);
    }

    // ===== AllocateIndexAsync Tests =====

    [Fact]
    public async Task AllocateIndexAsync_ReturnsSequentialIndices()
    {
        var alloc1 = await _manager.AllocateIndexAsync("issuer-1", "register-1", "cred-1");
        var alloc2 = await _manager.AllocateIndexAsync("issuer-1", "register-1", "cred-2");
        var alloc3 = await _manager.AllocateIndexAsync("issuer-1", "register-1", "cred-3");

        alloc1.Index.Should().Be(0);
        alloc2.Index.Should().Be(1);
        alloc3.Index.Should().Be(2);
    }

    [Fact]
    public async Task AllocateIndexAsync_ReturnsCorrectListId()
    {
        var alloc = await _manager.AllocateIndexAsync("issuer-1", "register-1", "cred-1");

        alloc.ListId.Should().Be("issuer-1-register-1-revocation-1");
    }

    [Fact]
    public async Task AllocateIndexAsync_ReturnsStatusListUrl()
    {
        var alloc = await _manager.AllocateIndexAsync("issuer-1", "register-1", "cred-1");

        alloc.StatusListUrl.Should().Be(
            "https://test.example/api/v1/credentials/status-lists/issuer-1-register-1-revocation-1");
    }

    [Fact]
    public async Task AllocateIndexAsync_NullCredentialId_AllocatesNormally()
    {
        // #220: pre-allocation happens before the credential is signed, so credentialId is null.
        // Allocation is keyed by (listId, index) — it must succeed and return a normal allocation.
        var alloc = await _manager.AllocateIndexAsync("issuer-1", "register-1", credentialId: null);

        alloc.Index.Should().Be(0);
        alloc.ListId.Should().Be("issuer-1-register-1-revocation-1");
        alloc.StatusListUrl.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AllocateIndexAsync_WhenFull_ThrowsInvalidOperationException()
    {
        // Pre-create a list and fill it
        var list = await _manager.GetOrCreateListAsync("issuer-1", "register-1", "revocation");
        list.NextAvailableIndex = list.Size; // Simulate full

        var act = () => _manager.AllocateIndexAsync("issuer-1", "register-1", "cred-overflow");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*full*");
    }

    // ===== SetBitAsync Tests =====

    [Fact]
    public async Task SetBitAsync_SetsAndReturnsBitUpdate()
    {
        var alloc = await _manager.AllocateIndexAsync("issuer-1", "register-1", "cred-1");

        var update = await _manager.SetBitAsync(alloc.ListId, alloc.Index, true, "revoked");

        update.ListId.Should().Be(alloc.ListId);
        update.Index.Should().Be(alloc.Index);
        update.Value.Should().BeTrue();
        update.Version.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task SetBitAsync_ClearBit_ReturnsFalseValue()
    {
        var alloc = await _manager.AllocateIndexAsync("issuer-1", "register-1", "cred-1");
        await _manager.SetBitAsync(alloc.ListId, alloc.Index, true, "suspended");

        var update = await _manager.SetBitAsync(alloc.ListId, alloc.Index, false, "reinstated");

        update.Value.Should().BeFalse();
    }

    [Fact]
    public async Task SetBitAsync_IncrementsVersion()
    {
        var alloc = await _manager.AllocateIndexAsync("issuer-1", "register-1", "cred-1");

        var update1 = await _manager.SetBitAsync(alloc.ListId, alloc.Index, true, "revoked");
        var update2 = await _manager.SetBitAsync(alloc.ListId, alloc.Index, false, "reinstated");

        update2.Version.Should().BeGreaterThan(update1.Version);
    }

    [Fact]
    public async Task SetBitAsync_NonExistentList_ThrowsKeyNotFoundException()
    {
        var act = () => _manager.SetBitAsync("nonexistent-list", 0, true, "test");
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ===== GetListAsync Tests =====

    [Fact]
    public async Task GetListAsync_ExistingList_ReturnsList()
    {
        var created = await _manager.GetOrCreateListAsync("issuer-1", "register-1", "revocation");

        var retrieved = await _manager.GetListAsync(created.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetListAsync_NonExistentList_ReturnsNull()
    {
        var result = await _manager.GetListAsync("nonexistent");
        result.Should().BeNull();
    }

    // ===== Thread Safety Tests =====

    [Fact]
    public async Task AllocateIndexAsync_ConcurrentAllocations_NoConflicts()
    {
        // Pre-create the list
        await _manager.GetOrCreateListAsync("issuer-1", "register-1", "revocation");

        var tasks = Enumerable.Range(0, 100)
            .Select(i => _manager.AllocateIndexAsync("issuer-1", "register-1", $"cred-{i}"))
            .ToList();

        var results = await Task.WhenAll(tasks);
        var indices = results.Select(r => r.Index).ToList();

        // All indices should be unique
        indices.Distinct().Count().Should().Be(100);
        // Indices should be 0-99
        indices.Min().Should().Be(0);
        indices.Max().Should().Be(99);
    }
}
