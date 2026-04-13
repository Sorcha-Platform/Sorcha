// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Hosted service that seeds Sorcha core identity primitives from JSON files on
/// startup. Scans <c>blueprints/schemas/sorcha-core/*.json</c> and upserts each
/// primitive into <see cref="ICoreSchemaRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="TemplateSeedService"/> in structure (IHostedService, multi-
/// path resolution walking content root → base dir → parent dirs, idempotent
/// upsert, loud failure on missing required metadata).
/// </para>
/// <para>
/// Validation rules applied at load time (per
/// <c>specs/103-verified-citizen-v2/contracts/identity-primitive-format.md</c>):
/// </para>
/// <list type="number">
///   <item>Root <c>$id</c> must be present and start with <c>https://schemas.sorcha.dev/core/</c>.</item>
///   <item>File name must match <c>{LastSegment}.v{N}.json</c> where the
///     segments come from the <c>$id</c> path.</item>
///   <item><c>type</c> must equal <c>"object"</c>.</item>
///   <item><c>title</c> must be present and non-empty.</item>
///   <item><c>properties</c> must be present and contain at least one property.</item>
/// </list>
/// <para>
/// Cycle detection and <c>x-persona</c> path validation are deferred to the
/// resolver — they depend on cross-primitive state and are better checked at
/// resolve time with all primitives available.
/// </para>
/// </remarks>
internal sealed class CoreSchemaSeedService : IHostedService
{
    private readonly ICoreSchemaRepository _repository;
    private readonly ILogger<CoreSchemaSeedService> _logger;
    private readonly IWebHostEnvironment _environment;

    private const string SchemasSubPath = "blueprints/schemas/sorcha-core";
    private const string RequiredIdPrefix = "https://schemas.sorcha.dev/core/";

    /// <summary>Initialises a new instance of the <see cref="CoreSchemaSeedService"/> class.</summary>
    public CoreSchemaSeedService(
        ICoreSchemaRepository repository,
        ILogger<CoreSchemaSeedService> logger,
        IWebHostEnvironment environment)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var path = ResolveSchemasPath();

        if (!Directory.Exists(path))
        {
            _logger.LogWarning(
                "Core primitive directory not found at {Path}; skipping seed (no primitives will be resolvable via $ref)",
                path);
            return;
        }

        var files = Directory.GetFiles(path, "*.json");
        _logger.LogInformation(
            "Found {Count} core primitive files in {Path}", files.Length, path);

        var seeded = 0;
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var node = JsonNode.Parse(json)
                    ?? throw new JsonException("Primitive file parsed to null");

                ValidatePrimitive(node, file);

                var id = node["$id"]!.GetValue<string>();
                _repository.Upsert(id, node);
                seeded++;

                _logger.LogInformation(
                    "Loaded core primitive {Id} (title: {Title}) from {File}",
                    id, node["title"]?.GetValue<string>(), Path.GetFileName(file));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Loud failure: a bad primitive file should prevent Blueprint
                // Service from serving blueprints that might reference it, so
                // we rethrow to fail startup.
                _logger.LogError(ex,
                    "Core primitive {File} failed validation; aborting startup",
                    Path.GetFileName(file));
                throw;
            }
        }

        _logger.LogInformation("Core primitive seeding complete: {Seeded} loaded", seeded);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Validation rules applied to a loaded primitive tree. Exposed internally
    /// so unit tests can reach it with handcrafted <see cref="JsonNode"/>
    /// trees without round-tripping to disk.
    /// </summary>
    internal const string RequiredSchemaDialect = "https://json-schema.org/draft/2020-12/schema";

    internal static void ValidatePrimitive(JsonNode node, string fileName)
    {
        if (node is not JsonObject obj)
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)}: top-level value must be a JSON object");

        // Rule 0: $schema dialect must be draft-2020-12. A primitive
        // accidentally written against a different dialect (e.g. draft-7 with
        // `exclusiveMaximum: true`) would load successfully but silently
        // mis-validate at runtime. Fail loud at load time instead.
        var schemaNode = obj["$schema"];
        if (schemaNode is null)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)}: missing '$schema' — all core primitives must declare '$schema': '{RequiredSchemaDialect}'");
        }
        var schemaDialect = schemaNode.GetValue<string>();
        if (!string.Equals(schemaDialect, RequiredSchemaDialect, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)}: '$schema' must be '{RequiredSchemaDialect}' (got '{schemaDialect}')");
        }

        // Rule 1: $id present and well-prefixed
        var idNode = obj["$id"]
            ?? throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)}: missing '$id'");
        var id = idNode.GetValue<string>();
        if (!id.StartsWith(RequiredIdPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)}: '$id' must start with '{RequiredIdPrefix}' (got '{id}')");
        }

        // Rule 2: file name must match the last two $id segments — {Name}.v{N}.json
        var idSuffix = id.Substring(RequiredIdPrefix.Length); // e.g. "PostalAddress/v1"
        var segments = idSuffix.Split('/');
        if (segments.Length != 2)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)}: '$id' path after the prefix must be '{{Name}}/v{{N}}' (got '{idSuffix}')");
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(segments[1], @"^v\d+$"))
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)}: version segment must match 'v{{N}}' (got '{segments[1]}')");
        }
        var expectedFileName = $"{segments[0]}.{segments[1]}.json";
        if (!string.Equals(Path.GetFileName(fileName), expectedFileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)}: file name must be '{expectedFileName}' to match '$id' ({id})");
        }

        // Rule 3: type == "object"
        var typeNode = obj["type"]
            ?? throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)}: missing 'type'");
        var typeValue = typeNode.GetValue<string>();
        if (!string.Equals(typeValue, "object", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)}: 'type' must be 'object' (got '{typeValue}')");
        }

        // Rule 4: title present and non-empty
        var titleNode = obj["title"]
            ?? throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)}: missing 'title'");
        var title = titleNode.GetValue<string>();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)}: 'title' must be non-empty");
        }

        // Rule 5: properties present and non-empty
        if (obj["properties"] is not JsonObject propsObj || propsObj.Count == 0)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)}: 'properties' must be present and contain at least one property");
        }
    }

    /// <summary>
    /// Resolves the path to the Sorcha core primitives directory. Walks the same
    /// content-root → base-dir → parent-chain pattern as
    /// <see cref="TemplateSeedService.ResolveTemplatesPath"/>.
    /// </summary>
    internal string ResolveSchemasPath()
    {
        var contentRootPath = Path.Combine(_environment.ContentRootPath, SchemasSubPath);
        if (Directory.Exists(contentRootPath)) return contentRootPath;

        var baseDirPath = Path.Combine(AppContext.BaseDirectory, SchemasSubPath);
        if (Directory.Exists(baseDirPath)) return baseDirPath;

        var current = _environment.ContentRootPath;
        for (var i = 0; i < 6; i++)
        {
            var parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;

            var candidate = Path.Combine(current, SchemasSubPath);
            if (Directory.Exists(candidate)) return candidate;
        }

        return baseDirPath;
    }
}
