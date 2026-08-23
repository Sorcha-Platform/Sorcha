// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Models;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Publishing;

/// <summary>
/// Feature 194 — a published definition must be genuinely immutable, because an instance is pinned
/// to its content hash.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this guards.</b> <c>PublishAsync</c> used to store the object
/// <c>IBlueprintStore.GetAsync</c> returned. For <c>InMemoryBlueprintStore</c> — the registered
/// production implementation — that IS the stored draft, so the "immutable snapshot" comment was
/// false: the publish path itself mutates it twice (<c>FlattenActionSchemas</c>, then the
/// <c>hasCycles</c> metadata write), and any later in-place edit of the draft would silently rewrite
/// the published version too.
/// </para>
/// <para>
/// <b>Why that is fatal here specifically.</b> A pin is a promise that an identifier always denotes
/// the same bytes. If the content behind a hash can change, an instance resolves its pinned entry
/// and receives a definition that was never hashed — and nothing anywhere reports it.
/// </para>
/// <para>
/// These tests use the <b>real in-memory stores</b> rather than mocks, deliberately: the defect is a
/// property of reference semantics, and a mocked store returning a fresh object each call would make
/// the test pass whether or not the fix is present.
/// </para>
/// </remarks>
public class PublishSnapshotIsolationTests
{
    [Fact]
    public async Task PublishedSnapshot_IsUnaffected_ByLaterInPlaceMutationOfTheDraft()
    {
        var draftStore = new InMemoryBlueprintStore();
        var publishedStore = new InMemoryPublishedBlueprintStore();
        var blueprint = CreateBlueprint();
        await draftStore.AddAsync(blueprint);
        var blueprintId = blueprint.Id;

        var service = new PublishService(draftStore, publishedStore);
        var result = await service.PublishAsync(blueprintId, "register-1");
        result.IsSuccess.Should().BeTrue(because: "the fixture blueprint is valid");

        var pinnedHash = result.PublishedBlueprint!.ExecDefHash;
        pinnedHash.Should().NotBeNullOrWhiteSpace();

        // Mutate the DRAFT in place — behaviourally, so the executable definition would change.
        // This is what an editing path does; the published version must not follow it.
        var draft = await draftStore.GetAsync(blueprintId);
        draft!.Actions[0].Title = "Renamed after publication";
        draft.Actions.Add(new ActionModel { Id = 99, Title = "Added after publication", Sender = "p2" });

        var stored = (await publishedStore.GetVersionsAsync(blueprintId)).Single();

        stored.Blueprint.Actions.Should().HaveCount(1,
            because: "the published snapshot is a deep copy, not a live reference to the draft");
        stored.Blueprint.Actions[0].Title.Should().Be("Start");
        stored.ExecDefHash.Should().Be(pinnedHash,
            because: "the content behind a pin must never change — an instance resolving this hash " +
                     "must receive exactly the definition that was hashed");
    }

    [Fact]
    public async Task RecomputingTheHash_OverTheStoredSnapshot_ReproducesThePin()
    {
        // The pin has to be verifiable from the stored bytes alone. If it were computed over a
        // different object than the one stored, this would be the only thing that noticed.
        var draftStore = new InMemoryBlueprintStore();
        var publishedStore = new InMemoryPublishedBlueprintStore();
        var blueprint = CreateBlueprint();
        await draftStore.AddAsync(blueprint);

        var service = new PublishService(draftStore, publishedStore);
        var result = await service.PublishAsync(blueprint.Id, "register-1");

        var stored = result.PublishedBlueprint!;
        new ExecutableDefinitionHasher().ComputeHash(stored.Blueprint).Should().Be(stored.ExecDefHash);
    }

