// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Storage;

namespace Sorcha.Blueprint.Service.Tests.Storage;

public class InMemoryInstanceStoreTests
{
    private readonly InMemoryInstanceStore _store = new();

    private static Instance CreateInstance(string id = "inst-1") => new()
    {
        Id = id,
        BlueprintId = "bp-1",
        BlueprintVersion = 1,
        RegisterId = "reg-1",
        TenantId = "tenant-1"
    };

    [Fact]
    public async Task CreateAsync_ValidInstance_ReturnsInstance()
    {
        var instance = CreateInstance();

        var result = await _store.CreateAsync(instance);

        result.Should().BeSameAs(instance);
    }

    [Fact]
    public async Task CreateAsync_DuplicateId_ThrowsInvalidOperationException()
    {
        await _store.CreateAsync(CreateInstance("dup"));

        var act = () => _store.CreateAsync(CreateInstance("dup"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public void CreateAsync_EmptyId_ThrowsArgumentException()
    {
        var instance = new Instance
        {
            Id = "",
            BlueprintId = "bp",
            BlueprintVersion = 1,
            RegisterId = "reg",
            TenantId = "t"
        };

        var act = () => _store.CreateAsync(instance);

        act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetAsync_ExistingId_ReturnsInstance()
    {
        var instance = CreateInstance();
        await _store.CreateAsync(instance);

        var result = await _store.GetAsync("inst-1");

        result.Should().NotBeNull();
        result!.Id.Should().Be("inst-1");
    }

    [Fact]
    public async Task GetAsync_NonExistentId_ReturnsNull()
    {
        var result = await _store.GetAsync("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_MatchingVersion_IncrementsVersionAndUpdatesTimestamp()
    {
        var instance = CreateInstance();
        await _store.CreateAsync(instance);

        instance.State = InstanceState.Completed;
        var result = await _store.UpdateAsync(instance);

        result.Version.Should().Be(1);
        result.State.Should().Be(InstanceState.Completed);
    }

    [Fact]
    public async Task UpdateAsync_VersionMismatch_ThrowsConcurrencyException()
    {
        var instance = CreateInstance();
        await _store.CreateAsync(instance);

        // Simulate concurrent update by manually bumping version
        instance.State = InstanceState.Completed;
        await _store.UpdateAsync(instance); // version 0 → 1

        // Now try updating with stale version 0
        var staleInstance = CreateInstance();
        staleInstance.State = InstanceState.Cancelled;

        var act = () => _store.UpdateAsync(staleInstance);

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact]
    public async Task UpdateAsync_NonExistentInstance_ThrowsInvalidOperationException()
    {
        var instance = CreateInstance("missing");

        var act = () => _store.UpdateAsync(instance);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task GetByBlueprintAsync_FiltersCorrectly()
    {
        await _store.CreateAsync(new Instance { Id = "i1", BlueprintId = "bp-A", BlueprintVersion = 1, RegisterId = "r", TenantId = "t" });
        await _store.CreateAsync(new Instance { Id = "i2", BlueprintId = "bp-B", BlueprintVersion = 1, RegisterId = "r", TenantId = "t" });
        await _store.CreateAsync(new Instance { Id = "i3", BlueprintId = "bp-A", BlueprintVersion = 1, RegisterId = "r", TenantId = "t" });

        var result = (await _store.GetByBlueprintAsync("bp-A")).ToList();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(i => i.BlueprintId == "bp-A");
    }

    [Fact]
    public async Task GetByBlueprintAsync_WithStateFilter_FiltersCorrectly()
    {
        var active = new Instance { Id = "i1", BlueprintId = "bp-1", BlueprintVersion = 1, RegisterId = "r", TenantId = "t", State = InstanceState.Active };
        var completed = new Instance { Id = "i2", BlueprintId = "bp-1", BlueprintVersion = 1, RegisterId = "r", TenantId = "t", State = InstanceState.Completed };
        await _store.CreateAsync(active);
        await _store.CreateAsync(completed);

        var result = (await _store.GetByBlueprintAsync("bp-1", InstanceState.Active)).ToList();

        result.Should().ContainSingle();
        result[0].Id.Should().Be("i1");
    }

    [Fact]
    public async Task GetByRegisterAsync_FiltersCorrectly()
    {
        await _store.CreateAsync(new Instance { Id = "i1", BlueprintId = "bp", BlueprintVersion = 1, RegisterId = "reg-A", TenantId = "t" });
        await _store.CreateAsync(new Instance { Id = "i2", BlueprintId = "bp", BlueprintVersion = 1, RegisterId = "reg-B", TenantId = "t" });

        var result = (await _store.GetByRegisterAsync("reg-A")).ToList();

        result.Should().ContainSingle();
        result[0].RegisterId.Should().Be("reg-A");
    }

    [Fact]
    public async Task GetByParticipantWalletAsync_FiltersCorrectly()
    {
        await _store.CreateAsync(new Instance
        {
            Id = "i1", BlueprintId = "bp", BlueprintVersion = 1, RegisterId = "r", TenantId = "t",
            ParticipantWallets = { ["buyer"] = "0xAAA" }
        });
        await _store.CreateAsync(new Instance
        {
            Id = "i2", BlueprintId = "bp", BlueprintVersion = 1, RegisterId = "r", TenantId = "t",
            ParticipantWallets = { ["seller"] = "0xBBB" }
        });

        var result = (await _store.GetByParticipantWalletAsync("0xAAA")).ToList();

        result.Should().ContainSingle();
        result[0].Id.Should().Be("i1");
    }

    [Fact]
    public async Task GetByBlueprintAsync_Pagination_RespectsSkipAndTake()
    {
        for (int i = 0; i < 5; i++)
            await _store.CreateAsync(new Instance { Id = $"i{i}", BlueprintId = "bp", BlueprintVersion = 1, RegisterId = "r", TenantId = "t" });

        var result = (await _store.GetByBlueprintAsync("bp", skip: 1, take: 2)).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteAsync_ExistingInstance_ReturnsTrue()
    {
        await _store.CreateAsync(CreateInstance());

        var result = await _store.DeleteAsync("inst-1");

        result.Should().BeTrue();
        (await _store.GetAsync("inst-1")).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonExistentInstance_ReturnsFalse()
    {
        var result = await _store.DeleteAsync("missing");

        result.Should().BeFalse();
    }
}
