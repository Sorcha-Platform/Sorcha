// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// P0 fix (<c>fix/pwa-p0-claim-and-camera</c>) — <c>GET /api/instances/{instanceId}/actions/{actionId}</c>
/// must be readable by a consumer-tier citizen token (the failure mode of the bug: the PWA previously
/// hit the authoring-only <c>GET /api/blueprints/{id}</c> and got a 403), must gate on the caller's
/// wallet being a recorded participant on the instance, and must return only the form-relevant subset
/// of the action — not the whole blueprint and not routing/other-participant internals.
/// </summary>
public sealed class InstanceActionEndpointsTests
{
    private const string CitizenWallet = "ws1qcitizen000000000000000000000000000000";
    private const string OtherWallet = "ws1qother00000000000000000000000000000000";

    private static Instance MakeInstance(string participantWallet, string blueprintId = "bp-1", string instanceId = "inst-1") =>
        new()
        {
            Id = instanceId,
            BlueprintId = blueprintId,
            BlueprintVersion = 1,
            RegisterId = "reg-1",
            TenantId = "default",
            CurrentActionIds = [1],
            ParticipantWallets = new Dictionary<string, string> { ["citizen"] = participantWallet },
        };

    private static Sorcha.Blueprint.Models.Action MakeAction() => new()
    {
        Id = 1,
        Title = "Claim your Assured Identity credential",
        Sender = "citizen",
        Disclosures = [new Disclosure { ParticipantAddress = "assessor", DataPointers = ["/email"] }],
        DataSchemas = [JsonDocument.Parse("""{"type":"object","properties":{"email":{"type":"string"}}}""")],
        Calculations = new Dictionary<string, System.Text.Json.Nodes.JsonNode>
        {
            ["total"] = System.Text.Json.Nodes.JsonNode.Parse("1"),
        },
        CredentialRequirements = [new CredentialRequirement { Type = "SomeCredential" }],
        CredentialIssuanceConfig = new CredentialIssuanceConfig { CredentialType = "AssuredIdentity" },
        RejectionConfig = new RejectionConfig { TargetActionId = 0, TargetParticipantId = "reviewer" },
        Routes = [new Route { Id = "r1", NextActionIds = [2] }],
        Participants = [new Condition { Principal = "assessor" }],
        AdditionalRecipients = ["assessor-wallet"],
    };

