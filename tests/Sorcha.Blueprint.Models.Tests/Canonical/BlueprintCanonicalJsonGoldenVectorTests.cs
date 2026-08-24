// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models.Canonical;

namespace Sorcha.Blueprint.Models.Tests.Canonical;

/// <summary>
/// The golden vector (Feature 195, contracts/publication-identity.md §5).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this class is for.</b> Once a definition's identity is the hash of its canonical bytes,
/// everything that can vary in the serialization becomes part of the ledger contract. Most of it is
/// pinned by attributes on the model rather than by a serializer setting — which reads like safety
/// and is not: renaming a <c>[JsonPropertyName]</c>, or adding a <c>JsonIgnoreCondition</c>, is a
/// refactor with <b>no compile-time consequence and no other test that would notice</b>, and it
/// silently changes the identity of every definition on every register.
/// </para>
/// <para>
/// These two frozen constants are the only guard that catches that. They are expected to change
/// <b>only</b> in a change that deliberately re-identifies every definition — which, per the
/// contract, also takes the domain tag to <c>v2</c>.
/// </para>
/// </remarks>
public class BlueprintCanonicalJsonGoldenVectorTests
{
    private const string Register = "b21d862d7aee471c89f844defb7fd108";
    private const string BlueprintId = "golden-vector-blueprint";

    /// <summary>
    /// Frozen publication id of the fixture's own bytes. Changes if the CANONICALISER changes.
    /// </summary>
    private const string ExpectedFixtureId =
        "642d23f7dfa1085ad8f2a4cece45478d494711f8a9011fe9878d44a2ccdab0d0";

    /// <summary>
    /// Frozen publication id of the fixture after a round trip through <see cref="Blueprint"/>.
    /// Changes if any <c>[JsonPropertyName]</c> or null-handling attribute on the blueprint object
    /// graph changes — the ledger-contract guard.
    /// </summary>
    private const string ExpectedModelRoundTripId =
        "b6ebf9bc4a15c6ac6f6e8ad3913b77f94016542f7854fb0e4a9f794649696425";

    [Fact]
    public void GoldenVector_CanonicaliserIsFrozen()
    {
        var canonical = BlueprintCanonicalJson.Canonicalise(CanonicalTestPaths.GoldenBlueprintJson());

        BlueprintPublicationId.Compute(Register, BlueprintId, canonical)
            .Should().Be(ExpectedFixtureId,
                "the canonical form of the fixture changed. If that was deliberate, the domain tag " +
                "goes to v2 and every definition on every register is re-identified — see " +
                "contracts/publication-identity.md §1");
    }

    /// <summary>
    /// The ledger-contract guard. Deserialise the fixture into the real model, serialise it back, and
    /// freeze the id of the result — so the serialized <i>shape of the model</i> is pinned, not just
    /// the canonicaliser.
    /// </summary>
    [Fact]
    public void GoldenVector_ModelWireShapeIsFrozen()
    {
        var model = JsonSerializer.Deserialize<Blueprint>(CanonicalTestPaths.GoldenBlueprintJson());
        model.Should().NotBeNull("the fixture must bind to the model, or this guard proves nothing");

        var reserialised = JsonSerializer.Serialize(model);
        var canonical = BlueprintCanonicalJson.Canonicalise(reserialised);

        BlueprintPublicationId.Compute(Register, BlueprintId, canonical)
            .Should().Be(ExpectedModelRoundTripId,
                "a [JsonPropertyName] or null-handling attribute on the blueprint graph changed. " +
                "That is a change to the LEDGER CONTRACT: it silently re-identifies every definition " +
                "on every register, with no compile error and no other failing test");
    }

    /// <summary>
    /// The round trip must be deterministic, or the vector above is flaky rather than frozen.
    /// </summary>
    /// <remarks>
    /// <b>This caught a real property of the model.</b> <c>Blueprint.CreatedAt</c> and
    /// <c>Blueprint.UpdatedAt</c> default to <c>DateTimeOffset.UtcNow</c>, so a fixture that omitted
    /// them produced a different id on every run — two consecutive runs of the same code differed.
    /// The fixture therefore pins both. The wider consequence is real and deliberate: those
    /// timestamps are part of the definition's content and therefore part of its identity. That is
    /// sound because they are stamped on <i>draft save</i>, not at publish
    /// (<c>InMemoryBlueprintStore.UpdateAsync</c>), so republishing an untouched draft is genuinely
    /// idempotent — while any edit produces a new publication, which is what should happen.
    /// </remarks>
    [Fact]
    public void GoldenVector_ModelRoundTripIsDeterministic()
    {
        static string RoundTrip()
        {
            var model = JsonSerializer.Deserialize<Blueprint>(CanonicalTestPaths.GoldenBlueprintJson());
            return BlueprintCanonicalJson.Canonicalise(JsonSerializer.Serialize(model));
        }

        RoundTrip().Should().Be(RoundTrip(),
            "a model default of DateTimeOffset.UtcNow or Guid.NewGuid() reaching the wire would make " +
            "the identity non-deterministic — the fixture must pin every such field");
    }

    /// <summary>
    /// Guards the guard. If the fixture ever stopped exercising the graph — trimmed to a stub, or
    /// silently failing to bind — both vectors above would still be "frozen" while checking almost
    /// nothing.
    /// </summary>
    [Fact]
    public void GoldenVector_FixtureActuallyExercisesTheGraph()
    {
        var model = JsonSerializer.Deserialize<Blueprint>(CanonicalTestPaths.GoldenBlueprintJson());

        model!.Participants.Should().HaveCountGreaterThanOrEqualTo(2);
        model.Actions.Should().HaveCountGreaterThanOrEqualTo(2);
        model.Actions.Should().Contain(a => a.RejectionConfig != null,
            "RejectionConfig is one of the fields the F194 pin omitted — the fixture must carry it");
        model.Actions.Should().Contain(a => a.Routes != null && a.Routes.Any(r => r.DecisionNotice != null),
            "x-decision-notice is another of the omitted fields");
        model.Actions.Should().Contain(a => a.CredentialIssuanceConfig != null);
        model.InstanceReference.Should().NotBeNull();
        model.PresentationConfig.Should().NotBeNull();
    }

    /// <summary>
    /// The fixture must contain characters an escaping-sensitive serializer would treat differently,
    /// so that "escaping does not survive a parse" is exercised by the vector rather than only by the
    /// unit tests.
    /// </summary>
    [Fact]
    public void GoldenVector_FixtureCarriesEscapeSensitiveCharacters()
    {
        var raw = CanonicalTestPaths.GoldenBlueprintJson();

        raw.Should().ContainAny("&", "<", ">");
        raw.Should().Contain("é");
    }
}
