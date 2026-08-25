// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Infrastructure;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Wallet;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// Issue #1182 — the three READ endpoints on <c>/api/instances</c> returned instance content to ANY
/// authenticated caller. The group policy <c>CanExecuteBlueprints</c> resolves to a bare
/// <c>RequireAuthenticatedUser()</c>, so knowledge of a GUID was the only thing protecting another
/// citizen's in-flight identity application — name, date of birth, address and portrait tokens in
/// <c>AccumulatedData</c>, plus the whole participant→wallet map.
///
/// <para>These tests pin BOTH directions of the gate, because this check is wrong in two opposite
/// ways if written naively, and both have already been made once in this codebase:</para>
/// <list type="bullet">
/// <item><description>Too open — no participant check at all (the defect).</description></item>
/// <item><description>Too closed — reading <c>wallet_address</c> off the claim set, which a
/// consumer-tier token never carries (Feature 136), thereby 403ing every genuine citizen while
/// leaving platform-tier callers unrestricted; and gating on participation alone, which 403s the
/// open (Feature 103) applicant on their own freshly-created instance.</description></item>
/// </list>
/// </summary>
public sealed class InstanceReadEndpointsTests
{
    private const string CitizenWallet = "ws1qcitizen000000000000000000000000000000";
    private const string AnalystWallet = "ws1qanalyst00000000000000000000000000000";
    private const string StrangerWallet = "ws1qstranger0000000000000000000000000000";
    private const string CitizenPlatformUserId = "platform-user-citizen";
    private const string StrangerPlatformUserId = "platform-user-stranger";

    private const string SensitiveField = "dateOfBirth";
    private const string SensitiveValue = "1984-02-29";

    // ---------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// An instance mid-flight: one action already completed, so <c>AccumulatedData</c> holds real
    /// citizen data and the applicant has been late-bound into <c>ParticipantWallets</c>.
    /// </summary>
    private static Instance InFlightInstance() => new()
    {
        Id = "inst-1",
        BlueprintId = "bp-1",
        BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: execution resolves and chains by the pin
        BlueprintVersion = 1,
        RegisterId = "reg-1",
        TenantId = "default",
        CurrentActionIds = [2],
        CompletedActionCount = 1,
        ParticipantWallets = new Dictionary<string, string>
        {
            ["citizen"] = CitizenWallet,
            ["analyst"] = AnalystWallet,
        },
        AccumulatedData = new Dictionary<string, object> { [SensitiveField] = SensitiveValue },
    };

    /// <summary>
    /// A freshly-created instance whose starting action belongs to an OPEN participant (Feature 103):
    /// nothing completed, and the applicant is not yet a participant — exactly the state the PWA reads
    /// before the citizen has typed anything.
    /// </summary>
    private static Instance AwaitingOpenParticipantInstance() => new()
    {
        Id = "inst-open",
        BlueprintId = "bp-1",
        BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: execution resolves and chains by the pin
        BlueprintVersion = 1,
        RegisterId = "reg-1",
        TenantId = "default",
        CurrentActionIds = [1],
        CompletedActionCount = 0,
        // CreateInstance seeds only participants that already carry a wallet — the open applicant
        // is deliberately absent.
        ParticipantWallets = new Dictionary<string, string> { ["analyst"] = AnalystWallet },
        AccumulatedData = new Dictionary<string, object>(),
    };

    /// <summary>Blueprint whose action 1 is a starting action sent by an unbound (open) participant.</summary>
    private static BlueprintModel OpenParticipantBlueprint() => new()
    {
        Id = "bp-1",
        Title = "Assured Identity",
        Participants =
        [
            new Sorcha.Blueprint.Models.Participant { Id = "citizen", Name = "Citizen", WalletAddress = null },
            new Sorcha.Blueprint.Models.Participant { Id = "analyst", Name = "Analyst", WalletAddress = AnalystWallet },
        ],
        Actions =
        [
            new Sorcha.Blueprint.Models.Action { Id = 1, Title = "Apply", Sender = "citizen", IsStartingAction = true },
            new Sorcha.Blueprint.Models.Action { Id = 2, Title = "Assess", Sender = "analyst" },
        ],
    };

