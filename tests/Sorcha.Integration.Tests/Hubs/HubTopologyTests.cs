// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace Sorcha.Integration.Tests.Hubs;

/// <summary>
/// Topology-enforcement test for Feature 118 — asserts the canonical five-hub
/// surface (BlueprintHub, EventsHub, WalletHub, RegisterHub, TenantHub +
/// ChatHub as the documented FR-019 streaming exception) and that every
/// non-ChatHub uses <c>AddSorchaHub&lt;THub, TClient&gt;</c> via inheritance
/// from <c>Hub&lt;TClient&gt;</c> rather than the untyped base class.
/// </summary>
/// <remarks>
/// Lives next to <see cref="ThinSignalContractTests"/> so both reflection
/// surveys load the same service assemblies. Tightens automatically when
/// the legacy <c>EventsHub</c> retires in Phase 10 — drop its entry from
/// <see cref="ExpectedHubFqns"/>.
/// </remarks>
[Trait("Category", "HubTopology")]
public class HubTopologyTests
{
    private static readonly string[] ExpectedHubFqns =
    [
        "Sorcha.Blueprint.Service.Hubs.BlueprintHub",
        "Sorcha.Blueprint.Service.Hubs.EventsHub",
        "Sorcha.Blueprint.Service.Hubs.ChatHub",
        "Sorcha.Tenant.Service.Hubs.TenantHub",
        "Sorcha.Wallet.Service.Hubs.WalletHub",
        "Sorcha.Register.Service.Hubs.RegisterHub",
    ];

    private static readonly string ChatHubFqn = "Sorcha.Blueprint.Service.Hubs.ChatHub";

    [Fact]
    public void Topology_ContainsExactlyTheCanonicalHubs()
    {
        var hubFqns = LocateHubClasses().Select(h => h.FullName!).OrderBy(x => x).ToArray();
        var expected = ExpectedHubFqns.OrderBy(x => x).ToArray();

        hubFqns.Should().BeEquivalentTo(expected,
            "the Feature 118 canonical topology is the five hubs above plus ChatHub. " +
            "Drop EventsHub from this list when Phase 10 retirement lands.");
    }

    [Fact]
    public void EveryNonChatHub_InheritsHubGeneric()
    {
        // FR-006 — every notification hub MUST use the typed Hub<TClient> form so the
        // server-side emit surface is contract-checked. ChatHub is the streaming
        // exception (FR-019) and may use the untyped Hub base.
        var hubs = LocateHubClasses();
        var nonTypedNonChat = hubs
            .Where(h => h.FullName != ChatHubFqn)
            .Where(h => !InheritsHubGeneric(h))
            .Select(h => h.FullName)
            .ToArray();

        nonTypedNonChat.Should().BeEmpty(
            "every notification hub MUST inherit Hub<TClient>. ChatHub is the only exception.");
    }

    [Fact]
    public void ChatHub_IsTheOnlyHubAllowedToBypassTypedClient()
    {
        var chat = LocateHubClasses().Single(h => h.FullName == ChatHubFqn);
        InheritsHubGeneric(chat).Should().BeFalse(
            "ChatHub is the documented FR-019 streaming exception — if it gains a typed " +
            "client, this assertion can be flipped to assert the rest of the topology too.");
    }

    private static List<Type> LocateHubClasses()
    {
        // Discover hubs by enumerating types in each service's assembly. Using
        // Assembly.GetAssembly on a known type per service forces the assembly
        // load and keeps the discovery wide enough to catch a newly-added hub
        // class (which the canonical-list assertion will then flag).
        var serviceAssemblies = new[]
        {
            typeof(Sorcha.Blueprint.Service.Hubs.BlueprintHub).Assembly,
            typeof(Sorcha.Tenant.Service.Hubs.TenantHub).Assembly,
            typeof(Sorcha.Wallet.Service.Hubs.WalletHub).Assembly,
            typeof(Sorcha.Register.Service.Hubs.RegisterHub).Assembly,
        };

        return serviceAssemblies
            .SelectMany(SafeGetTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(InheritsAnyHubBase)
            .ToList();
    }

    private static bool InheritsAnyHubBase(Type t)
    {
        var current = t.BaseType;
        while (current is not null)
        {
            if (current == typeof(Hub)) return true;
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Hub<>)) return true;
            current = current.BaseType;
        }
        return false;
    }

    private static bool InheritsHubGeneric(Type t)
    {
        var current = t.BaseType;
        while (current is not null)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Hub<>)) return true;
            current = current.BaseType;
        }
        return false;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
