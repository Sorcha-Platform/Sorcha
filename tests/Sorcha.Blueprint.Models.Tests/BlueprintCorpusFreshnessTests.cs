// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentAssertions;
using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Models.Tests;

/// <summary>
/// Keeps the shipped blueprint corpus from silently rotting against the models it is written for.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="JsonSerializer"/> ignores unknown properties by default. So when a model property is
/// renamed or removed, every blueprint JSON still carrying the old spelling keeps parsing — the
/// value is just dropped on the floor. There is no error, no warning, and the blueprint quietly
/// does less than it says it does. This is the same "nothing verifies the join" shape as the
/// dropped ClaimsFetchToken (#1327) and the T032 Redis fields (#1314).
/// </para>
/// <para>
/// JSON-Schema regions (<c>dataSchemas</c>, <c>form</c>, <c>calculations</c>, <c>condition</c>,
/// <c>previousData</c>, <c>metadata</c>, <c>additionalProperties</c>) are free-form by design and
/// are not descended into.
/// </para>
/// </remarks>
public class BlueprintCorpusFreshnessTests
{
    /// <summary>Directories holding blueprints that ship with the platform.</summary>
    private static readonly string[] SearchDirs = ["demos", "walkthroughs", "blueprints"];

    /// <summary>
    /// Properties whose values are arbitrary JSON (schemas, JsonLogic, UI layout, free-form bags).
    /// Their keys are data, not model members, so they are never checked.
    /// </summary>
    private static readonly HashSet<string> FreeFormKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "dataSchemas", "dataSchema", "schema", "form", "uiSchema", "calculations", "condition",
        "previousData", "metadata", "additionalProperties", "properties", "definitions",
        "$defs", "options", "config", "context", "@context", "payload", "data", "actionData",
        "claimMappings", "display", "branding", "rules", "checks"
    };

    /// <summary>
    /// JSON-e operators. A <see cref="BlueprintTemplate.Template"/> is rendered by the template
    /// engine before it is ever deserialised as a <see cref="Blueprint"/>, so these are legal
    /// anywhere inside one and are not model members.
    /// </summary>
    private static readonly HashSet<string> JsonEOperators = new(StringComparer.Ordinal)
    {
        "$eval", "$if", "then", "else", "$flatten", "$flattenDeep", "$map", "$let", "$merge",
        "$mergeDeep", "$switch", "$json", "$sort", "$reverse", "$fromNow", "each"
    };

    /// <summary>
    /// Keys that document rather than configure. JSON has no comments, and an author writing a
    /// <c>description</c> on a participant is annotating the file, not requesting behaviour —
    /// nothing is silently lost because nothing was promised.
    /// </summary>
    private static readonly HashSet<string> DocumentationKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "description", "comment", "$comment", "notes"
    };

    /// <summary>
    /// Known-unbound properties that DO claim behaviour, recorded rather than hidden. May only
    /// shrink — never add an entry to make a build pass.
    /// </summary>
    /// <remarks>
    /// <c>optional</c> on a credential requirement asks for something the platform cannot express:
    /// <see cref="CredentialRequirement"/> has no optional flag, and
    /// <c>CredentialVerifier.VerifyAsync</c> treats every requirement as mandatory. So the
    /// Trade Finance DPP requirement, whose own description says "Optional", is enforced. Fixing
    /// it means adding optional-requirement semantics to the verifier — a trust-relevant change
    /// that deserves its own review, not a quiet edit inside a blueprint refresh.
    /// </remarks>
    private static readonly HashSet<string> KnownGaps =
    [
        "walkthroughs/TradeFinance/invoice-finance-template.json :: "
        + "$.template.actions[0].credentialRequirements[1].optional"
    ];

    /// <summary>
    /// A blueprint definition: participants plus actions. Present at the root of a plain
    /// blueprint, and under <c>template</c> of a <see cref="BlueprintTemplate"/>.
    /// </summary>
    private static bool LooksLikeBlueprint(JsonObject obj)
        => obj.ContainsKey("participants") && obj.ContainsKey("actions");

    /// <summary>
    /// Every blueprint definition in the corpus, with the path prefix to report it under.
    /// </summary>
    /// <remarks>
    /// Yields BOTH shapes. Half the corpus is <see cref="BlueprintTemplate"/> — the blueprint sits
    /// under <c>template</c> — and a root-only check silently skips all of it. That is how the
    /// first version of these tests passed against blueprints it had never opened.
    /// </remarks>
    private static IEnumerable<(JsonObject Definition, string RelFile, string Path)> Definitions(
        string repoRoot)
    {
        foreach (var file in BlueprintFiles(repoRoot))
        {
            var relFile = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            var root = TryParse(file);
            if (root is null) continue;

            if (LooksLikeBlueprint(root))
            {
                yield return (root, relFile, "$");
            }
            else if (root["template"] is JsonObject template && LooksLikeBlueprint(template))
            {
                yield return (template, relFile, "$.template");
            }
        }
    }

    /// <summary>
    /// Floor on how many definitions the walk must find. Guards against a filter change silently
    /// emptying the corpus and turning every test in this class green for the wrong reason.
    /// </summary>
    private const int MinimumCorpusSize = 25;

    [Fact]
    public void TheWalkActuallyFindsTheCorpus()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);

        Definitions(repoRoot).Should().HaveCountGreaterThanOrEqualTo(MinimumCorpusSize,
            "the other tests in this class are only meaningful if they open real blueprints");
    }

    [Fact]
    public void EveryBlueprint_UsesOnlyPropertiesTheModelsStillBind()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var violations = new List<string>();

        foreach (var (definition, relFile, path) in Definitions(repoRoot))
        {
            // A definition carrying an @context is JSON-LD, where `type` is the conventional
            // alias for `@type` (the models bind the `@type` spelling). Not staleness.
            var isJsonLd = definition.ContainsKey("@context");
            CheckObject(definition, typeof(Blueprint), path, relFile, isJsonLd, violations);
        }

        violations.RemoveAll(KnownGaps.Contains);

        violations.Should().BeEmpty(
            "a blueprint property that no model binds is silently dropped at parse time — the "
            + "blueprint claims a behaviour it does not get. Unbound properties:\n"
            + string.Join("\n", violations));
    }

    /// <summary>
    /// Every credential a blueprint issues should carry a human display name. Without one the
    /// wallet card falls back to Humanize(vct), which renders the URI's last segment — readable,
    /// but not what the issuer would have chosen.
    /// </summary>
    [Fact]
    public void EveryIssuedCredential_DeclaresADisplayName()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var missing = new List<string>();

        foreach (var (definition, relFile, rootPath) in Definitions(repoRoot))
        {
            WalkFor(definition, "credentialIssuanceConfig", rootPath, (node, path) =>
            {
                if (node is not JsonObject cfg) return;
                var name = cfg["displayName"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name))
                {
                    missing.Add($"{relFile} :: {path}");
                }
            });
        }

        missing.Should().BeEmpty(
            "issued credentials need an authored card label; without one the wallet shows "
            + "Humanize(vct):\n" + string.Join("\n", missing));
    }

    /// <summary>
    /// Every credential requirement should state its presentationSource explicitly.
    /// </summary>
    /// <remarks>
    /// The default is <see cref="PresentationSource.SorchaInternal"/>, which routes to the
    /// synchronous in-process verifier and never initiates the F111/F127 async lifecycle. An
    /// author who omits the field and expected a wallet gate gets a form that asks for nothing
    /// and no error explaining why.
    /// </remarks>
    [Fact]
    public void EveryCredentialRequirement_StatesItsPresentationSource()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var missing = new List<string>();

        foreach (var (definition, relFile, rootPath) in Definitions(repoRoot))
        {
            WalkFor(definition, "credentialRequirements", rootPath, (node, path) =>
            {
                if (node is not JsonArray reqs) return;
                for (var i = 0; i < reqs.Count; i++)
                {
                    if (reqs[i] is not JsonObject req) continue;
                    if (req["presentationSource"] is null)
                    {
                        missing.Add($"{relFile} :: {path}[{i}]");
                    }
                }
            });
        }

        missing.Should().BeEmpty(
            "an omitted presentationSource silently means SorchaInternal — no wallet gate:\n"
            + string.Join("\n", missing));
    }

    // ---- walking helpers -------------------------------------------------

    private static IEnumerable<string> BlueprintFiles(string repoRoot)
    {
        foreach (var relDir in SearchDirs)
        {
            var dir = Path.Combine(repoRoot, relDir);
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }

    private static JsonObject? TryParse(string file)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Invokes <paramref name="onHit"/> for every property named <paramref name="key"/>.</summary>
    private static void WalkFor(JsonNode? node, string key, string path, Action<JsonNode?, string> onHit)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kv in obj)
                {
                    var childPath = $"{path}.{kv.Key}";
                    if (string.Equals(kv.Key, key, StringComparison.Ordinal))
                    {
                        onHit(kv.Value, childPath);
                    }
                    WalkFor(kv.Value, key, childPath, onHit);
                }
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    WalkFor(arr[i], key, $"{path}[{i}]", onHit);
                }
                break;
        }
    }

    /// <summary>
    /// Asserts every key on <paramref name="obj"/> binds to a property on <paramref name="modelType"/>,
    /// then descends into the ones whose types are themselves models.
    /// </summary>
    private static void CheckObject(
        JsonObject obj, Type modelType, string path, string relFile, bool isJsonLd,
        List<string> violations)
    {
        var bindable = BindableProperties(modelType);

        foreach (var kv in obj)
        {
            var childPath = $"{path}.{kv.Key}";

            if (FreeFormKeys.Contains(kv.Key)) continue;
            if (JsonEOperators.Contains(kv.Key)) continue;
            if (DocumentationKeys.Contains(kv.Key)) continue;
            if (kv.Key.StartsWith("x-", StringComparison.OrdinalIgnoreCase)) continue;
            if (isJsonLd && string.Equals(kv.Key, "type", StringComparison.Ordinal)) continue;

            if (!bindable.TryGetValue(kv.Key, out var prop))
            {
                violations.Add($"{relFile} :: {childPath}");
                continue;
            }

            Descend(kv.Value, prop.PropertyType, childPath, relFile, isJsonLd, violations);
        }
    }

    private static void Descend(
        JsonNode? node, Type declared, string path, string relFile, bool isJsonLd,
        List<string> violations)
    {
        var target = UnwrapCollection(Nullable.GetUnderlyingType(declared) ?? declared);
        if (!IsModelType(target)) return;

        switch (node)
        {
            case JsonObject childObj:
                CheckObject(childObj, target, path, relFile, isJsonLd, violations);
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonObject item)
                    {
                        CheckObject(item, target, $"{path}[{i}]", relFile, isJsonLd, violations);
                    }
                }
                break;
        }
    }

    /// <summary>Element type for a collection property, or the type itself.</summary>
    private static Type UnwrapCollection(Type type)
    {
        if (type.IsArray) return type.GetElementType()!;
        if (!type.IsGenericType) return type;

        // Dictionary<string, T> values are data, not members — treat as opaque.
        var def = type.GetGenericTypeDefinition();
        if (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>)) return typeof(object);

        return type.GetGenericArguments()[^1];
    }

    /// <summary>
    /// Whether a property type is one of our own models, i.e. worth recursing into. JsonNode /
    /// JsonDocument / JsonElement are free-form payloads and are deliberately excluded.
    /// </summary>
    private static bool IsModelType(Type type)
        => type.Namespace?.StartsWith("Sorcha.", StringComparison.Ordinal) == true
           && !type.IsEnum
           && type != typeof(JsonNode)
           && type != typeof(JsonDocument);

    /// <summary>Public settable properties keyed by every spelling JSON could use.</summary>
    private static Dictionary<string, PropertyInfo> BindableProperties(Type type)
    {
        var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
            {
                continue;
            }

            var name = prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? prop.Name;
            map[name] = prop;
            map[prop.Name] = prop;
            map[JsonNamingPolicy.CamelCase.ConvertName(prop.Name)] = prop;
        }

        return map;
    }

    private static string FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sorcha.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the tests must be able to locate the repo root");
        return dir!.FullName;
    }
}
