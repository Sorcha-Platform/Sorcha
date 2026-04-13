// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Default implementation of <see cref="ISchemaRefResolver"/>.
/// </summary>
public sealed class SchemaRefResolver : ISchemaRefResolver
{
    /// <summary>URI prefix for Sorcha core primitive references.</summary>
    public const string CoreUriPrefix = "https://schemas.sorcha.dev/core/";

    /// <summary>
    /// Reserved future URI scheme for primitives published to a Sorcha
    /// register. Currently rejected by the resolver — register publication
    /// of primitives is out of scope for Feature 103 and reserved for a
    /// follow-up. The resolver throws <see cref="SchemaRefResolutionException"/>
    /// rather than failing silently so the gap is loud.
    /// </summary>
    public const string DidSorchaPrefix = "did:sorcha:";

    /// <summary>Layout extension keys whose child-site values override the component default.</summary>
    private static readonly string[] LayoutOverrideKeys =
    [
        "x-pages",
        "x-sections",
        "x-introduction",
        "x-width"
    ];

    private readonly ICoreSchemaRepository _repository;
    private readonly ILogger<SchemaRefResolver> _logger;

    /// <summary>Initialises a new instance of the <see cref="SchemaRefResolver"/> class.</summary>
    public SchemaRefResolver(
        ICoreSchemaRepository repository,
        ILogger<SchemaRefResolver> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public JsonNode Flatten(JsonNode rootSchema)
    {
        ArgumentNullException.ThrowIfNull(rootSchema);

        // Deep-clone the input so the caller's tree is untouched. The walker
        // mutates the clone in place.
        var working = rootSchema.DeepClone();
        FlattenInPlace(working, new HashSet<string>(StringComparer.Ordinal));
        return working;
    }

    /// <summary>
    /// Recursively walks <paramref name="node"/>, resolving any Sorcha core
    /// <c>$ref</c> objects in place.
    /// </summary>
    /// <param name="node">The current node (mutated).</param>
    /// <param name="visiting">URIs currently being resolved on this branch — used for cycle detection.</param>
    private void FlattenInPlace(JsonNode? node, HashSet<string> visiting)
    {
        switch (node)
        {
            case JsonObject obj:
                FlattenObject(obj, visiting);
                break;

            case JsonArray arr:
                foreach (var item in arr)
                {
                    FlattenInPlace(item, visiting);
                }
                break;
        }
    }

    private void FlattenObject(JsonObject obj, HashSet<string> visiting)
    {
        // Step 1: if this object IS a $ref site (and the ref points at a
        // Sorcha core primitive), resolve it before walking children.
        if (obj.TryGetPropertyValue("$ref", out var refNode) &&
            refNode is JsonValue refValue &&
            refValue.TryGetValue<string>(out var refUri) &&
            !string.IsNullOrEmpty(refUri))
        {
            if (refUri.StartsWith(CoreUriPrefix, StringComparison.Ordinal))
            {
                ResolveCoreRefInPlace(obj, refUri, visiting);
                // After resolution the object's contents are entirely from the
                // component (with overrides reapplied). Recurse into the new
                // children to flatten any nested refs the component itself
                // declared. The component's own URI is already in `visiting`
                // so a self-reference would be caught.
                visiting.Add(refUri);
                try
                {
                    FlattenChildren(obj, visiting);
                }
                finally
                {
                    visiting.Remove(refUri);
                }
                return;
            }

            if (refUri.StartsWith(DidSorchaPrefix, StringComparison.Ordinal))
            {
                throw new SchemaRefResolutionException(
                    $"DID-based primitive references are reserved for a future feature and not yet supported: '{refUri}'. " +
                    "Use the HTTPS form 'https://schemas.sorcha.dev/core/{Name}/v{N}' instead.",
                    refUri);
            }

            // Any other $ref form (e.g. JSON Schema internal "#/definitions/Foo")
            // is left alone — that's not the resolver's concern.
        }

        // Step 2: walk into children even if the object wasn't a $ref.
        FlattenChildren(obj, visiting);
    }

    private void FlattenChildren(JsonObject obj, HashSet<string> visiting)
    {
        // Snapshot keys because resolution mutates child values.
        foreach (var key in obj.Select(kvp => kvp.Key).ToList())
        {
            FlattenInPlace(obj[key], visiting);
        }
    }

    private void ResolveCoreRefInPlace(JsonObject site, string refUri, HashSet<string> visiting)
    {
        if (visiting.Contains(refUri))
        {
            throw new SchemaRefResolutionException(
                $"Cycle detected in $ref chain: '{refUri}' is already being resolved on this branch. " +
                $"Refactor the primitives so they form a directed acyclic graph.",
                refUri);
        }

        var stored = _repository.Get(refUri)
            ?? throw new SchemaRefResolutionException(
                $"Unknown $ref: '{refUri}' is not registered in the core primitive library. " +
                $"Confirm the file exists at blueprints/schemas/sorcha-core/ and that CoreSchemaSeedService " +
                $"loaded it on startup (look for 'Loaded core primitive {refUri}' in the logs).",
                refUri);

        // The repository returns the live stored reference — clone before mutating.
        var resolvedComponent = stored.DeepClone();
        if (resolvedComponent is not JsonObject resolvedObj)
        {
            throw new SchemaRefResolutionException(
                $"Primitive '{refUri}' is not a JSON object — core primitives must be objects.",
                refUri);
        }

        // Capture the consumer's layout overrides BEFORE replacing the site contents.
        // Anything else the consumer wrote alongside $ref is silently dropped — the
        // contract says only layout extensions can be overridden inline.
        var overrides = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var key in LayoutOverrideKeys)
        {
            if (site.TryGetPropertyValue(key, out var overrideValue) && overrideValue is not null)
            {
                overrides[key] = overrideValue.DeepClone();
            }
        }

        // Replace site contents with the resolved component contents in place.
        // This works whether `site` is a property value, an array item, or the
        // root node — the parent reference is preserved.
        site.Clear();
        var componentKeys = resolvedObj.Select(kvp => kvp.Key).ToList();
        foreach (var key in componentKeys)
        {
            // Detach the value from the cloned component so we can reparent it.
            var detached = resolvedObj[key];
            resolvedObj.Remove(key);
            site[key] = detached;
        }

        // Reapply the consumer's layout overrides — child wins for layout.
        foreach (var (key, value) in overrides)
        {
            site[key] = value;
        }

        _logger.LogDebug("Resolved $ref {Uri} at site (overrides applied: {Overrides})",
            refUri, overrides.Count);
    }
}
