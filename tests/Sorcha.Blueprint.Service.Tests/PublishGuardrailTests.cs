// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Service.Storage;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests;

/// <summary>
/// Tests for the VAL_BP_010 publish-time guardrail that rejects blueprints where a
/// starting-action sender participant has been pre-bound to a wallet.
///
/// Contract: specs/103-verified-citizen-v2/contracts/validator-publish-errors.md
/// § Tests required.
/// </summary>
public class PublishGuardrailTests
{
    private readonly Mock<IBlueprintStore> _blueprintStore;
    private readonly Mock<IPublishedBlueprintStore> _publishedStore;
    private readonly PublishService _sut;

    public PublishGuardrailTests()
    {
        _blueprintStore = new Mock<IBlueprintStore>();
        _publishedStore = new Mock<IPublishedBlueprintStore>();
        _sut = new PublishService(_blueprintStore.Object, _publishedStore.Object, FakePublishingRegister.Client());
    }

    // ----- Feature 103 T041: publish-time SchemaRefResolver wiring -----

    [Fact]
    public async Task Publish_BlueprintWithCoreRef_FlattensSchemasBeforePersist()
    {
        var coreRepo = new Sorcha.Blueprint.Service.Services.InMemoryCoreSchemaRepository();
        var primitiveUri = "https://schemas.sorcha.dev/core/PostalAddress/v1";
        coreRepo.Upsert(primitiveUri, System.Text.Json.Nodes.JsonNode.Parse("""
            {
              "$id": "https://schemas.sorcha.dev/core/PostalAddress/v1",
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "title": "Postal Address",
              "properties": {
                "line1":    { "type": "string", "x-persona": "address.line1" },
                "postcode": { "type": "string", "x-persona": "address.postcode", "x-address-lookup": true }
              },
              "required": ["line1", "postcode"]
            }
            """)!);
        var resolver = new Sorcha.Blueprint.Service.Services.SchemaRefResolver(
            coreRepo,
            Mock.Of<ILogger<Sorcha.Blueprint.Service.Services.SchemaRefResolver>>());

        var blueprintStore = new Mock<IBlueprintStore>();
        var publishedStore = new Mock<IPublishedBlueprintStore>();

        BlueprintModel? capturedPublished = null;
        publishedStore
            .Setup(s => s.AddAsync(It.IsAny<PublishedBlueprint>()))
            .Callback<PublishedBlueprint>(p => capturedPublished = p.Blueprint)
            .ReturnsAsync((PublishedBlueprint p) => p);

        var bp = BuildBlueprintWithRefSchema(primitiveUri);
        blueprintStore.Setup(s => s.GetAsync(bp.Id)).ReturnsAsync(bp);

        var sut = new PublishService(
            blueprintStore.Object,
            publishedStore.Object,
            registerClient: FakePublishingRegister.Client(),
            redis: null,
            schemaRefResolver: resolver,
            logger: null);

        var result = await sut.PublishAsync(bp.Id, registerId: "reg-1");

        result.IsSuccess.Should().BeTrue($"publish should succeed: {string.Join(", ", result.Errors ?? Array.Empty<string>())}");
        capturedPublished.Should().NotBeNull();

        var actionSchema = capturedPublished!.Actions.First(a => a.Id == 1).DataSchemas!.First();
        var schemaJson = actionSchema.RootElement.GetRawText();
        schemaJson.Should().NotContain("$ref", "the publish path should have flattened the $ref");
        schemaJson.Should().Contain("\"line1\"", "the inlined primitive's properties should appear");
        schemaJson.Should().Contain("\"x-address-lookup\"", "per-property metadata flows through the flattener");
    }

