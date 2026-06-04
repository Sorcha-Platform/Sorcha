// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Schemas.Models;
using Sorcha.Blueprint.Schemas.Repositories;
using Xunit;

namespace Sorcha.Blueprint.Schemas.Tests;

/// <summary>
/// Unit tests for <see cref="InMemorySchemaIndexRepository"/> — the no-Mongo fallback.
/// Mirrors the behavioural contract of <see cref="MongoSchemaIndexRepository"/>:
/// composite-key upsert with content-hash short-circuit, Title-sorted Id-cursor search,
/// and Active-by-default status filtering.
/// </summary>
public class InMemorySchemaIndexRepositoryTests
{
    private static SchemaIndexEntryDocument Entry(
        string provider, string uri, string title,
        string? hash = null, string[]? sectors = null,
        SchemaIndexStatus status = SchemaIndexStatus.Active)
        => new()
        {
            SourceProvider = provider,
            SourceUri = uri,
            Title = title,
            SectorTags = sectors ?? new[] { "general" },
            ContentHash = hash,
            Status = status.ToString(),
        };

    [Fact]
    public async Task UpsertAsync_NewEntry_ReturnsTrueAndAssignsIdAndShortCode()
    {
        var repo = new InMemorySchemaIndexRepository();
        var entry = Entry("w3c", "uri-1", "Alpha", hash: "h1");

        var inserted = await repo.UpsertAsync(entry);

        inserted.Should().BeTrue();
        entry.Id.Should().NotBeNullOrEmpty();
        entry.ShortCode.Should().NotBeNullOrEmpty();
        (await repo.GetTotalCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpsertAsync_UnchangedContentHash_ReturnsFalseAndDoesNotDuplicate()
    {
        var repo = new InMemorySchemaIndexRepository();
        await repo.UpsertAsync(Entry("w3c", "uri-1", "Alpha", hash: "h1"));

        var second = await repo.UpsertAsync(Entry("w3c", "uri-1", "Alpha", hash: "h1"));

        second.Should().BeFalse();
        (await repo.GetTotalCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpsertAsync_ChangedContentHash_UpdatesAndPreservesIdShortCodeDateAdded()
    {
        var repo = new InMemorySchemaIndexRepository();
        var first = Entry("w3c", "uri-1", "Alpha", hash: "h1");
        await repo.UpsertAsync(first);
        var originalId = first.Id;
        var originalShortCode = first.ShortCode;
        var originalDateAdded = first.DateAdded;

        var updated = Entry("w3c", "uri-1", "Alpha v2", hash: "h2");
        var changed = await repo.UpsertAsync(updated);

        changed.Should().BeTrue();
        updated.Id.Should().Be(originalId);
        updated.ShortCode.Should().Be(originalShortCode);
        updated.DateAdded.Should().Be(originalDateAdded);
        (await repo.GetTotalCountAsync()).Should().Be(1);
        (await repo.GetByProviderAndUriAsync("w3c", "uri-1"))!.Title.Should().Be("Alpha v2");
    }

    [Fact]
    public async Task GetByShortCodeAsync_ReturnsMatchingEntry()
    {
        var repo = new InMemorySchemaIndexRepository();
        var entry = Entry("w3c", "uri-1", "Alpha", hash: "h1");
        await repo.UpsertAsync(entry);

        var found = await repo.GetByShortCodeAsync(entry.ShortCode);

        found.Should().NotBeNull();
        found!.SourceUri.Should().Be("uri-1");
    }

    [Fact]
    public async Task SearchAsync_FiltersByProviderSectorAndText()
    {
        var repo = new InMemorySchemaIndexRepository();
        await repo.UpsertAsync(Entry("w3c", "u1", "Invoice schema", hash: "h1", sectors: new[] { "finance" }));
        await repo.UpsertAsync(Entry("ubl", "u2", "Invoice doc", hash: "h2", sectors: new[] { "trade" }));
        await repo.UpsertAsync(Entry("w3c", "u3", "Passport", hash: "h3", sectors: new[] { "identity" }));

        var byProvider = await repo.SearchAsync(provider: "w3c");
        byProvider.TotalCount.Should().Be(2);

        var bySector = await repo.SearchAsync(sectors: new[] { "trade" });
        bySector.Results.Should().ContainSingle().Which.SourceProvider.Should().Be("ubl");

        var byText = await repo.SearchAsync(search: "invoice");
        byText.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_DefaultsToActiveStatus_ExcludingDeprecated()
    {
        var repo = new InMemorySchemaIndexRepository();
        await repo.UpsertAsync(Entry("w3c", "u1", "Active one", hash: "h1"));
        await repo.UpsertAsync(Entry("w3c", "u2", "Old one", hash: "h2"));
        await repo.UpdateProviderStatusAsync("w3c", SchemaIndexStatus.Deprecated);
        await repo.UpsertAsync(Entry("w3c", "u3", "Fresh one", hash: "h3")); // re-Active for u3 only

        var active = await repo.SearchAsync();
        active.Results.Should().OnlyContain(d => d.Status == nameof(SchemaIndexStatus.Active));
        active.Results.Should().ContainSingle(d => d.SourceUri == "u3");
    }

    [Fact]
    public async Task SearchAsync_CursorPagination_WalksAllResultsWithoutOverlap()
    {
        var repo = new InMemorySchemaIndexRepository();
        for (var i = 0; i < 5; i++)
        {
            await repo.UpsertAsync(Entry("w3c", $"u{i}", $"Title {i}", hash: $"h{i}"));
        }

        var page1 = await repo.SearchAsync(limit: 2);
        page1.Results.Should().HaveCount(2);
        page1.NextCursor.Should().NotBeNull();

        var page2 = await repo.SearchAsync(limit: 2, cursor: page1.NextCursor);
        page2.Results.Should().HaveCount(2);

        var page3 = await repo.SearchAsync(limit: 2, cursor: page2.NextCursor);
        page3.Results.Should().HaveCount(1);
        page3.NextCursor.Should().BeNull();

        var allIds = page1.Results.Concat(page2.Results).Concat(page3.Results)
            .Select(d => d.Id).ToList();
        allIds.Should().OnlyHaveUniqueItems();
        allIds.Should().HaveCount(5);
    }

    [Fact]
    public async Task BatchUpsertAsync_ReturnsCountOfChangedEntries()
    {
        var repo = new InMemorySchemaIndexRepository();
        await repo.UpsertAsync(Entry("w3c", "u1", "Alpha", hash: "h1"));

        var changed = await repo.BatchUpsertAsync(new[]
        {
            Entry("w3c", "u1", "Alpha", hash: "h1"),   // unchanged → not counted
            Entry("w3c", "u2", "Beta", hash: "h2"),    // new → counted
            Entry("w3c", "u3", "Gamma", hash: "h3"),   // new → counted
        });

        changed.Should().Be(2);
        (await repo.GetCountByProviderAsync("w3c")).Should().Be(3);
    }
}
