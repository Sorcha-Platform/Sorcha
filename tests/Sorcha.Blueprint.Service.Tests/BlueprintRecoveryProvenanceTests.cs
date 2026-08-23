// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.ServiceClients.Register;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests;

/// <summary>
/// Feature 138 US4 (T050) — a recovered blueprint whose content does not match the sealed
/// <c>ContentHash</c> is rejected and not stored.
/// </summary>
public sealed class BlueprintRecoveryProvenanceTests
{
    private const string GenuineBlueprint = """{"title":"Permit","participants":[],"actions":[]}""";

    [Fact]
    public void TryVerifyProvenance_TamperedContent_Rejected()
    {
        // The sealed hash is over the genuine blueprint…
        var sealedHash = BlueprintContentHash.Compute(GenuineBlueprint);
        // …but the content served has been tampered (an extra action injected).
        const string tampered = """{"title":"Permit","participants":[],"actions":[{"id":"evil"}]}""";

        var ok = BlueprintRecoveryService.TryVerifyProvenance(tampered, sealedHash, out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("hash_mismatch");
    }

    [Fact]
    public void TryVerifyProvenance_MalformedContent_Rejected()
    {
        var sealedHash = BlueprintContentHash.Compute(GenuineBlueprint);

        var ok = BlueprintRecoveryService.TryVerifyProvenance("not-json{", sealedHash, out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("hash_mismatch");
    }

    [Fact]
    public void TryVerifyProvenance_WrongHashForGenuineContent_Rejected()
    {
        // Genuine content, but the claimed sealed hash is for something else.
        var ok = BlueprintRecoveryService.TryVerifyProvenance(
            GenuineBlueprint,
            contentHash: new string('a', 64),
            out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("hash_mismatch");
    }
}

/// <summary>
/// Feature 194 — recovery must restore <b>every</b> published definition, not the newest per
/// blueprint id.
/// </summary>
/// <remarks>
/// The rule this guards is one expression, and getting it wrong is invisible until a restart: an
/// instance pinned to an earlier definition becomes permanently unresolvable, because the only copy
/// of its definition was discarded during recovery. The symptom is a transaction that never seals,
/// with nothing in the log pointing back here. Step 6 of the live acceptance test exists for exactly
/// this failure.
/// </remarks>
public sealed class BlueprintRecoverySelectsAllDefinitionsTests
{
    private static PublishedBlueprintEntry Entry(string blueprintId, int minutesAgo) => new()
    {
        BlueprintId = blueprintId,
        BlueprintJson = """{"title":"Permit","participants":[],"actions":[]}""",
        PublishedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo),
    };

    [Fact]
    public void EveryPublishedDefinitionOfABlueprint_IsSelectedForRecovery()
    {
        // Three publications of ONE blueprint. The pre-Feature-194 rule collapsed these to one.
        var published = new[] { Entry("bp-1", 30), Entry("bp-1", 20), Entry("bp-1", 10) };

        var selected = BlueprintRecoveryService.SelectDefinitionsToRecover(published);

        selected.Should().HaveCount(3,
            "an instance pinned to any of these must still resolve its definition after a restart");
    }

    [Fact]
    public void DefinitionsAreSelectedOldestFirst_SoOrdinalsMatchPublicationOrder()
    {
        var oldest = Entry("bp-1", 30);
        var middle = Entry("bp-1", 20);
        var newest = Entry("bp-1", 10);

        // Supplied newest-first, to prove the ordering is imposed rather than inherited.
        var selected = BlueprintRecoveryService.SelectDefinitionsToRecover([newest, middle, oldest]);

        selected.Select(e => e.PublishedAt).Should().BeInAscendingOrder();
        selected[0].PublishedAt.Should().Be(oldest.PublishedAt);
    }

    [Fact]
    public void DefinitionsOfSeveralBlueprints_AreAllSelected()
    {
        var published = new[] { Entry("bp-1", 30), Entry("bp-2", 25), Entry("bp-1", 10), Entry("bp-2", 5) };

        var selected = BlueprintRecoveryService.SelectDefinitionsToRecover(published);

        selected.Should().HaveCount(4);
        selected.Count(e => e.BlueprintId == "bp-1").Should().Be(2);
        selected.Count(e => e.BlueprintId == "bp-2").Should().Be(2);
    }

    [Fact]
    public void NoPublishedBlueprints_SelectsNothing()
    {
        BlueprintRecoveryService.SelectDefinitionsToRecover([]).Should().BeEmpty();
    }
}
