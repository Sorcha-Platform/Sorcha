// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Service.Storage;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests;

/// <summary>
/// Publish/validate coverage for the AIAS device-registration blueprint
/// (<c>demos/AIAS/blueprints/aias-device-registration.template.json</c>, #1195 Phase 2 —
/// "one assurance, two bindings"). Loads the real shipped template, deserializes its
/// <c>template</c> body into the <see cref="BlueprintModel"/>, and drives it through the same
/// <see cref="PublishService.ValidateAsync"/> publish-time gate that guards production publishes.
///
/// The blueprint's starting action gates on PRESENTING the web-issued
/// <c>AssuredIdentityCredential</c> (entitlement proof) and captures the phone's device public
/// JWK; its issuance action mints a device-<c>cnf</c> copy bound to that key. These tests pin the
/// shape the runtime relies on and prove the template publishes clean (no <c>VAL_BP_*</c> errors,
/// open citizen participant accepted).
/// </summary>
public class AiasDeviceRegistrationBlueprintTests
{
    private const string ExpectedVct = "https://sorcha.dev/vc/assured-identity/v1";

    private static readonly JsonSerializerOptions DeserializeOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static BlueprintModel LoadDeviceRegistrationBlueprint()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var file = Path.Combine(
            repoRoot, "demos", "AIAS", "blueprints", "aias-device-registration.template.json");
        File.Exists(file).Should().BeTrue($"the device-registration template must ship at {file}");

        var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();

        // Templates are wrapped: the workflow body lives under "template" (mirrors the sibling
        // aias-assured-identity.template.json and the walkthrough loader's wrapped/flat handling).
        var templateNode = root["template"];
        templateNode.Should().NotBeNull("the AIAS template must carry its blueprint under a 'template' property");

