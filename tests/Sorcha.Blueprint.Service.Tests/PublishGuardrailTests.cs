// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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
        _sut = new PublishService(_blueprintStore.Object, _publishedStore.Object);
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
