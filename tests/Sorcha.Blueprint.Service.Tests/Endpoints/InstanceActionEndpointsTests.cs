// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Wallet;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// P0 fix (<c>fix/pwa-p0-claim-and-camera</c>) — <c>GET /api/instances/{instanceId}/actions/{actionId}</c>
/// must be readable by a genuine consumer-tier citizen token, must gate on (at least one of) the caller's
/// resolved wallets being a recorded participant on the instance, and must return only the form-relevant
/// subset of the action — not the whole blueprint and not routing/other-participant internals.
///
/// This test file previously carried a load-bearing defect: <c>GetInstanceActionSchema_ConsumerTierParticipant_ReturnsOk</c>
/// built its principal with ONLY a <c>wallet_address</c> claim — which, per <c>TokenService.GenerateUserTokenAsync</c>,
/// is the PLATFORM-tier claim shape (<c>AddWalletAddressClaimAsync</c> only fires for <c>Tier.Platform</c>).
/// A real consumer-tier token (every PWA sign-in, Feature 136) never carries <c>wallet_address</c> — so the
/// "consumer" test was actually exercising the platform-tier fast path, the endpoint's participant gate read
/// a claim citizens never carry, and every genuine citizen 403'd. Fixed by rewiring the gate through
/// <c>ParticipantWalletResolver</c> (claim fast path, then Wallet-Service-by-owner fallback) and rewriting
/// the tests below to use a genuine consumer-tier claim set.
/// </summary>
public sealed class InstanceActionEndpointsTests
{
    private const string CitizenWallet = "ws1qcitizen000000000000000000000000000000";
    private const string OtherWallet = "ws1qother00000000000000000000000000000000";
    private const string PlatformUserId = "platform-user-1";

    private static Instance MakeInstance(string participantWallet, string blueprintId = "bp-1", string instanceId = "inst-1") =>
        new()
        {
            Id = instanceId,
            BlueprintId = blueprintId,
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: execution resolves and chains by the pin
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

    /// <summary>Fast-path context: a caller whose principal carries the <c>wallet_address</c> claim directly.</summary>
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

    /// <summary>
    /// Genuine consumer-tier claim set per <c>TokenService.GenerateUserTokenAsync</c> — <c>sub</c>,
    /// <c>email</c>, <c>org_id</c>, <c>platform_user_id</c>; explicitly NO <c>wallet_address</c> and NO
    /// roles (both are platform-tier-only, FR-014). The caller's wallet must be resolved via the
    /// Wallet-Service-by-owner fallback, exactly like a real citizen.
    /// </summary>
    private static HttpContext ConsumerTierContext(string platformUserId = PlatformUserId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", "user-1"),
            new Claim("email", "citizen@example.com"),
            new Claim("org_id", "org-1"),
            new Claim("platform_user_id", platformUserId),
        ], "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private static WalletInfo Wallet(string address) =>
        new()
        {
            Address = address, Name = "w", PublicKey = "pk", Algorithm = "ED25519",
            Status = "Active", Owner = "owner", Tenant = "tenant",
        };

    /// <summary>Wallet client mock resolving <paramref name="addresses"/> for <paramref name="owner"/>, empty for anyone else.</summary>
    private static Mock<IWalletServiceClient> WalletClientReturning(string owner, params string[] addresses)
    {
        var mock = new Mock<IWalletServiceClient>();
        mock.Setup(c => c.GetWalletsByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string requestedOwner, CancellationToken _) =>
                string.Equals(requestedOwner, owner, StringComparison.Ordinal)
                    ? addresses.Select(Wallet).ToList()
                    : new List<WalletInfo>());
        return mock;
    }

    /// <summary>Wallet client mock that resolves no wallet for any owner (Wallet Service reachable but empty).</summary>
    private static Mock<IWalletServiceClient> WalletClientReturningNothing()
    {
        var mock = new Mock<IWalletServiceClient>();
        mock.Setup(c => c.GetWalletsByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalletInfo>());
        return mock;
    }