    // ---------------------------------------------------------------------------------------
    // Principals
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Genuine consumer-tier claim set per <c>TokenService.GenerateUserTokenAsync</c> — explicitly NO
    /// <c>wallet_address</c> and no roles, both of which are platform-tier only (Feature 136). The
    /// caller's wallet must be resolved via the Wallet-Service-by-owner fallback, like a real citizen.
    /// </summary>
    private static HttpContext ConsumerTierContext(string platformUserId) =>
        new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "user-" + platformUserId),
                new Claim("email", platformUserId + "@example.com"),
                new Claim("org_id", "org-1"),
                new Claim("platform_user_id", platformUserId),
            ], "test")),
        };

    /// <summary>Platform-tier claim set — carries <c>wallet_address</c> directly (the claim fast path).</summary>
    private static HttpContext PlatformTierContext(string walletAddress) =>
        new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("wallet_address", walletAddress)], "test")),
        };

    private static HttpContext WithDelegationToken(HttpContext context)
    {
        context.Items["DelegationToken"] = "delegation-token";
        return context;
    }

    private static WalletInfo Wallet(string address) => new()
    {
        Address = address, Name = "w", PublicKey = "pk", Algorithm = "ED25519",
        Status = "Active", Owner = "owner", Tenant = "tenant",
    };

    private static IWalletServiceClient WalletClientFor(string owner, params string[] addresses)
    {
        var mock = new Mock<IWalletServiceClient>();
        mock.Setup(c => c.GetWalletsByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string requestedOwner, CancellationToken _) =>
                string.Equals(requestedOwner, owner, StringComparison.Ordinal)
                    ? addresses.Select(Wallet).ToList()
                    : new List<WalletInfo>());
        return mock.Object;
    }

    private static IInstanceStore StoreWith(Instance instance)
    {
        var mock = new Mock<IInstanceStore>();
        mock.Setup(s => s.GetAsync(instance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        return mock.Object;
    }

    private static IBlueprintStore BlueprintStoreWith(BlueprintModel? blueprint)
    {
        var mock = new Mock<IBlueprintStore>();
        mock.Setup(s => s.GetAsync(It.IsAny<string>())).ReturnsAsync(blueprint);
        return mock.Object;
    }

    private static IStateReconstructionService StateServiceReturning(AccumulatedState state)
    {
        var mock = new Mock<IStateReconstructionService>();
        mock.Setup(s => s.ReconstructAsync(
                It.IsAny<BlueprintModel>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        return mock.Object;
    }

    // ---------------------------------------------------------------------------------------
    // Reflection invokers (static handlers, no WebApplicationFactory — matches InstanceActionEndpointsTests)
    // ---------------------------------------------------------------------------------------

    private static Task<IResult> InvokeAsync(string handlerName, params object?[] args)
    {
        var method = typeof(InstanceReadEndpoints).GetMethod(
            handlerName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"{handlerName} should be reachable for reflection-based endpoint testing");
        return (Task<IResult>)method!.Invoke(null, args)!;
    }

    private static Task<IResult> GetInstance(
        HttpContext ctx, Instance instance, BlueprintModel? blueprint, IWalletServiceClient wallets) =>
        InvokeAsync(nameof(InstanceReadEndpoints.GetInstance),
            ctx, instance.Id, StoreWith(instance), BlueprintStoreWith(blueprint), wallets,
            NullLogger<InstanceReadEndpoints.InstanceReadEndpointsLogCategory>.Instance,
            CancellationToken.None);

    private static Task<IResult> GetInstanceState(
        HttpContext ctx, Instance instance, BlueprintModel? blueprint, IWalletServiceClient wallets) =>
        InvokeAsync(nameof(InstanceReadEndpoints.GetInstanceState),
            ctx, instance.Id,
            StateServiceReturning(new AccumulatedState { ActionCount = 1 }),
            StoreWith(instance), BlueprintStoreWith(blueprint), wallets,
            NullLogger<InstanceReadEndpoints.InstanceReadEndpointsLogCategory>.Instance,
            CancellationToken.None);

    private static Task<IResult> GetNextActions(
        HttpContext ctx, Instance instance, BlueprintModel? blueprint, IWalletServiceClient wallets) =>
        InvokeAsync(nameof(InstanceReadEndpoints.GetNextActions),
            ctx, instance.Id, StoreWith(instance), BlueprintStoreWith(blueprint), wallets,
            NullLogger<InstanceReadEndpoints.InstanceReadEndpointsLogCategory>.Instance,
            CancellationToken.None);

    private static int? StatusOf(IResult result) =>
        result as IStatusCodeHttpResult is { } s ? s.StatusCode : null;

    // ---------------------------------------------------------------------------------------
    // The defect: a stranger reads another citizen's in-flight application
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task GetInstance_AuthenticatedNonParticipant_IsForbidden()
    {
        var instance = InFlightInstance();
        var result = await GetInstance(
            ConsumerTierContext(StrangerPlatformUserId),
            instance,
            OpenParticipantBlueprint(),
            WalletClientFor(StrangerPlatformUserId, StrangerWallet));

        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden,
            "a caller who controls no wallet on this instance must not read another citizen's application");
    }

    [Fact]
    public async Task GetInstance_AuthenticatedNonParticipant_DoesNotLeakAccumulatedData()
    {
        var instance = InFlightInstance();
        var result = await GetInstance(
            ConsumerTierContext(StrangerPlatformUserId),
            instance,
            OpenParticipantBlueprint(),
            WalletClientFor(StrangerPlatformUserId, StrangerWallet));

        // Assert on the RESULT'S CARRIED VALUE, not on a serialisation of the IResult wrapper.
        // The first version of this test serialised `result` itself — which serialises the declared
        // IResult type and therefore never contained the payload, passing identically whether or not
        // the endpoint leaked. It proved nothing. AccumulatedData is where name / date of birth /
        // address / portrait tokens live, so the object itself must not reach a non-participant.
        var carried = (result as IValueHttpResult)?.Value;
        carried.Should().NotBeOfType<Instance>(
            "the response must not carry the Instance (and its AccumulatedData) to a non-participant");
    }

    [Fact]
    public async Task GetInstanceState_AuthenticatedNonParticipant_IsForbidden()
    {
        var instance = InFlightInstance();
        var result = await GetInstanceState(
            WithDelegationToken(ConsumerTierContext(StrangerPlatformUserId)),
            instance,
            OpenParticipantBlueprint(),
            WalletClientFor(StrangerPlatformUserId, StrangerWallet));

        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task GetNextActions_AuthenticatedNonParticipant_IsForbidden()
    {
        var instance = InFlightInstance();
        var result = await GetNextActions(
            ConsumerTierContext(StrangerPlatformUserId),
            instance,
            OpenParticipantBlueprint(),
            WalletClientFor(StrangerPlatformUserId, StrangerWallet));

        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
    }

    // ---------------------------------------------------------------------------------------
    // The opposite failure: the gate must not lock out the people it exists to serve
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task GetInstance_ConsumerTierParticipant_ReturnsOk()
    {
        // A real citizen token carries no wallet_address, so this only passes if the gate resolves
        // wallets through the Wallet-Service-by-owner fallback rather than reading the claim.
        var instance = InFlightInstance();
        var result = await GetInstance(
            ConsumerTierContext(CitizenPlatformUserId),
            instance,
            OpenParticipantBlueprint(),
            WalletClientFor(CitizenPlatformUserId, CitizenWallet));

        StatusOf(result).Should().NotBe(StatusCodes.Status403Forbidden,
            "a consumer-tier citizen who IS a participant must be able to read their own application");
    }

    [Fact]
    public async Task GetInstance_PlatformTierParticipant_ReturnsOk()
    {
        var instance = InFlightInstance();
        var result = await GetInstance(
            PlatformTierContext(AnalystWallet),
            instance,
            OpenParticipantBlueprint(),
            WalletClientFor("nobody"));

        StatusOf(result).Should().NotBe(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task GetInstance_InstanceAwaitingOpenParticipant_IsReadableByAnyAuthenticatedCaller()
    {
        // The PWA reads this endpoint to discover the current action BEFORE the citizen submits, at
        // which point they are not yet a participant. Refusing here breaks the apply flow for every
        // citizen (the #1183 regression class).
        var instance = AwaitingOpenParticipantInstance();
        var result = await GetInstance(
            ConsumerTierContext(StrangerPlatformUserId),
            instance,
            OpenParticipantBlueprint(),
            WalletClientFor(StrangerPlatformUserId, StrangerWallet));

        StatusOf(result).Should().NotBe(StatusCodes.Status403Forbidden,
            "an unstarted instance awaiting its open participant holds no citizen data to protect");
    }

    // ---------------------------------------------------------------------------------------
    // The carve-out must be narrow: it closes the moment real content exists
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task GetInstance_OpenStartingActionButWorkAlreadyCompleted_IsForbidden()
    {
        // Same open-participant blueprint, but an action has completed — so AccumulatedData is
        // populated and the applicant is late-bound. The carve-out must NOT still apply here, or it
        // is a blanket hole rather than an empty-shell exemption.
        var instance = InFlightInstance();
        instance.CurrentActionIds = [1];  // action 1 is the open starting action
        instance.CompletedActionCount = 1;

        var result = await GetInstance(
            ConsumerTierContext(StrangerPlatformUserId),
            instance,
            OpenParticipantBlueprint(),
            WalletClientFor(StrangerPlatformUserId, StrangerWallet));

        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden,
            "once any action has completed the instance carries real data and the carve-out must close");
    }

    [Fact]
    public void IsAwaitingOpenParticipant_UnresolvableBlueprint_FailsClosed()
    {
        // On a replica the blueprint may not have replicated yet (CreateInstance has its own fallback
        // for this). An unverifiable open-participant claim must not be granted.
        InstanceParticipantGate
            .IsAwaitingOpenParticipant(AwaitingOpenParticipantInstance(), blueprint: null)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAwaitingOpenParticipant_StartingActionSenderIsBound_FailsClosed()
    {
        var blueprint = OpenParticipantBlueprint();
        blueprint.Participants.First(p => p.Id == "citizen").WalletAddress = CitizenWallet;

        InstanceParticipantGate
            .IsAwaitingOpenParticipant(AwaitingOpenParticipantInstance(), blueprint)
            .Should().BeFalse("a starting action with a pre-bound sender is not an open participant");
    }

    // ---------------------------------------------------------------------------------------
    // The same root cause, opposite symptom: the LIST endpoint read wallet_address off the claim
    // set, which a consumer-tier token never carries — so every citizen saw an empty list of their
    // own applications. Not a disclosure bug, but the same missing resolver.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ListInstances_ConsumerTierCitizen_ReturnsTheirInstances()
    {
        var instance = InFlightInstance();
        var store = new Mock<IInstanceStore>();
        store.Setup(s => s.GetByParticipantWalletAsync(
                CitizenWallet, It.IsAny<InstanceState?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { instance });
        store.Setup(s => s.GetByParticipantWalletAsync(
                It.Is<string>(w => w != CitizenWallet), It.IsAny<InstanceState?>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Instance>());

        var result = await InvokeAsync(nameof(InstanceReadEndpoints.ListInstances),
            ConsumerTierContext(CitizenPlatformUserId), store.Object,
            WalletClientFor(CitizenPlatformUserId, CitizenWallet),
            NullLogger<InstanceReadEndpoints.InstanceReadEndpointsLogCategory>.Instance,
            (InstanceState?)null, 1, 20, CancellationToken.None);

        var payload = (result as IValueHttpResult)?.Value;
        payload.Should().NotBeNull();
        var totalCount = payload!.GetType().GetProperty("totalCount")!.GetValue(payload);
        totalCount.Should().Be(1,
            "a consumer-tier citizen carries no wallet_address claim, so the list must resolve their "
            + "wallet via the Wallet-Service owner fallback rather than returning an empty page");
    }

    [Fact]
    public async Task GetInstance_CallerWithNoResolvableWallet_IsForbidden()
    {
        // Empty resolved set means "could not resolve", not "any wallet" — must fail closed.
        var instance = InFlightInstance();
        var result = await GetInstance(
            ConsumerTierContext(StrangerPlatformUserId),
            instance,
            OpenParticipantBlueprint(),
            WalletClientFor("someone-else"));

        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
    }
}
