// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Storage;

namespace Sorcha.Blueprint.Service.Tests.Storage;

public class InMemoryRehearsalPassStoreTests
{
    private readonly InMemoryRehearsalPassStore _store = new();

    private static RehearsalPass CreatePass(
        string blueprintId = "bp-1",
        string execDefHash = "hash-1",
        DateTimeOffset? rehearsedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        BlueprintId = blueprintId,
        ExecDefHash = execDefHash,
        RehearsedAt = rehearsedAt ?? DateTimeOffset.UtcNow,
        RehearsedByPlatformUserId = Guid.NewGuid(),
        SandboxRegisterId = "sandbox-reg-1"
    };

    [Fact]
    public async Task RecordAsync_ThenGetLatest_SameBlueprintSameHash_ReturnsRecordedPass()
    {
        var pass = CreatePass();

        await _store.RecordAsync(pass);
        var result = await _store.GetLatestAsync(pass.BlueprintId, pass.ExecDefHash);

        result.Should().NotBeNull();
        result!.Id.Should().Be(pass.Id);
        result.ExecDefHash.Should().Be(pass.ExecDefHash);
    }

    [Fact]
    public async Task GetLatestAsync_DifferentHash_ReturnsNull()
    {
        var pass = CreatePass(execDefHash: "hash-1");
        await _store.RecordAsync(pass);

        var result = await _store.GetLatestAsync(pass.BlueprintId, "hash-2");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_DifferentBlueprint_ReturnsNull()
    {
        var pass = CreatePass(blueprintId: "bp-1");
        await _store.RecordAsync(pass);

        var result = await _store.GetLatestAsync("bp-other", pass.ExecDefHash);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_TwoPassesSameKey_ReturnsMostRecent()
    {
        var older = CreatePass(rehearsedAt: DateTimeOffset.UtcNow.AddHours(-2));
        var newer = CreatePass(rehearsedAt: DateTimeOffset.UtcNow.AddHours(-1));

        // Record older first, then newer.
        await _store.RecordAsync(older);
        await _store.RecordAsync(newer);

        var result = await _store.GetLatestAsync("bp-1", "hash-1");

        result.Should().NotBeNull();
        result!.Id.Should().Be(newer.Id);
    }

    [Fact]
    public async Task GetLatestAsync_TwoPassesSameKey_OrderIndependentOfInsertOrder()
    {
        var older = CreatePass(rehearsedAt: DateTimeOffset.UtcNow.AddHours(-2));
        var newer = CreatePass(rehearsedAt: DateTimeOffset.UtcNow.AddHours(-1));

        // Record newer first, then older — latest should still be by RehearsedAt.
        await _store.RecordAsync(newer);
        await _store.RecordAsync(older);

        var result = await _store.GetLatestAsync("bp-1", "hash-1");

        result.Should().NotBeNull();
        result!.Id.Should().Be(newer.Id);
    }

    [Fact]
    public async Task GetLatestAsync_NoPasses_ReturnsNull()
    {
        var result = await _store.GetLatestAsync("bp-none", "hash-none");

        result.Should().BeNull();
    }
}
