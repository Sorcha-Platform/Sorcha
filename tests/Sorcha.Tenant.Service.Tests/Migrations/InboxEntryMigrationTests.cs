// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Migrations;

/// <summary>
/// Feature 118 T054 — guards the EF model that backs the InboxEntry
/// migration (originally <c>20260505175116_AddInboxEntry</c>, squashed
/// into <c>InitialCreate</c> pre-release): the table exists in the
/// configured schema, every required column is present with the expected
/// nullability + length, and the four indexes on
/// <c>PlatformUserId</c>-prefixed columns are declared (one of them
/// unique, on <c>(PlatformUserId, SourceEventId)</c>).
/// </summary>
/// <remarks>
/// Uses an in-memory <see cref="Sorcha.Tenant.Service.Data.TenantDbContext"/> instance
/// to introspect the EF model, which is what the migration was generated
/// from. Postgres-specific column types (e.g. <c>character varying(256)</c>)
/// aren't asserted here — those are pinned in the migration <c>.cs</c>
/// itself and exercised end-to-end by the Docker integration suite.
/// </remarks>
public class InboxEntryMigrationTests
{
    [Fact]
    public void Model_ContainsInboxEntryEntityInPublicSchema()
    {
        using var db = InMemoryDbContextFactory.Create();
        var entity = db.Model.FindEntityType(typeof(InboxEntry));

        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("InboxEntries");
        // Schema only sticks on Postgres provider; in-memory drops it.
        // Production migration sets "public" — verified by the migration .cs
        // file's CreateTable(schema: "public", ...).
    }

    [Theory]
    [InlineData(nameof(InboxEntry.Id), false)]
    [InlineData(nameof(InboxEntry.PlatformUserId), false)]
    [InlineData(nameof(InboxEntry.Category), false)]
    [InlineData(nameof(InboxEntry.Severity), false)]
    [InlineData(nameof(InboxEntry.CorrelationKey), false)]
    [InlineData(nameof(InboxEntry.DetailHref), false)]
    [InlineData(nameof(InboxEntry.SourceEventId), false)]
    [InlineData(nameof(InboxEntry.OccurredAt), false)]
    [InlineData(nameof(InboxEntry.ReadAt), true)]
    [InlineData(nameof(InboxEntry.DismissedAt), true)]
    [InlineData(nameof(InboxEntry.Title), false)]
    [InlineData(nameof(InboxEntry.Summary), true)]
    [InlineData(nameof(InboxEntry.IconKey), true)]
    [InlineData(nameof(InboxEntry.ChannelHints), false)]
    [InlineData(nameof(InboxEntry.WriterServiceId), true)]
    public void EntityProperty_HasExpectedNullability(string propertyName, bool nullable)
    {
        using var db = InMemoryDbContextFactory.Create();
        var entity = db.Model.FindEntityType(typeof(InboxEntry))!;
        var property = entity.FindProperty(propertyName);

        property.Should().NotBeNull($"InboxEntry must declare '{propertyName}'");
        property!.IsNullable.Should().Be(nullable, $"'{propertyName}' nullability");
    }

    [Theory]
    [InlineData(nameof(InboxEntry.CorrelationKey), 256)]
    [InlineData(nameof(InboxEntry.DetailHref), 1024)]
    [InlineData(nameof(InboxEntry.Title), 200)]
    [InlineData(nameof(InboxEntry.Summary), 1000)]
    [InlineData(nameof(InboxEntry.IconKey), 64)]
    public void StringProperty_HasMaxLength(string propertyName, int maxLength)
    {
        using var db = InMemoryDbContextFactory.Create();
        var entity = db.Model.FindEntityType(typeof(InboxEntry))!;
        var property = entity.FindProperty(propertyName)!;

        property.GetMaxLength().Should().Be(maxLength,
            $"'{propertyName}' max length matches the migration column type");
    }

    [Fact]
    public void Indexes_CoverPlatformUserIdAndSourceEventId_Uniquely()
    {
        var indexes = LoadIndexes();

        var sourceEventIdx = indexes.FirstOrDefault(IsPlatformUserIdSourceEventIdIndex);
        sourceEventIdx.Should().NotBeNull(
            "every (PlatformUserId, SourceEventId) write must collide on the unique index for idempotent re-writes");
        sourceEventIdx!.IsUnique.Should().BeTrue("(PlatformUserId, SourceEventId) must be unique");
    }

    [Fact]
    public void Indexes_CoverPagedQueryShapes()
    {
        var keysets = LoadIndexes().Select(IndexKeyset).ToList();

        HasIndex(keysets, nameof(InboxEntry.PlatformUserId), nameof(InboxEntry.OccurredAt))
            .Should().BeTrue("list-page query orders by OccurredAt within the user");

        HasIndex(keysets, nameof(InboxEntry.PlatformUserId), nameof(InboxEntry.Category), nameof(InboxEntry.OccurredAt))
            .Should().BeTrue("category-filter query needs (PlatformUserId, Category, OccurredAt)");

        HasIndex(keysets, nameof(InboxEntry.PlatformUserId), nameof(InboxEntry.CorrelationKey), nameof(InboxEntry.OccurredAt))
            .Should().BeTrue("correlation-grouping query needs (PlatformUserId, CorrelationKey, OccurredAt)");
    }

    private static bool HasIndex(IEnumerable<string[]> keysets, params string[] expected) =>
        keysets.Any(k => k.SequenceEqual(expected));

    [Fact]
    public void PlatformUserId_HasCascadeDeleteFromPlatformUsers()
    {
        using var db = InMemoryDbContextFactory.Create();
        var entity = db.Model.FindEntityType(typeof(InboxEntry))!;
        var fk = entity.GetForeignKeys()
            .Single(f => f.Properties.Any(p => p.Name == nameof(InboxEntry.PlatformUserId)));

        fk.DeleteBehavior.Should().Be(DeleteBehavior.Cascade,
            "deleting a PlatformUser must remove their InboxEntries");
    }

    private static IReadOnlyList<IIndex> LoadIndexes()
    {
        using var db = InMemoryDbContextFactory.Create();
        var entity = db.Model.FindEntityType(typeof(InboxEntry))!;
        return entity.GetIndexes().ToList();
    }

    private static bool IsPlatformUserIdSourceEventIdIndex(IIndex idx)
    {
        var keys = IndexKeyset(idx);
        return keys.SequenceEqual(new[] { nameof(InboxEntry.PlatformUserId), nameof(InboxEntry.SourceEventId) });
    }

    private static string[] IndexKeyset(IIndex idx) => idx.Properties.Select(p => p.Name).ToArray();
}