    private static Task<IResult> InvokeAsync(
        HttpContext httpContext,
        string instanceId,
        int actionId,
        IInstanceStore instanceStore,
        IActionResolverService actionResolver,
        IWalletServiceClient walletClient)
    {
        var method = typeof(InstanceActionEndpoints).GetMethod(
            "GetInstanceActionSchema",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("GetInstanceActionSchema should be reachable for reflection-based endpoint testing");

        var result = method!.Invoke(null, [
            httpContext, instanceId, actionId, instanceStore, actionResolver,
            walletClient, NullLogger<InstanceActionEndpoints.InstanceActionEndpointsLogCategory>.Instance,
            CancellationToken.None,
        ]);
        return (Task<IResult>)result!;
    }

    [Fact]
    public async Task GetInstanceActionSchema_ConsumerTierParticipant_ReturnsOk()
    {
        // The P0 regression, reproduced honestly this time: a genuine consumer-tier citizen token —
        // sub/email/org_id/platform_user_id, NO wallet_address, NO roles — must be able to read the
        // action it's about to fill in. The caller's wallet is resolved via the Wallet-Service-by-owner
        // fallback (ParticipantWalletResolver), exactly as a real PWA sign-in would require.
        var instance = MakeInstance(CitizenWallet);
        var action = MakeAction();
        var blueprint = new BlueprintModel { Id = "bp-1", Title = "AIAS", Description = "desc-desc", Actions = [action] };

        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);

        var resolver = new Mock<IActionResolverService>();
        resolver.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        resolver.Setup(r => r.GetActionDefinition(blueprint, "1")).Returns(action);

        var walletClient = WalletClientReturning(PlatformUserId, CitizenWallet);

        var result = await InvokeAsync(
            ConsumerTierContext(), "inst-1", 1, instanceStore.Object, resolver.Object, walletClient.Object);

        var ok = result.Should().BeOfType<Ok<InstanceActionSchemaResponse>>().Subject;
        ok.Value!.ActionId.Should().Be(1);
        ok.Value.Title.Should().Be("Claim your Assured Identity credential");
        walletClient.Verify(c => c.GetWalletsByOwnerAsync(PlatformUserId, It.IsAny<CancellationToken>()), Times.Once);
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
        resolver.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        resolver.Setup(r => r.GetActionDefinition(blueprint, "1")).Returns(action);

        var walletClient = WalletClientReturning(PlatformUserId, CitizenWallet);

        var result = await InvokeAsync(
            ConsumerTierContext(), "inst-1", 1, instanceStore.Object, resolver.Object, walletClient.Object);

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
        // Fast-path claim resolves to CitizenWallet, but the instance's recorded participant is a
        // different wallet — no Wallet Service call needed (claim fast path short-circuits).
        var instance = MakeInstance(OtherWallet);
        var action = MakeAction();   // IsStartingAction defaults false — a mid-workflow action.
        var blueprint = new BlueprintModel
        {
            Id = "bp-1", Title = "AIAS", Description = "desc-desc", Actions = [action],
            Participants = [new Participant { Id = "citizen", Name = "Citizen", WalletAddress = OtherWallet }],
        };

        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);

        // #1183 changed the ordering: the blueprint is now resolved BEFORE the gate decides, because
        // an open-participant starting action must be readable by a not-yet-bound citizen. So a
        // non-participant costs one extra (cached) blueprint lookup where it previously cost none —
        // the resolver is no longer strict-with-no-calls here. The gate's verdict is unchanged.
        var resolver = new Mock<IActionResolverService>();
        resolver.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        resolver.Setup(r => r.GetActionDefinition(blueprint, "1")).Returns(action);

        var walletClient = new Mock<IWalletServiceClient>(MockBehavior.Strict);

        var result = await InvokeAsync(
            ContextWithWallet(CitizenWallet), "inst-1", 1, instanceStore.Object, resolver.Object, walletClient.Object);