    [Fact]
    public async Task Publish_BlueprintWithNoRefs_PassesThroughUnchanged()
    {
        // Pre-existing blueprints that predate the primitive library must
        // publish cleanly when the resolver is registered. The $ref scan
        // short-circuit means their DataSchemas are not even touched — the
        // published snapshot carries the same JsonDocument instances as the
        // draft (no round-trip, no dispose churn).
        var coreRepo = new Sorcha.Blueprint.Service.Services.InMemoryCoreSchemaRepository();
        var resolver = new Sorcha.Blueprint.Service.Services.SchemaRefResolver(
            coreRepo,
            Mock.Of<ILogger<Sorcha.Blueprint.Service.Services.SchemaRefResolver>>());

        var blueprintStore = new Mock<IBlueprintStore>();
        var publishedStore = new Mock<IPublishedBlueprintStore>();
        BlueprintModel? capturedPublished = null;
        publishedStore
            .Setup(s => s.AddAsync(It.IsAny<PublishedBlueprint>()))
            .Callback<PublishedBlueprint>(p => capturedPublished = p.Blueprint)
            .ReturnsAsync((PublishedBlueprint p) => p);

        var bp = BuildBlueprint(
            citizenWalletAddress: null,
            assessorWalletAddress: "ws1qassessor");
        blueprintStore.Setup(s => s.GetAsync(bp.Id)).ReturnsAsync(bp);

        var sut = new PublishService(
            blueprintStore.Object,
            publishedStore.Object,
            registerClient: FakePublishingRegister.Client(),
            redis: null,
            schemaRefResolver: resolver,
            logger: null);

        var result = await sut.PublishAsync(bp.Id, registerId: "reg-1");

        result.IsSuccess.Should().BeTrue();
        capturedPublished.Should().NotBeNull();
        // The built blueprint has no DataSchemas on its actions by default;
        // this path simply exercises the resolver-present + no-refs flow.
    }

    [Fact]
    public async Task Publish_BlueprintWithUnknownCoreRef_ReturnsFailureWithOffendingUri()
    {
        var coreRepo = new Sorcha.Blueprint.Service.Services.InMemoryCoreSchemaRepository();
        var resolver = new Sorcha.Blueprint.Service.Services.SchemaRefResolver(
            coreRepo,
            Mock.Of<ILogger<Sorcha.Blueprint.Service.Services.SchemaRefResolver>>());

        var blueprintStore = new Mock<IBlueprintStore>();
        var publishedStore = new Mock<IPublishedBlueprintStore>();
        var bp = BuildBlueprintWithRefSchema("https://schemas.sorcha.dev/core/Unknown/v1");
        blueprintStore.Setup(s => s.GetAsync(bp.Id)).ReturnsAsync(bp);

        var sut = new PublishService(
            blueprintStore.Object,
            publishedStore.Object,
            registerClient: FakePublishingRegister.Client(),
            redis: null,
            schemaRefResolver: resolver,
            logger: null);

        var result = await sut.PublishAsync(bp.Id, registerId: "reg-1");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().NotBeNull();
        result.Errors!.Should().ContainSingle(e =>
            e.Contains("Schema $ref resolution failed", StringComparison.Ordinal) &&
            e.Contains("Unknown/v1", StringComparison.Ordinal));
    }

    private static BlueprintModel BuildBlueprintWithRefSchema(string primitiveUri)
    {
        var schemaJson = $$"""
            {
              "type": "object",
              "properties": {
                "address": { "$ref": "{{primitiveUri}}" }
              },
              "required": ["address"]
            }
            """;

        return new BlueprintModel
        {
            Id = "bp-flatten-test-" + Guid.NewGuid().ToString("N")[..8],
            Title = "Flatten Test",
            Description = "Blueprint for testing publish-time schema $ref flattening",
            Version = 1,
            Participants = new List<Participant>
            {
                new() { Id = "citizen", Name = "Citizen" },
                new() { Id = "assessor", Name = "Assessor", WalletAddress = "ws1qassessor" }
            },
            Actions = new List<Sorcha.Blueprint.Models.Action>
            {
                new()
                {
                    Id = 1,
                    Title = "Submit",
                    Sender = "citizen",
                    IsStartingAction = true,
                    Disclosures = new[]
                    {
                        new Disclosure { ParticipantAddress = "citizen",  DataPointers = new List<string> { "/*" } },
                        new Disclosure { ParticipantAddress = "assessor", DataPointers = new List<string> { "/*" } }
                    },
                    DataSchemas = new[] { System.Text.Json.JsonDocument.Parse(schemaJson) },
                    Routes = new[] { new Route { Id = "to-2", NextActionIds = new[] { 2 }, IsDefault = true } }
                },
                new()
                {
                    Id = 2,
                    Title = "Review",
                    Sender = "assessor",
                    Disclosures = new[]
                    {
                        new Disclosure { ParticipantAddress = "assessor", DataPointers = new List<string> { "/*" } }
                    },
                    Routes = new[] { new Route { Id = "complete", NextActionIds = Array.Empty<int>(), IsDefault = true } }
                }
            }
        };
    }

