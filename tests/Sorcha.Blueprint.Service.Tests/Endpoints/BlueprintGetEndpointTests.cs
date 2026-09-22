// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;
using Sorcha.ServiceClients.Wallet;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// #1683 — <c>GET /api/blueprints/{id}</c> must not 404 a blueprint that plainly exists just because
/// the caller is not a member of the organisation that owns the DRAFT. When the org-scoped draft
/// does not resolve, the endpoint must fall back to a PUBLISHED definition on a register the caller
/// participates in (resolved by wallet, like the rest of the participant-gated surface), and only
/// 404 when nothing resolves at all. A published definition that the caller cannot reach because
/// they hold no participating wallet is a 403 (a refusal), not a 404 (a false absence) — the #1673
/// class this issue names explicitly.
/// </summary>
public sealed class BlueprintGetEndpointTests
{
    private const string BlueprintId = "bp-1";
    private const string OwningOrgId = "org-a";
    private const string CounterpartyWallet = "ws1qcounterparty0000000000000000000000000";
    private const string RegisterA = "reg-a";
    private const string RegisterB = "reg-b";

    private static BlueprintModel MakeBlueprint(string id, string title) =>
        new() { Id = id, Title = title, Description = "desc", OrganizationId = OwningOrgId };

    private static PublishedBlueprint MakePublished(string registerId, int version, BlueprintModel blueprint) =>
        new()
        {
            BlueprintId = blueprint.Id,
            Version = version,
            Blueprint = blueprint,
            PublicationTxId = $"tx-{registerId}-{version}",
            ExecDefHash = $"hash-{registerId}-{version}",
            RegisterId = registerId,
        };

