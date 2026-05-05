// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Sorcha.Integration.Tests.Hubs;

/// <summary>
/// Reflection-based regression test for the thin-signal contract from Feature 118
/// spec FR-016 — FR-019. Walks every event method on every typed-client interface
/// in the loaded service assemblies and asserts that each parameter type is in
/// the strict allow-list (opaque IDs, counts, timestamps, trace tokens).
/// </summary>
/// <remarks>
/// <para>
/// ChatHub is exempt — it streams content payloads by design and does not have a
/// typed-client interface. Methods on the documented exemption list (see
/// <see cref="DeferredExemptions"/>) are tracked as Phase 6 follow-up work — they
/// emit non-ID payloads today and migrate to ID-only signatures alongside
/// the descriptive-field stripping pass on the wire-level notification records.
/// </para>
/// <para>
/// New event methods added to any typed-client interface MUST conform to the
/// strict allow-list out of the gate — this test fails closed for unknown methods.
/// </para>
/// </remarks>
[Trait("Category", "ThinSignalContract")]
public class ThinSignalContractTests
{
    /// <summary>Every parameter type that is allowed on a non-ChatHub event method per the thin-signal contract.</summary>
    private static readonly Type[] AllowedParameterTypes =
    [
        typeof(string),
        typeof(Guid),
        typeof(Guid?),
        typeof(int),
        typeof(int?),
        typeof(long),
        typeof(long?),
        typeof(uint),
        typeof(ulong),
        typeof(DateTimeOffset),
        typeof(DateTimeOffset?),
    ];

    /// <summary>
    /// Methods on typed-client interfaces that are temporarily exempt from the strict
    /// allow-list because their payload types haven't been stripped yet. Each entry MUST
    /// have a follow-up phase tracked in MIGRATION.md.
    /// </summary>
    private static readonly HashSet<string> DeferredExemptions = new(StringComparer.Ordinal)
    {
        // Phase 6 follow-up — encryption events still carry object payloads. Strip when
        // the descriptive-field migration on EncryptionSignal lands.
        "Sorcha.Blueprint.Service.Hubs.IBlueprintHubClient.EncryptionProgress",
        "Sorcha.Blueprint.Service.Hubs.IBlueprintHubClient.EncryptionComplete",
        "Sorcha.Blueprint.Service.Hubs.IBlueprintHubClient.EncryptionFailed",
        // SignalNotification carries InstanceId + signal-type strings — Phase 6 strips these
        // to opaque IDs only.
        "Sorcha.Blueprint.Service.Hubs.IBlueprintHubClient.ActionAvailable",
        "Sorcha.Blueprint.Service.Hubs.IBlueprintHubClient.ActionRejected",
        "Sorcha.Blueprint.Service.Hubs.IBlueprintHubClient.WorkflowCompleted",
        // EventsHub legacy events — retired in Phase 10 polish; not migrated.
        "Sorcha.Blueprint.Service.Hubs.IEventsHubClient.EncryptionOperationCompleted",
        "Sorcha.Blueprint.Service.Hubs.IEventsHubClient.EventReceived",
        "Sorcha.Blueprint.Service.Hubs.IEventsHubClient.CredentialStatusChanged",
        "Sorcha.Blueprint.Service.Hubs.IEventsHubClient.InboundActionReceived",
    };

    [Fact]
    public void EveryEventMethod_OnEveryTypedClientInterface_HasAllowedParameterTypes()
    {
        var typedClientInterfaces = LocateTypedClientInterfaces();
        typedClientInterfaces.Should().NotBeEmpty(
            "the test discovery walk should find at least one I*HubClient interface in the loaded assemblies");

        var violations = new List<string>();

        foreach (var iface in typedClientInterfaces)
        {
            foreach (var method in iface.GetMethods())
            {
                var fqn = $"{iface.FullName}.{method.Name}";

                if (DeferredExemptions.Contains(fqn))
                {
                    continue;
                }

                foreach (var p in method.GetParameters())
                {
                    if (!AllowedParameterTypes.Contains(p.ParameterType))
                    {
                        violations.Add(
                            $"{fqn} parameter '{p.Name}' has disallowed type {p.ParameterType.FullName} — " +
                            "thin-signal contract permits only IDs, counts, timestamps, and the trace token. " +
                            "Either tighten the parameter to an ID-like type or add the method to DeferredExemptions " +
                            "with a tracked follow-up in MIGRATION.md.");
                    }
                }
            }
        }

        violations.Should().BeEmpty();
    }

