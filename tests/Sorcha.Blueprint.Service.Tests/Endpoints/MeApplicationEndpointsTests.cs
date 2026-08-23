// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Wallet;

using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// Feature 186 (#1163) — <c>/api/me/applications</c>, the citizen's own applications.
/// </summary>
/// <remarks>
/// Reflection-invoked static handlers, no <c>WebApplicationFactory</c>, matching
/// <c>InstanceReadEndpointsTests</c>.
/// <para>
/// Every principal here is <b>consumer-tier</b> — no <c>wallet_address</c> claim and no roles, as
/// Feature 136 mints them. That is not incidental: reading the wallet off the claim set is what made
/// <c>/api/instances</c> return an empty page to every citizen while the server held their data, and
/// writing this endpoint the same way would reproduce the bug it exists to fix.
/// </para>
/// </remarks>
public sealed class MeApplicationEndpointsTests
{
    private const string CitizenWallet = "ws1qcitizen000000000000000000000000000000";
    private const string SecondWallet = "ws1qcitizen2nd00000000000000000000000000";
    private const string AnalystWallet = "ws1qanalyst00000000000000000000000000000";
    private const string StrangerWallet = "ws1qstranger0000000000000000000000000000";
    private const string CitizenUserId = "platform-user-citizen";
    private const string StrangerUserId = "platform-user-stranger";

    // ---------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------

    private static Instance Application(
        string id = "inst-1",
        InstanceState state = InstanceState.Active,
        int[]? current = null,
        string? routeId = null,
        string? reasonCode = null,
        DateTimeOffset? createdAt = null) => new()
        {
            Id = id,
            BlueprintId = "bp-1",
            BlueprintVersion = 1,
            RegisterId = "reg-1",
            TenantId = "default",
            State = state,
            CurrentActionIds = [.. current ?? [2]],
            CompletedActionCount = 1,
            ParticipantWallets = new Dictionary<string, string>
            {
                ["citizen"] = CitizenWallet,
                ["analyst"] = AnalystWallet,
            },
            DecisionRouteId = routeId,
            DecisionReasonCode = reasonCode,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string> { ["instanceReference"] = "AI-CYB-14-A7K3" },
        };

    private static BlueprintModel Blueprint() => new()
    {
        Id = "bp-1",
        Title = "Assured Identity",
        Participants =
        [
            new Sorcha.Blueprint.Models.Participant { Id = "citizen", Name = "Citizen" },
            new Sorcha.Blueprint.Models.Participant { Id = "analyst", Name = "Analyst", WalletAddress = AnalystWallet },
        ],
        Actions =
        [
            new Sorcha.Blueprint.Models.Action { Id = 1, Title = "Apply", Sender = "citizen", IsStartingAction = true },
            new Sorcha.Blueprint.Models.Action
            {
                Id = 2,
                Title = "Assess",
                Sender = "analyst",
                Routes =
                [
                    new Sorcha.Blueprint.Models.Route
                    {
                        Id = "route-refuse",
                        NextActionIds = [],
                        DecisionNotice = new Sorcha.Blueprint.Models.DecisionNotice
                        {
                            RecipientParticipantId = "citizen",
                            Title = "We could not assure your identity",
                            Severity = "Warning",
                            Reasons = new Dictionary<string, string>
                            {
                                ["DOC_UNREADABLE"] = "The document you provided could not be read clearly.",
                            },
                        },
                    },
                ],
            },
            new Sorcha.Blueprint.Models.Action { Id = 3, Title = "Collect", Sender = "citizen" },
        ],
    };

