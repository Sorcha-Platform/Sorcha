// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Models.Canonical;
using Sorcha.ServiceClients.Register;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests;

/// <summary>
/// Feature 195 — a recovered definition whose content does not reproduce the id of the transaction
/// that carried it is rejected and not stored.
/// </summary>
/// <remarks>
/// Was Feature 138 US4 (T050), which compared against a separately-sealed <c>contentHash</c> sibling
/// field. Corrected rather than deleted: the property under test is unchanged (a tampered definition
/// must not be recovered), only the evidence is. The identity IS the digest now, so verification is
/// self-anchoring — two sibling fields can disagree, an id and the content it identifies cannot.
/// </remarks>
public sealed class BlueprintRecoveryProvenanceTests
{
    private const string Register = "b21d862d7aee471c89f844defb7fd108";
    private const string BlueprintId = "permit-v1";
    private const string GenuineBlueprint = """{"title":"Permit","participants":[],"actions":[]}""";

    private static string GenuineId()
        => BlueprintPublicationId.ComputeFromDefinition(Register, BlueprintId, GenuineBlueprint);

    [Fact]
    public void TryVerifyProvenance_GenuineContent_Accepted()
    {
        // The positive case matters as much as the negatives: without it, a verifier that rejected
        // EVERYTHING would pass all three rejection tests below.
        var ok = BlueprintRecoveryService.TryVerifyProvenance(
            Register, BlueprintId, GenuineBlueprint, GenuineId(), out var reason);

        ok.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void TryVerifyProvenance_TamperedContent_Rejected()
    {
        // The transaction's id is over the genuine definition…
        var txId = GenuineId();
        // …but the content served has been tampered (an extra action injected).
        const string tampered = """{"title":"Permit","participants":[],"actions":[{"id":"evil"}]}""";

        var ok = BlueprintRecoveryService.TryVerifyProvenance(
            Register, BlueprintId, tampered, txId, out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("hash_mismatch");
    }

    [Fact]
    public void TryVerifyProvenance_MalformedContent_Rejected()
    {
        var ok = BlueprintRecoveryService.TryVerifyProvenance(
            Register, BlueprintId, "not-json{", GenuineId(), out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("hash_mismatch");
    }

    [Fact]
    public void TryVerifyProvenance_DuplicateKeys_Rejected()
    {
        // A definition that cannot be read unambiguously is refused, not resolved — last-wins would
        // be a silent choice about which of two definitions was published.
        const string duplicate = """{"title":"Permit","title":"Other","participants":[],"actions":[]}""";

        var ok = BlueprintRecoveryService.TryVerifyProvenance(
            Register, BlueprintId, duplicate, GenuineId(), out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("hash_mismatch");
    }

    [Fact]
    public void TryVerifyProvenance_WrongIdForGenuineContent_Rejected()
    {
        var ok = BlueprintRecoveryService.TryVerifyProvenance(
            Register, BlueprintId, GenuineBlueprint, new string('a', 64), out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("hash_mismatch");
    }

    [Fact]
    public void TryVerifyProvenance_NoId_RejectedAsUnverifiable()
    {
        var ok = BlueprintRecoveryService.TryVerifyProvenance(
            Register, BlueprintId, GenuineBlueprint, "", out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("no_provenance");
    }

    /// <summary>
    /// The id is register-scoped, so a definition lifted from one register cannot be presented as
    /// the same definition on another.
    /// </summary>
    [Fact]
    public void TryVerifyProvenance_IdFromAnotherRegister_Rejected()
    {
        var otherRegisterId = BlueprintPublicationId.ComputeFromDefinition(
            "a-different-register", BlueprintId, GenuineBlueprint);

        var ok = BlueprintRecoveryService.TryVerifyProvenance(
            Register, BlueprintId, GenuineBlueprint, otherRegisterId, out var reason);

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
