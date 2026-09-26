// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using FluentAssertions;

using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Models.Tests;

/// <summary>
/// The currency gate for <c>src/Common/blueprint.schema.json</c> (issue #1609). Reflects every type
/// reachable from <see cref="Blueprint"/> via a real (typed) serialized property, and asserts every
/// wire property name it finds has a matching entry in the schema's corresponding <c>$defs</c> object
/// (or the schema root, for <see cref="Blueprint"/> itself).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why reflection, not a hand-maintained list.</b> The defect this gate exists to catch is pure
/// omission — four months of the schema silently falling behind the models with nothing red. A
/// hand-maintained "properties we've checked" list decays exactly the same way a hand-maintained
/// schema does. Reflecting the live model type graph means a newly added serialized property is
/// discovered automatically the next time this test runs, with no second place to remember to update.
/// </para>
/// <para>
/// <b>Traversal stops at opaque leaves.</b> <c>Action.DataSchemas</c> (and friends) are
/// <c>IEnumerable&lt;JsonDocument&gt;</c> — arbitrary embedded JSON Schema documents, not typed C#
/// objects — so the traversal does not and cannot follow into them. That is also why
/// <c>BlueprintPageDefinition</c>/<c>XReviewExtension</c>/<c>FileSchemaExtension</c>/
/// <c>HolderKeySchemaExtension</c> etc. never appear in <see cref="PinnedTypeToDefPath"/>: nothing in
/// the typed Blueprint graph references them — they parse the CONTENTS of a <c>dataSchemas</c> item,
/// which is opaque from here. The action <c>$def</c>'s <c>dataSchemas</c> property description
/// documents that vocabulary in prose instead (CLAUDE.md §22 also forbids constraining it further:
/// the shape of an action's own JSON Schema document is author content, not ledger contract).
/// </para>
/// <para>
/// <b>The mapping is pinned, not derived.</b> <see cref="PinnedTypeToDefPath"/> is asserted (in
/// <see cref="Discovery_ReachesExactlyThePinnedTypeSet"/>) to equal exactly the set of types the
/// traversal discovers — not a superset, not a subset. A newly reachable type with no entry fails
/// loudly instead of being silently skipped, and a stale entry for a type that stopped being
/// reachable fails too. This is what stops the gate from passing vacuously.
/// </para>
/// </remarks>
public sealed class BlueprintSchemaCurrencyTests
{
    /// <summary>Sentinel <c>$defs</c> path meaning "the schema's own top-level <c>properties</c>".</summary>
    private const string RootDefPath = "$root";

    /// <summary>
    /// The ONE place that says which schema location documents which model type. Every type the
    /// reflective traversal discovers from <see cref="Blueprint"/> MUST appear here — asserted
    /// separately from the per-property check, so an unmapped type fails with its own clear message
    /// rather than a confusing "property not found" on the wrong def.
    /// </summary>
    private static readonly Dictionary<Type, string> PinnedTypeToDefPath = new()
    {
        [typeof(Blueprint)] = RootDefPath,
        [typeof(Action)] = "action",
        [typeof(Participant)] = "participant",
        [typeof(Route)] = "route",
        [typeof(DecisionNotice)] = "decisionNotice",
        [typeof(Disclosure)] = "disclosure",
        [typeof(Condition)] = "condition",
        [typeof(Control)] = "control",
        [typeof(FormRule)] = "formRule",
        [typeof(SchemaBasedCondition)] = "schemaBasedCondition",
        [typeof(RejectionConfig)] = "rejectionConfig",
        [typeof(InstanceReferenceTemplate)] = "instanceReferenceTemplate",
        [typeof(ReferenceComponent)] = "referenceComponent",
        [typeof(BlueprintInstructions)] = "blueprintInstructions",
        [typeof(InstructionSet)] = "instructionSet",
        [typeof(NotificationConfig)] = "notificationConfig",
        [typeof(BlueprintPresentationConfig)] = "presentationConfig",
        [typeof(CredentialRequirement)] = "credentialRequirement",
        [typeof(ClaimConstraint)] = "claimConstraint",
        [typeof(TrustPolicy)] = "trustPolicy",
        [typeof(TrustSourceRef)] = "trustSourceRef",
        [typeof(CredentialIssuanceConfig)] = "credentialIssuanceConfig",
        [typeof(ClaimMapping)] = "claimMapping",
        [typeof(CredentialDisplayConfig)] = "credentialDisplayConfig",
    };

