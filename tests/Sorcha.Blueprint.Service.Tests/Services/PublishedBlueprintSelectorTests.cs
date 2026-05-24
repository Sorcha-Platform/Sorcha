// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 137 (C1): instance creation on a replica resolves the blueprint from the published
/// (replicated) store when the draft store has no copy. These tests pin the version-selection
/// contract used by that fallback.
/// </summary>
public class PublishedBlueprintSelectorTests
{
    private static PublishedBlueprint Published(int version, DateTimeOffset publishedAt, BlueprintModel? blueprint = null)
        => new()
        {
            BlueprintId = "bp-1",
            Version = version,
            PublishedAt = publishedAt,
            Blueprint = blueprint ?? new BlueprintModel { Title = $"v{version}" }
        };

    [Fact]
    public void SelectLatest_MultipleVersions_PicksHighestVersion()
    {
        var now = DateTimeOffset.UtcNow;
        var versions = new[]
        {
            Published(1, now.AddMinutes(-10)),
            Published(3, now.AddMinutes(-30)), // older timestamp but highest version
            Published(2, now.AddMinutes(-5)),
        };

        var result = PublishedBlueprintSelector.SelectLatest(versions);

        result.Should().NotBeNull();
        result!.Version.Should().Be(3);
    }

    [Fact]
    public void SelectLatest_SameVersion_PicksNewestPublishedAt()
    {
        var now = DateTimeOffset.UtcNow;
        var older = Published(1, now.AddHours(-1));
        var newer = Published(1, now);

        var result = PublishedBlueprintSelector.SelectLatest(new[] { older, newer });

        result.Should().BeSameAs(newer);
    }

    [Fact]
    public void SelectLatest_IgnoresEntriesWithNoBlueprintPayload()
    {
        var now = DateTimeOffset.UtcNow;
        var withPayload = Published(1, now.AddMinutes(-10));
        var noPayload = new PublishedBlueprint { BlueprintId = "bp-1", Version = 9, PublishedAt = now, Blueprint = null! };

        var result = PublishedBlueprintSelector.SelectLatest(new[] { noPayload, withPayload });

        result.Should().BeSameAs(withPayload);
    }

    [Fact]
    public void SelectLatest_EmptyOrAllNull_ReturnsNull()
    {
        PublishedBlueprintSelector.SelectLatest(Array.Empty<PublishedBlueprint>()).Should().BeNull();

        var allNull = new[] { new PublishedBlueprint { BlueprintId = "bp-1", Version = 1, Blueprint = null! } };
        PublishedBlueprintSelector.SelectLatest(allNull).Should().BeNull();
    }
}
