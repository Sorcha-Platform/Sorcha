// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Engine.Models;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Register;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using DisclosureModel = Sorcha.Blueprint.Models.Disclosure;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 176 — unit coverage for the shared <see cref="ActionDisclosureResolver"/>. Proves the
/// submit-side participant→wallet mapping and the read-side reconstruct-then-clamp: only fields
/// disclosed to the caller's participant are ever returned (FR-006 / FR-010), the caller wallet is
/// resolved (including multi-wallet), a non-recipient gets an empty / <c>recipientResolved=false</c>
/// view (fail-closed driver), and the view is identical regardless of encrypted vs dev-mode storage.
/// </summary>
public class ActionDisclosureResolverTests
{
    private const string RegisterId = "reg-1";
    private const string InstanceId = "inst-1";
    private const string AnalystWallet = "wsAnalyst";
    private const string CitizenWallet = "wsCitizen";

    private readonly Mock<IExecutionEngine> _engine = new();
    private readonly Mock<IRegisterServiceClient> _register = new();
    private readonly Mock<IStateReconstructionService> _reconstruction = new();
    private readonly Mock<IInstanceStore> _instanceStore = new();
    private readonly Mock<IBlueprintStore> _blueprintStore = new();

    private ActionDisclosureResolver CreateResolver() =>
        new(_engine.Object, _register.Object, NullLogger<ActionDisclosureResolver>.Instance,
            _reconstruction.Object, _instanceStore.Object, _blueprintStore.Object);

    private static BlueprintModel Blueprint() => new()
    {
        Id = "bp-1",
        Title = "AIAS Assured Identity",
        Participants =
        [
            new ParticipantModel { Id = "citizen", Name = "Applicant" },
            new ParticipantModel { Id = "verification-analyst", Name = "Assure-ID Agent" },
        ],
        Actions =
        [
            new ActionModel
            {
                Id = 1,
                Title = "Submit Assured Identity Application",
                Sender = "citizen",
                Disclosures =
                [
                    new DisclosureModel { ParticipantAddress = "citizen", DataPointers = ["/*"] },
                    new DisclosureModel { ParticipantAddress = "verification-analyst", DataPointers = ["/*"] },
                ],
            },
            new ActionModel { Id = 2, Title = "Verify Assured Identity Application", Sender = "verification-analyst" },
        ],
    };

    private static Instance Instance() => new()
    {
        Id = InstanceId,
        BlueprintId = "bp-1",
        BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: execution resolves and chains by the pin
        BlueprintVersion = 1,
        RegisterId = RegisterId,
        TenantId = "t-1",
        CurrentActionIds = [2],
        ParticipantWallets = new Dictionary<string, string>
        {
            ["citizen"] = CitizenWallet,
            ["verification-analyst"] = AnalystWallet,
        },
    };

    private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private void SetupReconstruction(JsonElement action1Data) =>
        _reconstruction
            .Setup(r => r.ReconstructAsync(
                It.IsAny<BlueprintModel>(), InstanceId, 2, RegisterId,
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccumulatedState
            {
                ActionData = new Dictionary<string, JsonElement> { ["1"] = action1Data },
                ActionCount = 1,
            });

    // ---- Submit-side primitive -------------------------------------------------------------------

    [Fact]
    public async Task ApplyDisclosuresAsync_ResolvesBoundParticipant_ToWalletKeyedDisclosedData()
    {
        var action = Blueprint().Actions!.First(a => a.Id == 1);
        var disclosed = new Dictionary<string, object> { ["postcode"] = "SW1A 1AA" };
        _engine.Setup(e => e.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), action))
            .Returns([DisclosureResult.Create("verification-analyst", disclosed)]);

        var result = await CreateResolver().ApplyDisclosuresAsync(
            action, new Dictionary<string, object>(), Blueprint(),
            new Dictionary<string, string> { ["verification-analyst"] = AnalystWallet }, RegisterId);