    [Fact]
    public void Discovery_ReachesExactlyThePinnedTypeSet()
    {
        var discovered = DiscoverReachableTypes();

        // Not a superset check and not a subset check — an exact match, so a type falling out of
        // reach (stale pin) is caught exactly as loudly as a new type falling into it (missing pin).
        discovered.Should().BeEquivalentTo(PinnedTypeToDefPath.Keys,
            "every type reachable from Blueprint via a typed property must have exactly one pinned " +
            "$defs mapping in BlueprintSchemaCurrencyTests.PinnedTypeToDefPath — add or remove the " +
            "entry there when this fails");

        // Guards against the gate passing vacuously on a traversal that (by a bug) discovers nothing.
        discovered.Should().HaveCountGreaterThanOrEqualTo(15,
            "the reachable type graph is large — a small count means the traversal is broken, not " +
            "that the model shrank");
    }

    [Fact]
    public void EveryReachableSerializedProperty_HasAMatchingSchemaEntry()
    {
        using var schemaDoc = JsonDocument.Parse(ReadSchemaFile());
        var schemaRoot = schemaDoc.RootElement;

        var pairs = CollectSerializedProperties();
        pairs.Should().HaveCountGreaterThanOrEqualTo(60,
            "a small count means the traversal or the property collector is broken, not that the " +
            "model shrank");

        var failures = new List<string>();

        foreach (var (owner, wireName) in pairs)
        {
            if (!PinnedTypeToDefPath.TryGetValue(owner, out var defPath))
            {
                failures.Add($"{owner.FullName} has no pinned $defs mapping (see Discovery test)");
                continue;
            }

            if (!TryGetSchemaProperties(schemaRoot, defPath, out var schemaProperties))
            {
                failures.Add(
                    $"{owner.Name}.{wireName}: schema has no '{defPath}' definition with a " +
                    "'properties' object — edit src/Common/blueprint.schema.json");
                continue;
            }

            if (!schemaProperties.TryGetProperty(wireName, out _))
            {
                var location = defPath == RootDefPath
                    ? "the schema's top-level 'properties'"
                    : $"$defs.{defPath}.properties";

                failures.Add(
                    $"{owner.Name}.{wireName}: serialized property '{wireName}' is not described in " +
                    $"{location} — edit src/Common/blueprint.schema.json");
            }
        }

        failures.Should().BeEmpty(
            "every serialized blueprint-graph property must be described in blueprint.schema.json:\n" +
            string.Join('\n', failures));
    }

    private static bool TryGetSchemaProperties(
        JsonElement schemaRoot, string defPath, out JsonElement properties)
    {
        properties = default;

        JsonElement container;
        if (defPath == RootDefPath)
        {
            container = schemaRoot;
        }
        else
        {
            if (!schemaRoot.TryGetProperty("$defs", out var defs) ||
                !defs.TryGetProperty(defPath, out container))
            {
                return false;
            }
        }

        return container.TryGetProperty("properties", out properties);
    }

    private static string ReadSchemaFile()
    {
        var path = Path.Combine(RepoRoot(), "src", "Common", "blueprint.schema.json");
        File.Exists(path).Should().BeTrue("the schema this gate checks against must exist on disk");
        return File.ReadAllText(path);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sorcha.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the tests must be able to locate the repo root");
        return dir!.FullName;
    }

