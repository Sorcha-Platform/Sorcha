// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Storage;

namespace Sorcha.Blueprint.Service.Tests.Storage;

public class InMemoryPublishOverrideStoreTests
{
    private readonly InMemoryPublishOverrideStore _store = new();

    private static PublishOverride CreateOverride(
        string blueprintId = "bp-1",
        int version = 1,
        string reason = "ship it",
        DateTimeOffset? overriddenAt = null) => new()
    {
        Id = Guid.NewGuid(),
        BlueprintId = blueprintId,
        Version = version,
        RegisterId = "reg-1",
        ExecDefHash = "hash-1",
        OverriddenByPlatformUserId = Guid.NewGuid(),
        OverriddenAt = overriddenAt ?? DateTimeOffset.UtcNow,
        Reason = reason
    };

    [Fact]
    public async Task RecordAsync_ThenGetByBlueprint_ReturnsRecord()
    {
        var record = CreateOverride();

        await _store.RecordAsync(record);
        var results = await _store.GetByBlueprintAsync(record.BlueprintId);

        results.Should().ContainSingle();
        results[0].Id.Should().Be(record.Id);
        results[0].Reason.Should().Be("ship it");
    }

    [Fact]
    public async Task RecordAsync_MultipleRecords_AllAppended_NeverOverwritten()
    {
        var first = CreateOverride(version: 1, reason: "first", overriddenAt: DateTimeOffset.UtcNow.AddHours(-2));
        var second = CreateOverride(version: 2, reason: "second", overriddenAt: DateTimeOffset.UtcNow.AddHours(-1));

        await _store.RecordAsync(first);
        await _store.RecordAsync(second);

        var results = await _store.GetByBlueprintAsync("bp-1");

        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().Contain(new[] { first.Id, second.Id });
    }

    [Fact]
    public async Task GetByBlueprintAsync_ReturnsNewestFirst()
    {
        var older = CreateOverride(reason: "older", overriddenAt: DateTimeOffset.UtcNow.AddHours(-2));
        var newer = CreateOverride(reason: "newer", overriddenAt: DateTimeOffset.UtcNow.AddHours(-1));

        await _store.RecordAsync(older);
        await _store.RecordAsync(newer);

        var results = await _store.GetByBlueprintAsync("bp-1");

        results.Should().HaveCount(2);
        results[0].Id.Should().Be(newer.Id);
        results[1].Id.Should().Be(older.Id);
    }

    [Fact]
    public async Task GetByBlueprintAsync_OtherBlueprint_ReturnsEmpty()
    {
        await _store.RecordAsync(CreateOverride(blueprintId: "bp-1"));

        var results = await _store.GetByBlueprintAsync("bp-other");

        results.Should().BeEmpty();
    }
}