        result.GetType().Name.Should().Contain("ProblemHttpResult");
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        walletClient.VerifyNoOtherCalls();
    }

    /// <summary>
    /// #1183 — an open-participant STARTING action (sender left unbound in the published blueprint
    /// per Feature 103) must be readable by a citizen who is not yet a participant, because
    /// late-binding only happens at submit. Previously this 403'd: the citizen could not open the
    /// blank form they were meant to fill in.
    /// </summary>
    [Fact]
    public async Task GetInstanceActionSchema_OpenParticipantStartingAction_ReadableByNonParticipant()
    {
        var instance = MakeInstance(OtherWallet);
        var action = MakeAction();
        action.IsStartingAction = true;
        var blueprint = new BlueprintModel
        {
            Id = "bp-1", Title = "AIAS", Description = "desc-desc", Actions = [action],
            // Open participant: WalletAddress deliberately null (VAL_BP_010 enforces this at publish).
            Participants = [new Participant { Id = "citizen", Name = "Citizen", WalletAddress = null }],
        };

        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);

        var resolver = new Mock<IActionResolverService>();
        resolver.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        resolver.Setup(r => r.GetActionDefinition(blueprint, "1")).Returns(action);

        var result = await InvokeAsync(
            ContextWithWallet(CitizenWallet), "inst-1", 1,
            instanceStore.Object, resolver.Object, WalletClientReturningNothing().Object);

        var ok = result.Should().BeOfType<Ok<InstanceActionSchemaResponse>>().Subject;
        ok.Value!.ActionId.Should().Be(1);
    }

    /// <summary>
    /// The open-participant allowance is scoped to STARTING actions only. A pre-bound sender on a
    /// starting action is a closed participant (and is itself a publish-time error, VAL_BP_010), so
    /// it must not open the door.
    /// </summary>
    [Fact]
    public async Task GetInstanceActionSchema_StartingActionWithBoundSender_StillForbiddenToNonParticipant()
    {
        var instance = MakeInstance(OtherWallet);
        var action = MakeAction();
        action.IsStartingAction = true;
        var blueprint = new BlueprintModel
        {
            Id = "bp-1", Title = "AIAS", Description = "desc-desc", Actions = [action],
            Participants = [new Participant { Id = "citizen", Name = "Citizen", WalletAddress = OtherWallet }],
        };

        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);

        var resolver = new Mock<IActionResolverService>();
        resolver.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        resolver.Setup(r => r.GetActionDefinition(blueprint, "1")).Returns(action);

        var result = await InvokeAsync(
            ContextWithWallet(CitizenWallet), "inst-1", 1,
            instanceStore.Object, resolver.Object, WalletClientReturningNothing().Object);

        ((ProblemHttpResult)result).StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// The reordering must not turn the gate into a probe. A non-participant asking for an action
    /// that does NOT exist must get the same 403 as one asking for an action that does — otherwise
    /// 404-vs-403 leaks which actions a blueprint contains.
    /// </summary>
    [Fact]
    public async Task GetInstanceActionSchema_NonParticipant_CannotDistinguishMissingActionFromForbidden()
    {
        var instance = MakeInstance(OtherWallet);
        var blueprint = new BlueprintModel
        {
            Id = "bp-1", Title = "AIAS", Description = "desc-desc", Actions = [],
            Participants = [new Participant { Id = "citizen", Name = "Citizen", WalletAddress = OtherWallet }],
        };

        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);

        var resolver = new Mock<IActionResolverService>();
        resolver.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        resolver.Setup(r => r.GetActionDefinition(blueprint, "99")).Returns((Sorcha.Blueprint.Models.Action?)null);

        var result = await InvokeAsync(
            ContextWithWallet(CitizenWallet), "inst-1", 99,
            instanceStore.Object, resolver.Object, WalletClientReturningNothing().Object);

        ((ProblemHttpResult)result).StatusCode.Should().Be(
            StatusCodes.Status403Forbidden,
            "a non-participant must not learn whether the action exists");
    }

    [Fact]
    public async Task GetInstanceActionSchema_NoWalletClaim_NoResolvableWallet_ReturnsForbidden()
    {
        // Reframe of the old (bug-asserting) NoWalletClaim test: no wallet_address claim AND the
        // Wallet Service fallback resolves nothing for this caller — genuinely "couldn't resolve any
        // wallet", so 403 is correct fail-closed behaviour, not the old "gate reads a claim citizens
        // never carry" bug.
        var instance = MakeInstance(CitizenWallet);
        var action = MakeAction();   // Not a starting action — the open-participant door stays shut.
        var blueprint = new BlueprintModel
        {
            Id = "bp-1", Title = "AIAS", Description = "desc-desc", Actions = [action],
            Participants = [new Participant { Id = "citizen", Name = "Citizen", WalletAddress = CitizenWallet }],
        };

        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);

        // Non-strict since #1183: the blueprint is now resolved before the gate decides (see the
        // comment on GetInstanceActionSchema_WalletNotAParticipant_ReturnsForbidden).
        var resolver = new Mock<IActionResolverService>();
        resolver.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        resolver.Setup(r => r.GetActionDefinition(blueprint, "1")).Returns(action);

        var walletClient = WalletClientReturningNothing();

        var result = await InvokeAsync(
            ConsumerTierContext(), "inst-1", 1, instanceStore.Object, resolver.Object, walletClient.Object);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        walletClient.Verify(c => c.GetWalletsByOwnerAsync(PlatformUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetInstanceActionSchema_NoWalletClaim_ResolvableWalletIsParticipant_ReturnsOk()
    {
        // No wallet_address claim, but the Wallet Service fallback resolves a wallet that IS the
        // instance's recorded participant — this is the exact citizen path the P0 fix restores.
        var instance = MakeInstance(CitizenWallet);
        var action = MakeAction();
        var blueprint = new BlueprintModel { Id = "bp-1", Title = "AIAS", Description = "desc-desc", Actions = [action] };

        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);

        var resolver = new Mock<IActionResolverService>();
        resolver.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        resolver.Setup(r => r.GetActionDefinition(blueprint, "1")).Returns(action);

        var walletClient = WalletClientReturning(PlatformUserId, CitizenWallet);

        var result = await InvokeAsync(
            ConsumerTierContext(), "inst-1", 1, instanceStore.Object, resolver.Object, walletClient.Object);

        var ok = result.Should().BeOfType<Ok<InstanceActionSchemaResponse>>().Subject;
        ok.Value!.ActionId.Should().Be(1);
    }

    [Fact]
    public async Task GetInstanceActionSchema_CallerHasMultipleWallets_ReturnsOkWhenAnyIsParticipant()
    {
        // A caller can own more than one wallet; membership must be checked against the FULL resolved
        // set, not just the first address returned.
        const string secondWallet = "ws1qsecond000000000000000000000000000000";
        var instance = MakeInstance(secondWallet);
        var action = MakeAction();
        var blueprint = new BlueprintModel { Id = "bp-1", Title = "AIAS", Description = "desc-desc", Actions = [action] };

        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);

        var resolver = new Mock<IActionResolverService>();
        resolver.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        resolver.Setup(r => r.GetActionDefinition(blueprint, "1")).Returns(action);

        var walletClient = WalletClientReturning(PlatformUserId, CitizenWallet, secondWallet);

        var result = await InvokeAsync(
            ConsumerTierContext(), "inst-1", 1, instanceStore.Object, resolver.Object, walletClient.Object);

        result.Should().BeOfType<Ok<InstanceActionSchemaResponse>>();
    }

    [Fact]
    public async Task GetInstanceActionSchema_InstanceNotFound_ReturnsNotFound()
    {
        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((Instance?)null);
        var resolver = new Mock<IActionResolverService>(MockBehavior.Strict);
        var walletClient = new Mock<IWalletServiceClient>(MockBehavior.Strict);

        var result = await InvokeAsync(
            ContextWithWallet(CitizenWallet), "missing", 1, instanceStore.Object, resolver.Object, walletClient.Object);

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
        resolver.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        resolver.Setup(r => r.GetActionDefinition(blueprint, "1")).Returns((Sorcha.Blueprint.Models.Action?)null);

        var walletClient = new Mock<IWalletServiceClient>(MockBehavior.Strict);

        var result = await InvokeAsync(
            ContextWithWallet(CitizenWallet), "inst-1", 1, instanceStore.Object, resolver.Object, walletClient.Object);

        result.GetType().Name.Should().Contain("NotFound");
    }
}