    /// <summary>Breadth-first walk of every type reachable from <see cref="Blueprint"/>.</summary>
    private static HashSet<Type> DiscoverReachableTypes()
    {
        var visited = new HashSet<Type> { typeof(Blueprint) };
        var queue = new Queue<Type>();
        queue.Enqueue(typeof(Blueprint));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var property in SerializedProperties(current))
            {
                var resolved = ResolveComplexType(property.PropertyType);
                if (IsOwnModelType(resolved) && visited.Add(resolved))
                {
                    queue.Enqueue(resolved);
                }
            }
        }

        return visited;
    }

    /// <summary>Every (owner type, wire property name) pair reachable from <see cref="Blueprint"/>.</summary>
    private static List<(Type Owner, string WireName)> CollectSerializedProperties()
    {
        var pairs = new List<(Type Owner, string WireName)>();
        var visited = new HashSet<Type> { typeof(Blueprint) };
        var queue = new Queue<Type>();
        queue.Enqueue(typeof(Blueprint));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var property in SerializedProperties(current))
            {
                pairs.Add((current, WireName(property)));

                var resolved = ResolveComplexType(property.PropertyType);
                if (IsOwnModelType(resolved) && visited.Add(resolved))
                {
                    queue.Enqueue(resolved);
                }
            }
        }

        return pairs;
    }

    private static IEnumerable<PropertyInfo> SerializedProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always });

    private static string WireName(PropertyInfo property)
    {
        var explicitName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
        if (!string.IsNullOrEmpty(explicitName))
        {
            return explicitName;
        }

        // Matches the camelCase naming policy the Blueprint Service's wire form uses (SorchaJson /
        // System.Text.Json Web defaults) for the handful of properties with no explicit
        // [JsonPropertyName] — currently only BlueprintPresentationConfig's positional record
        // parameters.
        var name = property.Name;
        return string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>
    /// True when <paramref name="type"/> is a non-enum class declared in the blueprint models
    /// assembly's own namespace tree (<c>Sorcha.Blueprint.Models</c> and
    /// <c>Sorcha.Blueprint.Models.Credentials</c>) — i.e. a type this gate should recurse into and
    /// pin a $defs mapping for, as opposed to a leaf (string, primitive, enum, JsonDocument, JsonNode,
    /// JsonElement, or anything from outside this model tree).
    /// </summary>
    private static bool IsOwnModelType(Type type) =>
        type is { IsEnum: false, IsClass: true } &&
        type.Namespace is { } ns &&
        (ns == "Sorcha.Blueprint.Models" || ns == "Sorcha.Blueprint.Models.Credentials");

    /// <summary>
    /// Unwraps <c>Nullable&lt;T&gt;</c>, dictionary value types, and enumerable element types (in
    /// that precedence order, since a dictionary is also enumerable) to the type whose own properties
    /// (if any) would actually appear on the wire.
    /// </summary>
    private static Type ResolveComplexType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        var dictionaryValueType = TryGetDictionaryValueType(underlying);
        if (dictionaryValueType is not null)
        {
            return ResolveComplexType(dictionaryValueType);
        }

        var elementType = TryGetEnumerableElementType(underlying);
        if (elementType is not null)
        {
            return ResolveComplexType(elementType);
        }

        return underlying;
    }

    private static Type? TryGetDictionaryValueType(Type type)
    {
        foreach (var candidate in CandidateInterfaces(type))
        {
            if (candidate.IsGenericType &&
                (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                 candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))
            {
                return candidate.GetGenericArguments()[1];
            }
        }

        return null;
    }

    private static Type? TryGetEnumerableElementType(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        foreach (var candidate in CandidateInterfaces(type))
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return candidate.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static IEnumerable<Type> CandidateInterfaces(Type type)
    {
        if (type.IsInterface)
        {
            yield return type;
        }

        foreach (var iface in type.GetInterfaces())
        {
            yield return iface;
        }
    }
}
