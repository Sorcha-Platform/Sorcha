// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;

using Microsoft.EntityFrameworkCore;

using FluentAssertions;

using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Data;
using Sorcha.Blueprint.Service.Storage;

namespace Sorcha.Blueprint.Service.Tests.Storage;

/// <summary>
/// Both <see cref="IStatusListStore"/> implementations copy <see cref="BitstringStatusList"/> field by
/// field, by hand — the EF store in THREE places (insert, update, read). A property missing from any
/// of them is saved in memory, reported as saved, and silently lost. #1759 adds the issuing
/// organisation to the list; a list that forgets it after a restart can no longer be signed, so its
/// verifiers fail closed. Asserted over EVERY property, by reflection, so the next field added is
/// covered the day it is added.
/// </summary>
public class StatusListStoreRoundTripTests
{
    /// <summary>Properties not expected to round-trip, each with a reason. Every entry is a hole.</summary>
    private static readonly Dictionary<string, string> NotRoundTripped = new()
    {
        [nameof(BitstringStatusList.RegisterTxId)] =
            "no producer anywhere in src/ ever assigns it, so there is nothing to persist",
    };

    public static TheoryData<string> Stores => new() { "ef", "memory" };

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task SaveThenGet_NewList_EveryPropertySurvives(string kind)
    {
        var store = CreateStore(kind);
        var list = Populated(seed: 1);

        await store.SaveAsync(list);
        var read = await store.GetAsync(list.Id);

        AssertSame(read!, list);
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task SaveThenGet_ExistingList_EveryUpdatedPropertySurvives(string kind)
    {
        var store = CreateStore(kind);
        await store.SaveAsync(Populated(seed: 1));

        var updated = Populated(seed: 2);
        await store.SaveAsync(updated);
        var read = await store.GetAsync(updated.Id);

        AssertSame(read!, updated);
    }

    private static void AssertSame(BitstringStatusList actual, BitstringStatusList expected)
    {
        actual.Should().NotBeNull();
        var checkedCount = 0;
        foreach (var p in Properties())
        {
            if (NotRoundTripped.ContainsKey(p.Name)) continue;
            p.GetValue(actual).Should().Be(p.GetValue(expected), $"'{p.Name}' must survive the store");
            checkedCount++;
        }
        checkedCount.Should().BeGreaterThan(8, "the guard must actually walk the model's properties");
    }

    /// <summary>Sets every writable property to a non-default value derived from <paramref name="seed"/>.</summary>
    private static BitstringStatusList Populated(int seed)
    {
        var list = BitstringStatusList.Create("ws11qissuer", "2141b08339d34c27824536ec250b025e", "revocation");
        foreach (var p in Properties())
        {
            if (p.Name == nameof(BitstringStatusList.Id)) continue; // the key — both saves must hit one row
            object value = Type.GetTypeCode(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) switch
            {
                TypeCode.String => $"{p.Name}-{seed}",
                TypeCode.Int32 => 1000 + seed,
                TypeCode.Int64 => 2000L + seed,
                _ when p.PropertyType == typeof(DateTimeOffset) =>
                    new DateTimeOffset(2026, 9, 29, 10, seed, 0, TimeSpan.Zero),
                _ when (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) == typeof(Guid) =>
                    new Guid($"00000000-0000-0000-0000-{seed:D12}"),
                _ => throw new InvalidOperationException(
                    $"Round-trip guard cannot populate '{p.Name}' ({p.PropertyType}); teach it the type."),
            };
            p.SetValue(list, value);
        }
        return list;
    }

    private static IEnumerable<PropertyInfo> Properties() =>
        typeof(BitstringStatusList).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.CanRead);

    private static IStatusListStore CreateStore(string kind)
    {
        if (kind == "memory") return new InMemoryStatusListStore();

        var options = new DbContextOptionsBuilder<BlueprintDbContext>()
            .UseInMemoryDatabase($"statuslists-{Guid.NewGuid():N}")
            .Options;
        return new EfCoreStatusListStore(new StubContextFactory(options));
    }

    private sealed class StubContextFactory(DbContextOptions<BlueprintDbContext> options)
        : IDbContextFactory<BlueprintDbContext>
    {
        public BlueprintDbContext CreateDbContext() => new(options);

        public Task<BlueprintDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateDbContext());
    }
}