    /// <summary>Platform-tier caller carrying the fast-path <c>wallet_address</c> claim.</summary>
    private static HttpContext ContextWithWallet(string walletAddress, string orgId = "org-b")
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("wallet_address", walletAddress),
            new Claim("org_id", orgId),
        ], "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    /// <summary>A caller for whom no wallet can be resolved at all (Wallet Service reachable but empty).</summary>
    private static HttpContext ContextWithNoResolvableWallet(string orgId = "org-b")
    {
        var identity = new ClaimsIdentity([new Claim("org_id", orgId)], "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private static Mock<IWalletServiceClient> WalletClientReturningNothing()
    {
        var mock = new Mock<IWalletServiceClient>();
        mock.Setup(c => c.GetWalletsByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalletInfo>());
        return mock;
    }

    private static PublishedParticipantRecord ActiveParticipant() => new()
    {
        ParticipantId = "p1",
        OrganizationName = "Org B",
        ParticipantName = "Counterparty",
        Status = "active",
        Version = 1,
        LatestTxId = "tx-1",
        Addresses = [new ParticipantAddressInfo { WalletAddress = CounterpartyWallet, PublicKey = "pk", Algorithm = "ED25519", Primary = true }],
    };

    private static PublishedParticipantRecord RevokedParticipant() => new()
    {
        ParticipantId = "p1",
        OrganizationName = "Org B",
        ParticipantName = "Counterparty",
        Status = "revoked",
        Version = 2,
        LatestTxId = "tx-2",
        Addresses = [new ParticipantAddressInfo { WalletAddress = CounterpartyWallet, PublicKey = "pk", Algorithm = "ED25519", Primary = true }],
    };

    private static Task<IResult> InvokeAsync(
        HttpContext httpContext,
        string id,
        IBlueprintService service,
        IPublishedBlueprintStore publishedStore,
        IWalletServiceClient walletClient,
        IRegisterServiceClient registerClient)
    {
        var method = typeof(BlueprintGetEndpoint).GetMethod(
            "GetBlueprintById",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("GetBlueprintById should be reachable for reflection-based endpoint testing");

        var result = method!.Invoke(null, [
            httpContext, id, service, publishedStore, walletClient, registerClient,
            NullLogger<BlueprintGetEndpoint.BlueprintGetEndpointLogCategory>.Instance,
            CancellationToken.None,
        ]);
        return (Task<IResult>)result!;
    }

    [Fact]
    public async Task GetBlueprintById_DraftOwnedByCallerOrg_ReturnsOkFromDraft_AndNeverConsultsPublishedStore()
    {
        var blueprint = MakeBlueprint(BlueprintId, "Owner's draft");
        var blueprintService = new Mock<IBlueprintService>();
        blueprintService.Setup(s => s.GetByIdAsync(BlueprintId, OwningOrgId)).ReturnsAsync(blueprint);

        var publishedStore = new Mock<IPublishedBlueprintStore>();
        var walletClient = WalletClientReturningNothing();
        var registerClient = new Mock<IRegisterServiceClient>();

        var result = await InvokeAsync(
            ContextWithWallet(CounterpartyWallet, orgId: OwningOrgId), BlueprintId,
            blueprintService.Object, publishedStore.Object, walletClient.Object, registerClient.Object);

        var ok = result.Should().BeOfType<Ok<BlueprintModel>>().Subject;
        ok.Value!.Title.Should().Be("Owner's draft");

        // The regression this test also guards: the org-owner path must be resolved WITHOUT ever
        // touching the published fallback plumbing.
        publishedStore.Verify(s => s.GetVersionsAsync(It.IsAny<string>()), Times.Never);
        registerClient.Verify(c => c.GetPublishedParticipantByAddressAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetBlueprintById_NoDraftAndNoPublishedVersion_ReturnsNotFound()
    {
        var blueprintService = new Mock<IBlueprintService>();
        blueprintService.Setup(s => s.GetByIdAsync(BlueprintId, "org-b")).ReturnsAsync((BlueprintModel?)null);

        var publishedStore = new Mock<IPublishedBlueprintStore>();
        publishedStore.Setup(s => s.GetVersionsAsync(BlueprintId)).ReturnsAsync(Array.Empty<PublishedBlueprint>());

        var walletClient = WalletClientReturningNothing();
        var registerClient = new Mock<IRegisterServiceClient>();

        var result = await InvokeAsync(
            ContextWithWallet(CounterpartyWallet), BlueprintId,
            blueprintService.Object, publishedStore.Object, walletClient.Object, registerClient.Object);

        result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task GetBlueprintById_CounterpartyIsRegisterParticipant_FallsBackToPublishedDefinition()
    {
        // Reproduces #1683 exactly: org B's draft lookup misses (blueprint belongs to org A), but
        // org B holds a wallet that is an active participant on the register the definition was
        // published to. The published definition must be served, not a 404.
        var blueprintService = new Mock<IBlueprintService>();
        blueprintService.Setup(s => s.GetByIdAsync(BlueprintId, "org-b")).ReturnsAsync((BlueprintModel?)null);

        var published = MakePublished(RegisterA, version: 1, MakeBlueprint(BlueprintId, "Published v1"));
        var publishedStore = new Mock<IPublishedBlueprintStore>();
        publishedStore.Setup(s => s.GetVersionsAsync(BlueprintId)).ReturnsAsync([published]);

        var walletClient = WalletClientReturningNothing(); // caller uses the wallet_address fast path, not the fallback

        var registerClient = new Mock<IRegisterServiceClient>();
        registerClient.Setup(c => c.GetPublishedParticipantByAddressAsync(RegisterA, CounterpartyWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveParticipant());

        var result = await InvokeAsync(
            ContextWithWallet(CounterpartyWallet), BlueprintId,
            blueprintService.Object, publishedStore.Object, walletClient.Object, registerClient.Object);

        var ok = result.Should().BeOfType<Ok<BlueprintModel>>().Subject;
        ok.Value!.Title.Should().Be("Published v1");
    }

    [Fact]
    public async Task GetBlueprintById_PublishedExistsButCallerNotAParticipant_ReturnsForbidden_NotNotFound()
    {
        var blueprintService = new Mock<IBlueprintService>();
        blueprintService.Setup(s => s.GetByIdAsync(BlueprintId, "org-b")).ReturnsAsync((BlueprintModel?)null);

        var published = MakePublished(RegisterA, version: 1, MakeBlueprint(BlueprintId, "Published v1"));
        var publishedStore = new Mock<IPublishedBlueprintStore>();
        publishedStore.Setup(s => s.GetVersionsAsync(BlueprintId)).ReturnsAsync([published]);

        var walletClient = WalletClientReturningNothing();

        var registerClient = new Mock<IRegisterServiceClient>();
        registerClient.Setup(c => c.GetPublishedParticipantByAddressAsync(RegisterA, CounterpartyWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublishedParticipantRecord?)null);

        var result = await InvokeAsync(
            ContextWithWallet(CounterpartyWallet), BlueprintId,
            blueprintService.Object, publishedStore.Object, walletClient.Object, registerClient.Object);

        // Specifically NOT NotFound — a scoping refusal must not be presented as an absence (#1673).
        result.Should().NotBeOfType<NotFound>();
        var problem = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task GetBlueprintById_RevokedParticipant_IsTreatedAsNotAParticipant_ReturnsForbidden()
    {
        var blueprintService = new Mock<IBlueprintService>();
        blueprintService.Setup(s => s.GetByIdAsync(BlueprintId, "org-b")).ReturnsAsync((BlueprintModel?)null);

        var published = MakePublished(RegisterA, version: 1, MakeBlueprint(BlueprintId, "Published v1"));
        var publishedStore = new Mock<IPublishedBlueprintStore>();
        publishedStore.Setup(s => s.GetVersionsAsync(BlueprintId)).ReturnsAsync([published]);

        var walletClient = WalletClientReturningNothing();

        var registerClient = new Mock<IRegisterServiceClient>();
        registerClient.Setup(c => c.GetPublishedParticipantByAddressAsync(RegisterA, CounterpartyWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RevokedParticipant());

        var result = await InvokeAsync(
            ContextWithWallet(CounterpartyWallet), BlueprintId,
            blueprintService.Object, publishedStore.Object, walletClient.Object, registerClient.Object);

        var problem = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task GetBlueprintById_MultipleRegisters_ServesHighestVersionCallerParticipatesOn()
    {
        var blueprintService = new Mock<IBlueprintService>();
        blueprintService.Setup(s => s.GetByIdAsync(BlueprintId, "org-b")).ReturnsAsync((BlueprintModel?)null);

        var publishedOld = MakePublished(RegisterA, version: 1, MakeBlueprint(BlueprintId, "Old on A"));
        var publishedNew = MakePublished(RegisterB, version: 2, MakeBlueprint(BlueprintId, "New on B"));
        var publishedStore = new Mock<IPublishedBlueprintStore>();
        publishedStore.Setup(s => s.GetVersionsAsync(BlueprintId)).ReturnsAsync([publishedOld, publishedNew]);

        var walletClient = WalletClientReturningNothing();

        var registerClient = new Mock<IRegisterServiceClient>();
        registerClient.Setup(c => c.GetPublishedParticipantByAddressAsync(RegisterA, CounterpartyWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveParticipant());
        registerClient.Setup(c => c.GetPublishedParticipantByAddressAsync(RegisterB, CounterpartyWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveParticipant());

        var result = await InvokeAsync(
            ContextWithWallet(CounterpartyWallet), BlueprintId,
            blueprintService.Object, publishedStore.Object, walletClient.Object, registerClient.Object);

        var ok = result.Should().BeOfType<Ok<BlueprintModel>>().Subject;
        ok.Value!.Title.Should().Be("New on B");
    }

    [Fact]
    public async Task GetBlueprintById_NoResolvableWalletAtAll_ReturnsForbidden_NotNotFound()
    {
        // A caller who resolves to zero wallets cannot be a participant on anything — must still be
        // told "not permitted", not "doesn't exist", when a publication genuinely exists.
        var blueprintService = new Mock<IBlueprintService>();
        blueprintService.Setup(s => s.GetByIdAsync(BlueprintId, "org-b")).ReturnsAsync((BlueprintModel?)null);

        var published = MakePublished(RegisterA, version: 1, MakeBlueprint(BlueprintId, "Published v1"));
        var publishedStore = new Mock<IPublishedBlueprintStore>();
        publishedStore.Setup(s => s.GetVersionsAsync(BlueprintId)).ReturnsAsync([published]);

        var walletClient = WalletClientReturningNothing();
        var registerClient = new Mock<IRegisterServiceClient>();

        var result = await InvokeAsync(
            ContextWithNoResolvableWallet(), BlueprintId,
            blueprintService.Object, publishedStore.Object, walletClient.Object, registerClient.Object);

        var problem = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        registerClient.Verify(c => c.GetPublishedParticipantByAddressAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