    [Fact]
    public async Task GetByExecDefHash_ResolvesThePinnedDefinition_AndNothingElse()
    {
        var draftStore = new InMemoryBlueprintStore();
        var publishedStore = new InMemoryPublishedBlueprintStore();
        var blueprint = CreateBlueprint();
        await draftStore.AddAsync(blueprint);
        var blueprintId = blueprint.Id;
        var service = new PublishService(draftStore, publishedStore);

        var v1 = await service.PublishAsync(blueprintId, "register-1");

        // A behavioural republish: a second action the first definition did not have.
        var draft = await draftStore.GetAsync(blueprintId);
        draft!.Actions.Add(new ActionModel { Id = 2, Title = "Review", Sender = "p2" });
        var v2 = await service.PublishAsync(blueprintId, "register-1");

        v2.PublishedBlueprint!.ExecDefHash.Should().NotBe(v1.PublishedBlueprint!.ExecDefHash,
            because: "adding an action changes the executable definition");

        var resolvedV1 = await publishedStore.GetByExecDefHashAsync(blueprintId, v1.PublishedBlueprint.ExecDefHash);
        var resolvedV2 = await publishedStore.GetByExecDefHashAsync(blueprintId, v2.PublishedBlueprint.ExecDefHash);

        resolvedV1!.Blueprint.Actions.Should().HaveCount(1,
            because: "an instance pinned to the first definition must still resolve the first definition");
        resolvedV2!.Blueprint.Actions.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByExecDefHash_ReturnsNull_ForAnUnknownHash()
    {
        // Null is a refusal signal. The caller must never read it as licence to fall back to latest —
        // that is the defect this feature exists to remove.
        var draftStore = new InMemoryBlueprintStore();
        var publishedStore = new InMemoryPublishedBlueprintStore();
        var blueprint = CreateBlueprint();
        await draftStore.AddAsync(blueprint);
        await new PublishService(draftStore, publishedStore).PublishAsync(blueprint.Id, "register-1");

        var resolved = await publishedStore.GetByExecDefHashAsync(blueprint.Id, new string('f', 64));

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task APresentationalOnlyRepublish_ProducesTheSamePin()
    {
        // Story 4 / FR-014: relabelling must not strand a running instance on the older definition.
        var draftStore = new InMemoryBlueprintStore();
        var publishedStore = new InMemoryPublishedBlueprintStore();
        var blueprint = CreateBlueprint();
        await draftStore.AddAsync(blueprint);
        var service = new PublishService(draftStore, publishedStore);

        var v1 = await service.PublishAsync(blueprint.Id, "register-1");

        var draft = await draftStore.GetAsync(blueprint.Id);
        draft!.Title = "A completely different human-facing title";
        draft.Description = "And a rewritten description, for the reader's benefit.";
        draft.Actions[0].Title = "Begin";
        var v2 = await service.PublishAsync(blueprint.Id, "register-1");

        v2.PublishedBlueprint!.ExecDefHash.Should().Be(v1.PublishedBlueprint!.ExecDefHash,
            because: "titles and descriptions are presentational — nothing about how the blueprint " +
                     "executes has changed, so no instance should be moved");
        v2.PublishedBlueprint.Version.Should().Be(2,
            because: "the ordinal still increments, for human bookkeeping only");
    }

    private static BlueprintModel CreateBlueprint() => new()
    {
        Id = "bp-pin-1",
        Title = "Pinning Test Blueprint",
        Description = "A valid blueprint used to exercise publication snapshot isolation.",
        Participants =
        [
            new ParticipantModel { Id = "p1", Name = "Alice" },
            new ParticipantModel { Id = "p2", Name = "Bob" }
        ],
        Actions =
        [
            new ActionModel { Id = 1, Title = "Start", Sender = "p1", IsStartingAction = true }
        ]
    };
}

/// <summary>
/// Feature 194 — the pin must be computed over the bytes that are actually stored, which means
/// AFTER <c>$ref</c> flattening.
/// </summary>
/// <remarks>
/// Flattening rewrites every action's data schemas in place. A hash taken before it addresses a
/// definition that is never stored, never cached and never pushed to the register — so a pinned
/// instance could never resolve it, and the failure would present as "this instance is stuck" rather
/// than as an ordering mistake. The resolver here is a stand-in that performs one visible rewrite,
/// so the assertion is about <b>ordering</b> and does not depend on the real core-schema catalogue.
/// </remarks>
public class PublishHashOrderingTests
{
    [Fact]
    public async Task TheHash_IsComputedOverTheFlattenedSchemas_NotTheOriginals()
    {
        var draftStore = new InMemoryBlueprintStore();
        var publishedStore = new InMemoryPublishedBlueprintStore();
        var blueprint = CreateBlueprintWithRef();
        await draftStore.AddAsync(blueprint);

        var service = new PublishService(
            draftStore,
            publishedStore,
            registerClient: null,
            redis: null,
            schemaRefResolver: new MarkerRewritingRefResolver());

        var result = await service.PublishAsync(blueprint.Id, "register-1");
        result.IsSuccess.Should().BeTrue();

        var stored = result.PublishedBlueprint!;
        var storedSchema = stored.Blueprint.Actions[0].DataSchemas!.Single().RootElement.GetRawText();

        storedSchema.Should().Contain("\"flattened\"",
            because: "the stored snapshot must be the flattened form the validator will see");
        storedSchema.Should().NotContain("$ref");

        // The decisive assertion: the pin matches a hash recomputed over the FLATTENED definition.
        // Were the hash taken before flattening it would match the pre-flatten form instead, and a
        // pinned instance would resolve nothing.
        new ExecutableDefinitionHasher().ComputeHash(stored.Blueprint).Should().Be(stored.ExecDefHash);
    }

    private static BlueprintModel CreateBlueprintWithRef() => new()
    {
        Id = "bp-ref-1",
        Title = "Ref Flattening Blueprint",
        Description = "Exercises the publish-time ordering of flattening and hashing.",
        Participants =
        [
            new ParticipantModel { Id = "p1", Name = "Alice" },
            new ParticipantModel { Id = "p2", Name = "Bob" }
        ],
        Actions =
        [
            new ActionModel
            {
                Id = 1,
                Title = "Start",
                Sender = "p1",
                IsStartingAction = true,
                DataSchemas =
                [
                    JsonDocument.Parse("""
                    {"type":"object","properties":{"name":{"$ref":"https://schemas.sorcha.dev/core/PersonName/v1"}}}
                    """)
                ]
            }
        ]
    };

    /// <summary>
    /// A stand-in resolver that makes flattening visible: it replaces the <c>$ref</c> with a marker
    /// property. Enough to prove ordering without coupling the test to the real catalogue.
    /// </summary>
    private sealed class MarkerRewritingRefResolver : Sorcha.Blueprint.Service.Services.ISchemaRefResolver
    {
        public JsonNode Flatten(JsonNode schema)
        {
            var text = schema.ToJsonString()
                .Replace("""{"$ref":"https://schemas.sorcha.dev/core/PersonName/v1"}""",
                         """{"type":"string","flattened":true}""");
            return JsonNode.Parse(text)!;
        }
    }
}