    /// <summary>Consumer-tier principal: no wallet_address, no roles — exactly as Feature 136 mints it.</summary>
    private static HttpContext ConsumerContext(string platformUserId) =>
        new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "user-" + platformUserId),
                new Claim("platform_user_id", platformUserId),
                new Claim("org_id", "org-1"),
            ], "test")),
        };

    private static WalletInfo Wallet(string address) => new()
    {
        Address = address, Name = "w", PublicKey = "pk", Algorithm = "ED25519",
        Status = "Active", Owner = "owner", Tenant = "tenant",
    };

    private static IWalletServiceClient WalletClientFor(string owner, params string[] addresses)
    {
        var mock = new Mock<IWalletServiceClient>();
        mock.Setup(c => c.GetWalletsByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string requested, CancellationToken _) =>
                string.Equals(requested, owner, StringComparison.Ordinal)
                    ? addresses.Select(Wallet).ToList()
                    : new List<WalletInfo>());
        return mock.Object;
    }

    /// <summary>A store whose by-wallet query returns <paramref name="byWallet"/> for each given wallet.</summary>
    private static IInstanceStore StoreWith(
        Dictionary<string, Instance[]> byWallet, params Instance[] byId)
    {
        var mock = new Mock<IInstanceStore>();
        mock.Setup(s => s.GetByParticipantWalletAsync(
                It.IsAny<string>(), It.IsAny<InstanceState?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wallet, InstanceState? _, int _, int _, CancellationToken _) =>
                byWallet.TryGetValue(wallet, out var list) ? list : []);

        foreach (var instance in byId)
        {
            mock.Setup(s => s.GetAsync(instance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        }

        return mock.Object;
    }

    /// <summary>
    /// Feature 194 — the endpoints resolve the PINNED definition first and fall back to the draft
    /// store. These fixtures' instances carry no pin, so this stand-in returns nothing and the
    /// existing draft-store behaviour is what every test below still exercises. The pinned path has
    /// its own coverage in InstancePinReadTests.
    /// </summary>
    private static IPublishedBlueprintStore EmptyPublishedStore()
    {
        var mock = new Mock<IPublishedBlueprintStore>();
        mock.Setup(s => s.GetByExecDefHashAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((PublishedBlueprint?)null);
        mock.Setup(s => s.GetVersionsAsync(It.IsAny<string>()))
            .ReturnsAsync(Enumerable.Empty<PublishedBlueprint>());
        return mock.Object;
    }

    private static IBlueprintStore BlueprintStoreWith(BlueprintModel? blueprint)
    {
        var mock = new Mock<IBlueprintStore>();
        mock.Setup(s => s.GetAsync(It.IsAny<string>())).ReturnsAsync(blueprint);
        return mock.Object;
    }

    // ---------------------------------------------------------------------------------------
    // Reflection invokers
    // ---------------------------------------------------------------------------------------

    private static Task<IResult> InvokeAsync(string handlerName, params object?[] args)
    {
        var method = typeof(MeApplicationEndpoints).GetMethod(
            handlerName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"{handlerName} should be reachable for reflection-based endpoint testing");
        return (Task<IResult>)method!.Invoke(null, args)!;
    }

    /// <summary>
    /// Paging arguments are passed as <c>int?</c> and default to <see langword="null"/> here, so the
    /// harness exercises the same shape a bare <c>GET /api/me/applications</c> produces. Passing
    /// explicit values (as this harness originally did) is what let a 400 on the real request go
    /// unnoticed: a non-nullable minimal-API query parameter is REQUIRED, and reflection never
    /// crosses the binder.
    /// </summary>
    private static Task<IResult> List(
        HttpContext ctx, IInstanceStore store, BlueprintModel? blueprint, IWalletServiceClient wallets,
        InstanceState? status = null, int? page = null, int? pageSize = null) =>
        InvokeAsync(nameof(MeApplicationEndpoints.ListMyApplications),
            ctx, store, BlueprintStoreWith(blueprint), EmptyPublishedStore(), wallets,
            NullLogger<MeApplicationEndpoints.MeApplicationEndpointsLogCategory>.Instance,
            status, page, pageSize, CancellationToken.None);

    private static Task<IResult> Detail(
        HttpContext ctx, string instanceId, IInstanceStore store, BlueprintModel? blueprint,
        IWalletServiceClient wallets) =>
        InvokeAsync(nameof(MeApplicationEndpoints.GetMyApplication),
            ctx, instanceId, store, BlueprintStoreWith(blueprint), EmptyPublishedStore(), wallets,
            NullLogger<MeApplicationEndpoints.MeApplicationEndpointsLogCategory>.Instance,
            CancellationToken.None);

    private static int? StatusOf(IResult result) =>
        result as IStatusCodeHttpResult is { } s ? s.StatusCode : null;

    private static MyApplicationPage<MyApplicationSummary> PageOf(IResult result) =>
        ((Ok<MyApplicationPage<MyApplicationSummary>>)result).Value!;

    private static MyApplicationDetail DetailOf(IResult result) =>
        ((Ok<MyApplicationDetail>)result).Value!;

    // ---------------------------------------------------------------------------------------
    // A consumer-tier citizen sees their own applications
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task List_ConsumerTierCitizen_SeesTheirApplications()
    {
        // The regression that matters: a token with no wallet_address claim must still resolve to
        // the citizen's wallets, via the by-owner lookup.
        var result = await List(
            ConsumerContext(CitizenUserId),
            StoreWith(new() { [CitizenWallet] = [Application()] }),
            Blueprint(),
            WalletClientFor(CitizenUserId, CitizenWallet));

        var page = PageOf(result);
        page.TotalCount.Should().Be(1);
        page.Items.Single().InstanceId.Should().Be("inst-1");
        page.Items.Single().BlueprintTitle.Should().Be("Assured Identity");
        page.Items.Single().InstanceReference.Should().Be("AI-CYB-14-A7K3");
    }

    [Fact]
    public async Task List_StateIsAName_NeverAnInteger()
    {
        // InstanceState serialises as an integer by default in this service (no
        // JsonStringEnumConverter), and the old client model expected a string named "Status" — so it
        // silently read "active" for every application, rejected ones included.
        var result = await List(
            ConsumerContext(CitizenUserId),
            StoreWith(new() { [CitizenWallet] = [Application(state: InstanceState.Completed, current: [])] }),
            Blueprint(),
            WalletClientFor(CitizenUserId, CitizenWallet));

        PageOf(result).Items.Single().State.Should().Be("Completed");
    }

    [Fact]
    public async Task List_CallerWithNoResolvableWallet_GetsAnEmptyPage_NotAnError()
    {
        var result = await List(
            ConsumerContext(CitizenUserId),
            StoreWith([]),
            Blueprint(),
            WalletClientFor("somebody-else", CitizenWallet));

        var page = PageOf(result);
        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task List_ApplicationSharedAcrossTwoOfTheCallersWallets_IsListedOnce()
    {
        var shared = Application();
        var result = await List(
            ConsumerContext(CitizenUserId),
            StoreWith(new() { [CitizenWallet] = [shared], [SecondWallet] = [shared] }),
            Blueprint(),
            WalletClientFor(CitizenUserId, CitizenWallet, SecondWallet));

        PageOf(result).TotalCount.Should().Be(1, "one application, not one per wallet the caller holds");
    }

    [Fact]
    public async Task List_IsOrderedNewestFirst_WithAStableTiebreak()
    {
        // Paging over an unstable order silently skips and repeats rows; the store promises no
        // ordering of its own.
        var now = DateTimeOffset.UtcNow;
        var older = Application("inst-older", createdAt: now.AddDays(-2));
        var newer = Application("inst-newer", createdAt: now);
        var sameInstantA = Application("inst-a", createdAt: now);

        var result = await List(
            ConsumerContext(CitizenUserId),
            StoreWith(new() { [CitizenWallet] = [older, sameInstantA, newer] }),
            Blueprint(),
            WalletClientFor(CitizenUserId, CitizenWallet));

        PageOf(result).Items.Select(i => i.InstanceId)
            .Should().Equal("inst-a", "inst-newer", "inst-older");
    }

    [Fact]
    public async Task List_TerminalApplicationsAreIncluded()
    {
        var result = await List(
            ConsumerContext(CitizenUserId),
            StoreWith(new()
            {
                [CitizenWallet] =
                [
                    Application("inst-live"),
                    Application("inst-done", InstanceState.Completed, current: []),
                    Application("inst-cancelled", InstanceState.Cancelled, current: []),
                ],
            }),
            Blueprint(),
            WalletClientFor(CitizenUserId, CitizenWallet));

        PageOf(result).TotalCount.Should().Be(3,
            "\"what did I submit\" must include applications that have finished");
    }

    [Fact]
    public async Task List_WithNoPagingArguments_DefaultsRatherThanFailing()
    {
        // A bare GET /api/me/applications must work. It did not: `int page` binds as a REQUIRED
        // query parameter, so the live request returned 400 "Required parameter "int page" was not
        // provided from query string" while every unit test passed — reflection hands the handler
        // values directly and never crosses the binder.
        var result = await List(
            ConsumerContext(CitizenUserId),
            StoreWith(new() { [CitizenWallet] = [Application()] }),
            Blueprint(),
            WalletClientFor(CitizenUserId, CitizenWallet),
            page: null, pageSize: null);

        var page = PageOf(result);
        page.PageNumber.Should().Be(1);
        page.PageSize.Should().Be(20);
        page.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task List_ClampsAnAbsurdPageSize()
    {
        var result = await List(
            ConsumerContext(CitizenUserId),
            StoreWith(new() { [CitizenWallet] = [Application()] }),
            Blueprint(),
            WalletClientFor(CitizenUserId, CitizenWallet),
            pageSize: 100_000);

        PageOf(result).PageSize.Should().Be(100, "an unbounded page size is a denial-of-service lever");
    }

    // ---------------------------------------------------------------------------------------
    // The decision
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task List_RefusedApplication_ReportsNotApprovedWithItsReason()
    {
        var result = await List(
            ConsumerContext(CitizenUserId),
            StoreWith(new()
            {
                [CitizenWallet] =
                [
                    Application(state: InstanceState.Completed, current: [],
                        routeId: "route-refuse", reasonCode: "DOC_UNREADABLE"),
                ],
            }),
            Blueprint(),
            WalletClientFor(CitizenUserId, CitizenWallet));

        var row = PageOf(result).Items.Single();
        row.State.Should().Be("Completed");
        row.Outcome.Should().Be("NotApproved");
        row.DecisionReason.Should().Be("The document you provided could not be read clearly.");
    }

    [Fact]
    public async Task List_NeverReturnsTheInternalReasonCode()
    {
        var result = await List(
            ConsumerContext(CitizenUserId),
            StoreWith(new()
            {
                [CitizenWallet] =
                [
                    Application(state: InstanceState.Completed, current: [],
                        routeId: "route-refuse", reasonCode: "DOC_UNREADABLE"),
                ],
            }),
            Blueprint(),
            WalletClientFor(CitizenUserId, CitizenWallet));

        var json = System.Text.Json.JsonSerializer.Serialize(PageOf(result));
        json.Should().NotContain("DOC_UNREADABLE", "the code is internal classification, not citizen copy");
        json.Should().NotContain(AnalystWallet, "participant wallets de-anonymise the other party");
    }

    [Fact]
    public async Task List_BlueprintMissingFromThisNode_StillListsTheApplication()
    {
        var result = await List(
            ConsumerContext(CitizenUserId),
            StoreWith(new() { [CitizenWallet] = [Application()] }),
            blueprint: null,
            WalletClientFor(CitizenUserId, CitizenWallet));

        var row = PageOf(result).Items.Single();
        row.BlueprintTitle.Should().Be("bp-1", "degrade to the id rather than dropping the row");
        row.DecisionReason.Should().BeNull();
    }

    // ---------------------------------------------------------------------------------------
    // needsYou — the #1268 dissolution
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task List_JustSubmitted_OffersNoAction()
    {
        var result = await List(
            ConsumerContext(CitizenUserId),
            StoreWith(new() { [CitizenWallet] = [Application(current: [2])] }),
            Blueprint(),
            WalletClientFor(CitizenUserId, CitizenWallet));

        PageOf(result).Items.Single().NeedsYou.Should().BeFalse(
            "action 2 is the analyst's; offering the citizen a live action here is #1268");
    }

    [Fact]
    public async Task List_WaitingOnTheCitizen_OffersAnAction()
    {
        var result = await List(
            ConsumerContext(CitizenUserId),
            StoreWith(new() { [CitizenWallet] = [Application(current: [3])] }),
            Blueprint(),
            WalletClientFor(CitizenUserId, CitizenWallet));

        PageOf(result).Items.Single().NeedsYou.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------
    // Detail
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Detail_Participant_GetsTheApplicationAndItsSteps()
    {
        var application = Application();
        var result = await Detail(
            ConsumerContext(CitizenUserId), "inst-1",
            StoreWith([], application),
            Blueprint(),
            WalletClientFor(CitizenUserId, CitizenWallet));

        var detail = DetailOf(result);
        detail.Summary.InstanceId.Should().Be("inst-1");
        detail.Steps.Select(s => s.Status).Should().Equal("Completed", "Current", "Upcoming");
    }

    [Fact]
    public async Task Detail_NonParticipant_IsIndistinguishableFromNotFound()
    {
        // FR-021. If "not yours" and "no such thing" differ, the id space can be probed — and these
        // ids appear in URLs, logs and inbox detailHref values.
        var notYours = await Detail(
            ConsumerContext(StrangerUserId), "inst-1",
            StoreWith([], Application()),
            Blueprint(),
            WalletClientFor(StrangerUserId, StrangerWallet));

        var noSuchThing = await Detail(
            ConsumerContext(StrangerUserId), "inst-does-not-exist",
            StoreWith([]),
            Blueprint(),
            WalletClientFor(StrangerUserId, StrangerWallet));

        StatusOf(notYours).Should().Be(StatusCodes.Status404NotFound);
        StatusOf(noSuchThing).Should().Be(StatusOf(notYours));
    }
}