    [Fact]
    public async Task Validate_WhenStartingActionParticipantHasNullWallet_Passes()
    {
        // Arrange — correct shape: citizen is open (walletAddress null)
        var bp = BuildBlueprint(
            citizenWalletAddress: null,
            assessorWalletAddress: "ws1qassessor");
        _blueprintStore.Setup(s => s.GetAsync(bp.Id)).ReturnsAsync(bp);

        // Act
        var result = await _sut.ValidateAsync(bp.Id);

        // Assert — no VAL_BP_010 error
        result.IsValid.Should().BeTrue();
        result.ValidationResults.Should().NotContain(i => i.Message.Contains("VAL_BP_010"));
    }

    [Fact]
    public async Task Validate_WhenStartingActionParticipantHasEmptyWallet_Passes()
    {
        // Arrange — whitespace-only wallet counts as unset
        var bp = BuildBlueprint(
            citizenWalletAddress: "   ",
            assessorWalletAddress: "ws1qassessor");
        _blueprintStore.Setup(s => s.GetAsync(bp.Id)).ReturnsAsync(bp);

        // Act
        var result = await _sut.ValidateAsync(bp.Id);

        // Assert
        result.ValidationResults.Should().NotContain(i => i.Message.Contains("VAL_BP_010"));
    }

    [Fact]
    public async Task Validate_WhenStartingActionParticipantHasPopulatedWallet_Fails()
    {
        // Arrange — citizen pre-bound: foot-gun
        var bp = BuildBlueprint(
            citizenWalletAddress: "ws1qcitizen",
            assessorWalletAddress: "ws1qassessor");
        _blueprintStore.Setup(s => s.GetAsync(bp.Id)).ReturnsAsync(bp);

        // Act
        var result = await _sut.ValidateAsync(bp.Id);

        // Assert — VAL_BP_010 fires, names the offending participant, explains the fix
        result.IsValid.Should().BeFalse();
        result.ValidationResults.Should().ContainSingle(i =>
            i.Severity == "error" &&
            i.Message.Contains("VAL_BP_010") &&
            i.Message.Contains("citizen") &&
            i.Message.Contains("ws1qcitizen"));
    }

    [Fact]
    public async Task Validate_WhenNonStartingActionParticipantHasWallet_Passes()
    {
        // Arrange — assessor is NOT a starting-action sender and SHOULD be pre-bound
        var bp = BuildBlueprint(
            citizenWalletAddress: null,
            assessorWalletAddress: "ws1qassessor");
        _blueprintStore.Setup(s => s.GetAsync(bp.Id)).ReturnsAsync(bp);

        // Act
        var result = await _sut.ValidateAsync(bp.Id);

        // Assert — the rule applies only to starting-action senders
        result.IsValid.Should().BeTrue();
        result.ValidationResults.Should().NotContain(i => i.Message.Contains("VAL_BP_010"));
    }

    [Fact]
    public async Task Validate_WhenStartingActionHasNoSender_Passes()
    {
        // Arrange — starting action with no sender attribute (degenerate but legal)
        var bp = BuildBlueprint(
            citizenWalletAddress: null,
            assessorWalletAddress: "ws1qassessor");

        // Null out the sender on action 1 (our built blueprint's starting action)
        var actions = bp.Actions.ToList();
        actions[0].Sender = string.Empty;
        bp.Actions = actions;
        _blueprintStore.Setup(s => s.GetAsync(bp.Id)).ReturnsAsync(bp);

        // Act
        var result = await _sut.ValidateAsync(bp.Id);

        // Assert — no VAL_BP_010 (there's nothing to check)
        result.ValidationResults.Should().NotContain(i => i.Message.Contains("VAL_BP_010"));
    }