        var blueprint = templateNode.Deserialize<BlueprintModel>(DeserializeOptions);
        blueprint.Should().NotBeNull("the template body must deserialize into the Blueprint model");
        return blueprint!;
    }

    private static PublishService BuildValidatingService(BlueprintModel blueprint)
    {
        var blueprintStore = new Mock<IBlueprintStore>();
        var publishedStore = new Mock<IPublishedBlueprintStore>();
        blueprintStore.Setup(s => s.GetAsync(blueprint.Id)).ReturnsAsync(blueprint);
        return new PublishService(blueprintStore.Object, publishedStore.Object, FakePublishingRegister.Client());
    }

    [Fact]
    public async Task Validate_DeviceRegistrationBlueprint_PublishesWithoutValidationErrors()
    {
        var blueprint = LoadDeviceRegistrationBlueprint();
        var service = BuildValidatingService(blueprint);

        var result = await service.ValidateAsync(blueprint.Id);

        result.IsValid.Should().BeTrue(
            "the shipped device-registration blueprint must pass the publish-time gate: "
            + string.Join(" | ", result.ValidationResults.Select(i => i.Message)));
        result.ValidationResults.Should().NotContain(
            i => i.Message.Contains("VAL_BP_", StringComparison.Ordinal),
            "no publish-time guardrail (VAL_BP_*) may fire");
    }

    [Fact]
    public void DeviceRegistrationBlueprint_CitizenIsOpenParticipant_WalletAddressOmitted()
    {
        var blueprint = LoadDeviceRegistrationBlueprint();

        var citizen = blueprint.Participants.Should().ContainSingle(p => p.Id == "citizen").Subject;
        citizen.WalletAddress.Should().BeNullOrWhiteSpace(
            "citizen is the sender of the open starting action and must late-bind (VAL_BP_010)");
    }

    [Fact]
    public void DeviceRegistrationBlueprint_StartingAction_GatesOnAssuredIdentityPresentation()
    {
        var blueprint = LoadDeviceRegistrationBlueprint();

        var start = blueprint.Actions.Should().ContainSingle(a => a.IsStartingAction).Subject;
        start.Sender.Should().Be("citizen");

        var requirement = start.CredentialRequirements.Should()
            .ContainSingle("the starting action gates on a single credential presentation").Subject;

        // Canonical VCT URI (case-sensitive) — matches VctUris.AssuredIdentityV1 and the corpus
        // convention (walkthroughs/.../driving-licence.json). A bare type name would fail the
        // BlueprintVctConformanceTests sweep that walks demos/.
        requirement.Type.Should().Be(ExpectedVct);
        requirement.PresentationSource.Should()
            .Be(Sorcha.Blueprint.Models.Credentials.PresentationSource.SorchaWallet,
                "the citizen presents from their on-device Sorcha Wallet PWA (F127), not HAIP");
        requirement.RequiredClaims.Should().NotBeNull();
        // Task 6 fix round — optional-claim gate semantics. The gate requires ONLY the
        // mandatory identity core every root provably carries. middleName/fullName/portrait
        // are OPTIONAL at apply time (claim mappings skip missing sources), so requiring them
        // would refuse the bind to every citizen without a middle name. Full disclosure of the
        // copy comes from the consumer's PASS-THROUGH of all verified disclosed claims
        // (design §4.1), not from this list — the requiredClaims are the entitlement gate.
        requirement.RequiredClaims!.Select(c => c.ClaimName).Should().BeEquivalentTo(
            "givenName", "familyName", "dateOfBirth");
    }

    [Fact]
    public void DeviceRegistrationBlueprint_StartingAction_CapturesDevicePublicKey()
    {
        var blueprint = LoadDeviceRegistrationBlueprint();

        var start = blueprint.Actions.Single(a => a.IsStartingAction);
        var schema = start.DataSchemas.Should().ContainSingle().Subject;

        using var doc = schema; // JsonDocument
        var deviceKey = doc.RootElement.GetProperty("properties").GetProperty("deviceKey");
        deviceKey.GetProperty("format").GetString().Should().Be("sorcha-device-key");
        deviceKey.GetProperty("x-device-key").GetProperty("required").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void DeviceRegistrationBlueprint_IssuanceAction_MintsDeviceCnfCopy()
    {
        var blueprint = LoadDeviceRegistrationBlueprint();

        var issuance = blueprint.Actions.Should()
            .ContainSingle(a => a.CredentialIssuanceConfig != null).Subject;
        issuance.Sender.Should().Be("aias-issuer");
        issuance.RequiredPriorActions.Should().Contain(1);
        issuance.Routes.Should().ContainSingle().Which.NextActionIds.Should()
            .BeEmpty("the issuance action is terminal");

        var config = issuance.CredentialIssuanceConfig!;
        config.CredentialType.Should().Be("AssuredIdentityCredential");
        config.Vct.Should().Be(ExpectedVct);
        config.RecipientParticipantId.Should().Be("citizen");
        config.HolderKeySourceField.Should().Be("/deviceKey/holderJwk",
            "the device copy is bound (cnf) to the captured device key");
        config.ClaimMappings.Select(m => m.SourceField).Should()
            .OnlyContain(s => s!.StartsWith("/presentedCredential/", StringComparison.Ordinal),
                "the full assured claim set is carried from the verified presentation");
    }

    [Fact]
    public void DeviceRegistrationBlueprint_DoesNotBakeRegisterId()
    {
        // Register selection is left to the AIAS publish/deploy tooling (same register as the
        // apply blueprint). The apply template bakes no registerId; neither may this one.
        var blueprint = LoadDeviceRegistrationBlueprint();
        foreach (var action in blueprint.Actions)
        {
            action.CredentialIssuanceConfig?.RegisterId.Should().BeNullOrEmpty(
                "the device-registration blueprint must not target a register — the deploy tooling picks it");
        }
    }

    private static string FindRepoRoot(string startDir)
    {
        var dir = startDir;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Sorcha.sln")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repo root (dir containing Sorcha.sln) walking up from '{startDir}'.");
    }
}
