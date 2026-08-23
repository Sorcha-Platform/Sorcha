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
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Wallet;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// Feature 194 — <c>GET /api/instances/{id}/definition</c>: which definition is this instance
/// running, and is it still the latest?
/// </summary>
/// <remarks>
/// The defect this feature fixes was invisible from outside — a republish silently moved every
/// in-flight instance onto new rules, and no surface showed it. A pin that is correct but
/// unreportable would leave the next investigation exactly as blind, which is why the read surface
/// is part of the feature.
/// <para>
/// The load-bearing assertions here are the THREE DISTINGUISHABLE STATES. Collapsing
/// "pinned but unresolvable" into "unpinned" — or, worse, substituting the latest definition's
/// version label when the pin cannot be resolved — would hide the one failure mode Story 3 exists
/// to surface: an instance that cannot advance on this node.
/// </para>
/// </remarks>
public sealed class InstancePinReadTests
{
    private const string CitizenWallet = "ws1qcitizen000000000000000000000000000000";
    private const string PinV1 = "aaaa000000000000000000000000000000000000000000000000000000000001";
    private const string PinV2 = "bbbb000000000000000000000000000000000000000000000000000000000002";

    [Fact]
    public async Task PinnedToTheLatestDefinition_ReportsTheHash_TheVersion_AndIsPinnedToLatestTrue()
    {
        var result = await GetDefinition(
            InstanceWith(PinV1),
            published: [Published(PinV1, version: 1)]);

        var body = Body(result);
        Value<string>(body, "pinState").Should().Be("pinned");
        Value<string>(body, "blueprintExecDefHash").Should().Be(PinV1);
        Value<int?>(body, "blueprintVersion").Should().Be(1);
        Value<bool?>(body, "isPinnedToLatest").Should().BeTrue();
    }

    [Fact]
    public async Task PinnedToASupersededDefinition_ReportsIsPinnedToLatestFalse_AndItsOwnVersion()
    {
        // The state an operator is actually trying to recognise: this application is on the older
        // rules BY DESIGN, and is not broken.
        var result = await GetDefinition(
            InstanceWith(PinV1),
            published: [Published(PinV1, version: 1), Published(PinV2, version: 2)]);

        var body = Body(result);
        Value<string>(body, "pinState").Should().Be("pinned");
        Value<int?>(body, "blueprintVersion").Should().Be(1,
            "the version label is derived FROM THE PIN, so the two can never disagree");
        Value<bool?>(body, "isPinnedToLatest").Should().BeFalse();
    }

    [Fact]
    public async Task PinnedButUnresolvable_ReportsTheHash_AndRefusesToGuessAVersion()
    {
        // The stuck-instance state. A version label here would be a guess, and a plausible guess is
        // worse than an absent answer — it would read as healthy.
        var result = await GetDefinition(
            InstanceWith(PinV1),
            published: [Published(PinV2, version: 2)]);

        var body = Body(result);
        Value<string>(body, "pinState").Should().Be("unresolvable");
        Value<string>(body, "blueprintExecDefHash").Should().Be(PinV1);
        Value<int?>(body, "blueprintVersion").Should().BeNull();
        Value<bool?>(body, "isPinnedToLatest").Should().BeNull();
    }

    [Fact]
    public async Task AnUnpinnedInstance_IsDistinguishableFromAnUnresolvableOne()
    {
        // Both lack a version label; only one of them is a problem. Reporting them identically
        // would make a pre-Feature-194 instance indistinguishable from a stuck one.
        var result = await GetDefinition(
            InstanceWith(execDefHash: string.Empty),
            published: [Published(PinV1, version: 1)]);

        var body = Body(result);
        Value<string>(body, "pinState").Should().Be("unpinned");
        Value<string>(body, "blueprintExecDefHash").Should().BeNull();
        Value<int?>(body, "blueprintVersion").Should().BeNull();
        Value<bool?>(body, "isPinnedToLatest").Should().BeNull();
    }