    [Fact]
    public async Task Validate_MultipleStartingActionsWithOnePreBound_ReportsOnlyOffender()
    {
        // Arrange — two starting actions, only one pre-bound
        var bp = BuildBlueprint(
            citizenWalletAddress: "ws1qcitizen", // offender
            assessorWalletAddress: "ws1qassessor");

        // Add a second starting action whose sender is a legitimately open participant
        var actions = bp.Actions.ToList();
        actions.Add(new Sorcha.Blueprint.Models.Action
        {
            Id = 3,
            Title = "Alternative Start",
            Sender = "observer",
            IsStartingAction = true,
            Disclosures = new[] { new Disclosure { ParticipantAddress = "observer", DataPointers = new List<string> { "/*" } } }
        });
        bp.Actions = actions;

        var participants = bp.Participants.ToList();
        participants.Add(new Participant { Id = "observer", Name = "Observer" /* walletAddress = null */ });
        bp.Participants = participants;

        _blueprintStore.Setup(s => s.GetAsync(bp.Id)).ReturnsAsync(bp);

        // Act
        var result = await _sut.ValidateAsync(bp.Id);

        // Assert — citizen reported, observer not reported
        result.ValidationResults.Should().ContainSingle(i => i.Message.Contains("VAL_BP_010") && i.Message.Contains("citizen"));
        result.ValidationResults.Should().NotContain(i => i.Message.Contains("VAL_BP_010") && i.Message.Contains("observer"));
    }

    [Fact]
    public async Task Validate_WhenBlueprintIdUnknown_ReturnsNotFoundWithoutThrowing()
    {
        // Arrange — store returns null
        _blueprintStore.Setup(s => s.GetAsync("missing")).ReturnsAsync((BlueprintModel?)null);

        // Act
        var result = await _sut.ValidateAsync("missing");

        // Assert — handled gracefully, no VAL_BP_010 noise
        result.IsValid.Should().BeFalse();
        result.ValidationResults.Should().ContainSingle(i => i.Message.Contains("not found"));
    }

    // ----- test helpers -----

    private static BlueprintModel BuildBlueprint(string? citizenWalletAddress, string? assessorWalletAddress)
    {
        return new BlueprintModel
        {
            Id = "bp-test-" + Guid.NewGuid().ToString("N")[..8],
            Title = "Test Blueprint",
            Description = "Blueprint for testing the publish-time open-participant guardrail",
            Version = 1,
            Participants = new List<Participant>
            {
                new Participant { Id = "citizen", Name = "Citizen", WalletAddress = citizenWalletAddress },
                new Participant { Id = "assessor", Name = "Assessor", WalletAddress = assessorWalletAddress }
            },
            Actions = new List<Sorcha.Blueprint.Models.Action>
            {
                new Sorcha.Blueprint.Models.Action
                {
                    Id = 1,
                    Title = "Submit",
                    Sender = "citizen",
                    IsStartingAction = true,
                    Disclosures = new[]
                    {
                        new Disclosure { ParticipantAddress = "citizen",  DataPointers = new List<string> { "/*" } },
                        new Disclosure { ParticipantAddress = "assessor", DataPointers = new List<string> { "/*" } }
                    },
                    Routes = new[] { new Route { Id = "to-review", NextActionIds = new[] { 2 }, IsDefault = true } }
                },
                new Sorcha.Blueprint.Models.Action
                {
                    Id = 2,
                    Title = "Review",
                    Sender = "assessor",
                    Disclosures = new[]
                    {
                        new Disclosure { ParticipantAddress = "assessor", DataPointers = new List<string> { "/*" } }
                    },
                    Routes = new[] { new Route { Id = "complete", NextActionIds = Array.Empty<int>(), IsDefault = true } }
                }
            }
        };
    }
}