        result.Should().ContainKey(AnalystWallet);
        result[AnalystWallet].Should().BeEquivalentTo(disclosed);
    }

    // ---- Read-side: reconstruct-then-clamp -------------------------------------------------------

    [Fact]
    public async Task ResolveDisclosedDataAsync_RecipientCaller_ReturnsOnlyClampedDisclosedFields()
    {
        // Reconstruction yields name + address for the caller; the engine clamps action-1's disclosure
        // to the analyst — here deliberately a SUBSET (address only) — proving no undisclosed field
        // (the applicant's name) leaks through even though it was in the reconstructed view.
        _instanceStore.Setup(s => s.GetAsync(InstanceId, It.IsAny<CancellationToken>())).ReturnsAsync(Instance());
        _blueprintStore.Setup(s => s.GetAsync("bp-1")).ReturnsAsync(Blueprint());
        SetupReconstruction(Element("""{ "name": { "fullName": "Ada Lovelace" }, "address": { "postcode": "SW1A 1AA" } }"""));

        var addressOnly = new Dictionary<string, object> { ["address"] = Element("""{ "postcode": "SW1A 1AA" }""") };
        _engine.Setup(e => e.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), It.IsAny<ActionModel>()))
            .Returns([DisclosureResult.Create("verification-analyst", addressOnly)]);

        var result = await CreateResolver().ResolveDisclosedDataAsync(
            InstanceId, 2, [AnalystWallet], delegationToken: "tok");

        result.RecipientResolved.Should().BeTrue();
        result.DisclosedFields.Should().ContainKey("address");
        result.DisclosedFields.Should().NotContainKey("name", "an undisclosed field must never be returned");
        result.Disclosures.Should().ContainSingle();
        result.Disclosures[0].ActionId.Should().Be(1);
        result.Disclosures[0].ActionTitle.Should().Be("Submit Assured Identity Application");
    }

    [Fact]
    public async Task ResolveDisclosedDataAsync_MultiWalletCaller_ResolvesViaMatchingWallet()
    {
        _instanceStore.Setup(s => s.GetAsync(InstanceId, It.IsAny<CancellationToken>())).ReturnsAsync(Instance());
        _blueprintStore.Setup(s => s.GetAsync("bp-1")).ReturnsAsync(Blueprint());
        SetupReconstruction(Element("""{ "email": { "email": "ada@example.test" } }"""));

        var disclosed = new Dictionary<string, object> { ["email"] = Element("""{ "email": "ada@example.test" }""") };
        _engine.Setup(e => e.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), It.IsAny<ActionModel>()))
            .Returns([DisclosureResult.Create("verification-analyst", disclosed)]);

        // The agent owns two wallets; only the second is the analyst recipient.
        var result = await CreateResolver().ResolveDisclosedDataAsync(
            InstanceId, 2, ["wsOther", AnalystWallet], delegationToken: "tok");

        result.RecipientResolved.Should().BeTrue();
        result.DisclosedFields.Should().ContainKey("email");
    }

    [Fact]
    public async Task ResolveDisclosedDataAsync_NonRecipientCaller_RecipientNotResolved()
    {
        _instanceStore.Setup(s => s.GetAsync(InstanceId, It.IsAny<CancellationToken>())).ReturnsAsync(Instance());
        _blueprintStore.Setup(s => s.GetAsync("bp-1")).ReturnsAsync(Blueprint());
        SetupReconstruction(Element("""{ "decision": "approved" }"""));

        // Action-1 discloses only to the citizen; the caller wallet is a stranger.
        var disclosed = new Dictionary<string, object> { ["decision"] = "approved" };
        _engine.Setup(e => e.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), It.IsAny<ActionModel>()))
            .Returns([DisclosureResult.Create("citizen", disclosed)]);

        var result = await CreateResolver().ResolveDisclosedDataAsync(
            InstanceId, 2, ["wsStranger"], delegationToken: "tok");

        result.RecipientResolved.Should().BeFalse();
        result.DisclosedFields.Should().BeEmpty();
        result.Disclosures.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveDisclosedDataAsync_InstanceNotFound_ReturnsEmptyNotResolved()
    {
        _instanceStore.Setup(s => s.GetAsync(InstanceId, It.IsAny<CancellationToken>())).ReturnsAsync((Instance?)null);

        var result = await CreateResolver().ResolveDisclosedDataAsync(
            InstanceId, 2, [AnalystWallet], delegationToken: "tok");

        result.RecipientResolved.Should().BeFalse();
        result.DisclosedFields.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveDisclosedDataAsync_ReconstructionFaults_FailsClosedEmpty()
    {
        _instanceStore.Setup(s => s.GetAsync(InstanceId, It.IsAny<CancellationToken>())).ReturnsAsync(Instance());
        _blueprintStore.Setup(s => s.GetAsync("bp-1")).ReturnsAsync(Blueprint());
        _reconstruction
            .Setup(r => r.ReconstructAsync(
                It.IsAny<BlueprintModel>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("register unreachable"));

        var result = await CreateResolver().ResolveDisclosedDataAsync(
            InstanceId, 2, [AnalystWallet], delegationToken: "tok");

        result.RecipientResolved.Should().BeFalse("a reconstruction fault must fail closed, not decide on blanks");
        result.DisclosedFields.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveDisclosedDataAsync_IdenticalReconstruction_YieldsIdenticalView_RegardlessOfPosture()
    {
        // Encrypted vs dev-mode is normalised by StateReconstructionService: given identical
        // reconstructed ActionData, the resolver's disclosed view is byte-for-byte the same. The
        // resolver never branches on storage posture.
        _instanceStore.Setup(s => s.GetAsync(InstanceId, It.IsAny<CancellationToken>())).ReturnsAsync(Instance());
        _blueprintStore.Setup(s => s.GetAsync("bp-1")).ReturnsAsync(Blueprint());
        SetupReconstruction(Element("""{ "address": { "postcode": "SW1A 1AA" } }"""));
        var disclosed = new Dictionary<string, object> { ["address"] = Element("""{ "postcode": "SW1A 1AA" }""") };
        _engine.Setup(e => e.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), It.IsAny<ActionModel>()))
            .Returns([DisclosureResult.Create("verification-analyst", disclosed)]);

        var resolver = CreateResolver();
        var first = await resolver.ResolveDisclosedDataAsync(
            InstanceId, 2, [AnalystWallet], "tok");
        var second = await resolver.ResolveDisclosedDataAsync(
            InstanceId, 2, [AnalystWallet], "tok");

        JsonSerializer.Serialize(first.DisclosedFields)
            .Should().Be(JsonSerializer.Serialize(second.DisclosedFields));
        first.RecipientResolved.Should().BeTrue();
    }
}