    [Fact]
    public async Task AMissingInstance_Is404_NotAnEmptyPinReport()
    {
        var store = new Mock<IInstanceStore>();
        store.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Instance?)null);

        var result = await Invoke(
            Context(), "no-such-instance", store.Object,
            BlueprintStore(), PublishedStore([]), Wallets());

        result.GetType().Name.Should().StartWith("NotFound",
            "a missing instance is a 404, not a pin report with everything null");
    }

    // ---------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------

    private static Instance InstanceWith(string execDefHash) => new()
    {
        Id = "inst-1",
        BlueprintId = "bp-1",
        BlueprintVersion = 1,
        BlueprintExecDefHash = execDefHash,
        RegisterId = "reg-1",
        TenantId = "tenant-1",
        State = InstanceState.Active,
        CurrentActionIds = [2],
        ParticipantWallets = new Dictionary<string, string> { ["citizen"] = CitizenWallet },
        CompletedActionCount = 1,
    };

    private static PublishedBlueprint Published(string execDefHash, int version) => new()
    {
        BlueprintId = "bp-1",
        ExecDefHash = execDefHash,
        Version = version,
        RegisterId = "reg-1",
        Blueprint = new BlueprintModel { Id = "bp-1", Title = "T", Description = "Desc." },
        PublishedAt = DateTimeOffset.UtcNow.AddMinutes(-version),
    };

    private static IPublishedBlueprintStore PublishedStore(IReadOnlyList<PublishedBlueprint> published)
    {
        var mock = new Mock<IPublishedBlueprintStore>();
        mock.Setup(s => s.GetVersionsAsync("bp-1")).ReturnsAsync(published);
        mock.Setup(s => s.GetByExecDefHashAsync("bp-1", It.IsAny<string>()))
            .ReturnsAsync((string _, string hash) =>
                published.FirstOrDefault(p => p.ExecDefHash == hash));
        return mock.Object;
    }

    private static IBlueprintStore BlueprintStore()
    {
        var mock = new Mock<IBlueprintStore>();
        mock.Setup(s => s.GetAsync(It.IsAny<string>()))
            .ReturnsAsync(new BlueprintModel { Id = "bp-1", Title = "T", Description = "Desc." });
        return mock.Object;
    }

    private static IWalletServiceClient Wallets() => new Mock<IWalletServiceClient>().Object;

    /// <summary>A caller controlling the instance's bound participant wallet, so the gate passes.</summary>
    private static HttpContext Context()
    {
        var identity = new ClaimsIdentity(
            [new Claim("wallet_address", CitizenWallet)], authenticationType: "Test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private static Task<IResult> GetDefinition(Instance instance, IReadOnlyList<PublishedBlueprint> published)
    {
        var store = new Mock<IInstanceStore>();
        store.Setup(s => s.GetAsync(instance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        return Invoke(Context(), instance.Id, store.Object, BlueprintStore(), PublishedStore(published), Wallets());
    }

    private static Task<IResult> Invoke(
        HttpContext ctx, string instanceId, IInstanceStore instances,
        IBlueprintStore blueprints, IPublishedBlueprintStore published, IWalletServiceClient wallets)
    {
        var method = typeof(InstanceReadEndpoints).GetMethod(
            nameof(InstanceReadEndpoints.GetInstanceDefinition),
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("GetInstanceDefinition should be reachable for reflection-based testing");

        return (Task<IResult>)method!.Invoke(null,
        [
            ctx, instanceId, instances, blueprints, published, wallets,
            NullLogger<InstanceReadEndpoints.InstanceReadEndpointsLogCategory>.Instance,
            CancellationToken.None
        ])!;
    }

    /// <summary>
    /// Reads the anonymous body off the IResult. Asserting on the <c>IResult</c> itself would pass
    /// vacuously — System.Text.Json serialises the DECLARED type, so the carried value never appears
    /// (the trap that let a wide-open endpoint's own leak test go green in #1182).
    /// </summary>
    private static object Body(IResult result)
    {
        var value = (result as Microsoft.AspNetCore.Http.IValueHttpResult)?.Value;
        value.Should().NotBeNull("the handler must return a value result, not a bare status");
        return value!;
    }

    private static T? Value<T>(object body, string propertyName)
    {
        var prop = body.GetType().GetProperty(propertyName);
        prop.Should().NotBeNull($"the response should carry '{propertyName}'");
        return (T?)prop!.GetValue(body);
    }
}