    private static HttpContext ContextWithWallet(string? walletAddress)
    {
        var claims = new List<Claim>();
        if (walletAddress is not null)
        {
            claims.Add(new Claim("wallet_address", walletAddress));
        }
        var identity = new ClaimsIdentity(claims, "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private static Task<IResult> InvokeAsync(
        HttpContext httpContext,
        string instanceId,
        int actionId,
        IInstanceStore instanceStore,
        IActionResolverService actionResolver)
    {
        var method = typeof(InstanceActionEndpoints).GetMethod(
            "GetInstanceActionSchema",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("GetInstanceActionSchema should be reachable for reflection-based endpoint testing");

        var result = method!.Invoke(null, [httpContext, instanceId, actionId, instanceStore, actionResolver, CancellationToken.None]);
        return (Task<IResult>)result!;
    }

    [Fact]
    public async Task GetInstanceActionSchema_ConsumerTierParticipant_ReturnsOk()
    {
        // This is the P0 regression: a consumer-tier citizen token (identified here purely by the
        // presence of a wallet_address claim and absence of any org/service claim — the endpoint does
        // not require platform/service tier at all) must be able to read the action it's about to fill in.
        var instance = MakeInstance(CitizenWallet);
        var action = MakeAction();
        var blueprint = new BlueprintModel { Id = "bp-1", Title = "AIAS", Description = "desc-desc", Actions = [action] };

        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);

        var resolver = new Mock<IActionResolverService>();
        resolver.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        resolver.Setup(r => r.GetActionDefinition(blueprint, "1")).Returns(action);

        var result = await InvokeAsync(
            ContextWithWallet(CitizenWallet), "inst-1", 1, instanceStore.Object, resolver.Object);

        var ok = result.Should().BeOfType<Ok<InstanceActionSchemaResponse>>().Subject;
        ok.Value!.ActionId.Should().Be(1);
        ok.Value.Title.Should().Be("Claim your Assured Identity credential");
    }

    [Fact]
    public async Task GetInstanceActionSchema_ConsumerTierParticipant_DoesNotLeakRoutingOrOtherParticipantFields()
    {
        var instance = MakeInstance(CitizenWallet);
        var action = MakeAction();
        var blueprint = new BlueprintModel { Id = "bp-1", Title = "AIAS", Description = "desc-desc", Actions = [action] };

        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);

        var resolver = new Mock<IActionResolverService>();
        resolver.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        resolver.Setup(r => r.GetActionDefinition(blueprint, "1")).Returns(action);

        var result = await InvokeAsync(
            ContextWithWallet(CitizenWallet), "inst-1", 1, instanceStore.Object, resolver.Object);

        var ok = result.Should().BeOfType<Ok<InstanceActionSchemaResponse>>().Subject;

        // Serialize exactly as the wire response would be and assert the sensitive/internal
        // fields are structurally absent — not merely null-valued on a type that happens to carry
        // them (the response DTO has no properties for these at all).
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var propertyNames = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        propertyNames.Should().NotContain(new[]
        {
            "routes", "condition", "participants", "target", "additionalRecipients",
            "requiredPriorActions", "rejectionConfig", "disclosures", "sender",
            "blueprint", "previousTxId", "notification", "additionalProperties",
        });

        // And confirm the DTO type itself has no such properties (belt-and-braces: this fails even
        // if a future edit adds one of these fields back with [JsonIgnore(WhenWritingNull)] and a
        // null value happened to hide it from the serialized JSON above).
        var dtoProperties = typeof(InstanceActionSchemaResponse).GetProperties().Select(p => p.Name).ToList();
        dtoProperties.Should().NotContain(new[]
        {
            "Routes", "Condition", "Participants", "Target", "AdditionalRecipients",
            "RequiredPriorActions", "RejectionConfig", "Disclosures", "Sender", "BlueprintId",
            "PreviousTxId", "Notification", "AdditionalProperties",
        });
    }

    [Fact]
    public async Task GetInstanceActionSchema_WalletNotAParticipant_ReturnsForbidden()
    {
        var instance = MakeInstance(OtherWallet);
        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);

        var resolver = new Mock<IActionResolverService>(MockBehavior.Strict);

        var result = await InvokeAsync(
            ContextWithWallet(CitizenWallet), "inst-1", 1, instanceStore.Object, resolver.Object);

        result.GetType().Name.Should().Contain("ProblemHttpResult");
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetInstanceActionSchema_NoWalletClaim_ReturnsForbidden()
    {
        var instance = MakeInstance(CitizenWallet);
        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);

        var resolver = new Mock<IActionResolverService>(MockBehavior.Strict);

        var result = await InvokeAsync(
            ContextWithWallet(null), "inst-1", 1, instanceStore.Object, resolver.Object);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task GetInstanceActionSchema_InstanceNotFound_ReturnsNotFound()
    {
        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((Instance?)null);
        var resolver = new Mock<IActionResolverService>(MockBehavior.Strict);

        var result = await InvokeAsync(
            ContextWithWallet(CitizenWallet), "missing", 1, instanceStore.Object, resolver.Object);

        result.GetType().Name.Should().Contain("NotFound");
    }

    [Fact]
    public async Task GetInstanceActionSchema_ActionNotInBlueprint_ReturnsNotFound()
    {
        var instance = MakeInstance(CitizenWallet);
        var blueprint = new BlueprintModel { Id = "bp-1", Title = "AIAS", Description = "desc-desc", Actions = [] };

        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);

        var resolver = new Mock<IActionResolverService>();
        resolver.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        resolver.Setup(r => r.GetActionDefinition(blueprint, "1")).Returns((Sorcha.Blueprint.Models.Action?)null);

        var result = await InvokeAsync(
            ContextWithWallet(CitizenWallet), "inst-1", 1, instanceStore.Object, resolver.Object);

        result.GetType().Name.Should().Contain("NotFound");
    }
}
