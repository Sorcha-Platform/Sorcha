// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Models;
using Sorcha.Storage.Abstractions;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Hosted service that seeds blueprint templates from JSON files on startup.
/// Scans the blueprints/templates directory for *.json files and upserts them
/// into the in-memory document store. Idempotent — skips templates that already
/// exist with the same version.
/// </summary>
public class TemplateSeedService : IHostedService
{
    private readonly IDocumentStore<BlueprintTemplate, string> _store;
    private readonly ILogger<TemplateSeedService> _logger;
    private readonly IWebHostEnvironment _environment;

    public TemplateSeedService(
        IDocumentStore<BlueprintTemplate, string> store,
        ILogger<TemplateSeedService> logger,
        IWebHostEnvironment environment)
    {
        _store = store;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Scans the templates directory and seeds templates into the store on startup.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var templatesPath = ResolveTemplatesPath();

        if (!Directory.Exists(templatesPath))
        {
            _logger.LogWarning("Templates directory not found at {Path}, skipping seed", templatesPath);
            return;
        }

        var files = Directory.GetFiles(templatesPath, "*.json");
        _logger.LogInformation("Found {Count} template files in {Path}", files.Length, templatesPath);

        var seeded = 0;
        var skipped = 0;

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var template = JsonSerializer.Deserialize<BlueprintTemplate>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (template is null || string.IsNullOrWhiteSpace(template.Title))
                {
                    _logger.LogWarning("Skipping invalid template file {File}", Path.GetFileName(file));
                    skipped++;
                    continue;
                }

                // Check if same version already exists
                var existing = await _store.GetAsync(template.Id, cancellationToken);
                if (existing is not null && existing.Version >= template.Version)
                {
                    _logger.LogDebug("Template {Id} already exists at v{Version}, skipping",
                        template.Id, existing.Version);
                    skipped++;
                    continue;
                }

                // Ensure template is published for catalogue visibility
                template.Published = true;
                await _store.UpsertAsync(template.Id, template, cancellationToken);
                seeded++;

                _logger.LogInformation("Seeded template {Id}: {Title} v{Version}",
                    template.Id, template.Title, template.Version);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse template file {File}", Path.GetFileName(file));
                skipped++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error seeding template from {File}", Path.GetFileName(file));
                skipped++;
            }
        }

        _logger.LogInformation("Template seeding complete: {Seeded} seeded, {Skipped} skipped",
            seeded, skipped);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Resolves the path to the templates directory.
    /// Checks multiple locations: content root, base directory, and parent paths.
    /// </summary>
    internal string ResolveTemplatesPath()
    {
        // First check relative to content root (typical for development)
        var contentRootPath = Path.Combine(_environment.ContentRootPath, "blueprints", "templates");
        if (Directory.Exists(contentRootPath))
            return contentRootPath;

        // Check relative to base directory (typical for published/Docker)
        var baseDirPath = Path.Combine(AppContext.BaseDirectory, "blueprints", "templates");
        if (Directory.Exists(baseDirPath))
            return baseDirPath;

        // Walk up from content root to find solution-level blueprints/templates
        var current = _environment.ContentRootPath;
        for (var i = 0; i < 6; i++)
        {
            var parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;

            var candidate = Path.Combine(current, "blueprints", "templates");
            if (Directory.Exists(candidate))
                return candidate;
        }

        // Fallback to base directory path (even if it doesn't exist yet)
        return baseDirPath;
    }
}