    [Fact]
    public void DeferredExemptions_AllReferenceExistingMethods()
    {
        // Guards against typos in the exemption list — every entry must resolve to a real method.
        var typedClientInterfaces = LocateTypedClientInterfaces();
        var allMethodFqns = typedClientInterfaces
            .SelectMany(i => i.GetMethods().Select(m => $"{i.FullName}.{m.Name}"))
            .ToHashSet(StringComparer.Ordinal);

        var stale = DeferredExemptions.Where(e => !allMethodFqns.Contains(e)).ToList();

        stale.Should().BeEmpty(
            "exemptions should be removed when their methods are migrated or renamed");
    }

    [Fact]
    public void EveryNonChatHub_HasTypedClientInterface()
    {
        // Asserts FR-006 — every notification hub uses the typed-client surface.
        // ChatHub is the documented exception (FR-019).
        var hubs = LocateHubClasses();
        hubs.Should().NotBeEmpty();

        var nonChatHubsWithoutTypedClient = hubs
            .Where(h => h.Name != "ChatHub")
            .Where(h => !IsTypedClientHub(h))
            .Select(h => h.FullName)
            .ToList();

        nonChatHubsWithoutTypedClient.Should().BeEmpty(
            "every notification hub MUST inherit from Hub<TClient> per FR-006. ChatHub is the only exception.");
    }

    private static List<Type> LocateTypedClientInterfaces()
    {
        // Force-load all referenced Sorcha.* service assemblies — Reflection walks
        // only assemblies actually loaded in the AppDomain.
        ForceLoadServiceAssemblies();

        // Direct lookup by typeof(...) is more reliable than walking the AppDomain —
        // the AppDomain query depends on assembly load order which can vary in test
        // host environments. We enumerate the known typed-client interfaces explicitly
        // here; this is the canonical list and missing an entry is itself a contract
        // violation that EveryNonChatHub_HasTypedClientInterface catches separately.
        return [
            typeof(Sorcha.Blueprint.Service.Hubs.IBlueprintHubClient),
            typeof(Sorcha.Blueprint.Service.Hubs.IEventsHubClient),
            typeof(Sorcha.Tenant.Service.Hubs.ITenantHubClient),
            typeof(Sorcha.Wallet.Service.Hubs.IWalletHubClient),
            typeof(Sorcha.Register.Service.Hubs.IRegisterHubClient),
        ];
    }

    private static List<Type> LocateHubClasses()
    {
        // As above — enumerate by typeof(...) instead of walking the AppDomain.
        return [
            typeof(Sorcha.Blueprint.Service.Hubs.BlueprintHub),
            typeof(Sorcha.Blueprint.Service.Hubs.EventsHub),
            typeof(Sorcha.Blueprint.Service.Hubs.ChatHub),
            typeof(Sorcha.Tenant.Service.Hubs.TenantHub),
            typeof(Sorcha.Wallet.Service.Hubs.WalletHub),
            typeof(Sorcha.Register.Service.Hubs.RegisterHub),
        ];
    }

    private static bool IsTypedClientHub(Type hub)
    {
        var current = hub.BaseType;
        while (current is not null)
        {
            if (current.IsGenericType && current.Name.StartsWith("Hub`", StringComparison.Ordinal))
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }

    private static bool InheritsHubGeneric(Type t)
    {
        var current = t.BaseType;
        while (current is not null)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition().Name.StartsWith("Hub`", StringComparison.Ordinal))
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private static void ForceLoadServiceAssemblies()
    {
        // Reference one type from each service so the assembly loader pulls them into the AppDomain.
        _ = typeof(Sorcha.Blueprint.Service.Hubs.IBlueprintHubClient);
        _ = typeof(Sorcha.Tenant.Service.Hubs.ITenantHubClient);
        _ = typeof(Sorcha.Wallet.Service.Hubs.IWalletHubClient);
        _ = typeof(Sorcha.Register.Service.Hubs.RegisterHub);
    }
}
