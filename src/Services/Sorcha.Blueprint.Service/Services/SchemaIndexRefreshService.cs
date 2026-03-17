// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Schemas.Services;
using Sorcha.Blueprint.Service.Services.Interfaces;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Background service that periodically refreshes the schema index from all providers.
/// Non-blocking startup: each provider runs in its own Task.
/// </summary>
public class SchemaIndexRefreshService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SchemaIndexRefreshService> _logger;
    private readonly TimeSpan _refreshInterval;

    public SchemaIndexRefreshService(
        IServiceScopeFactory scopeFactory,
        ILogger<SchemaIndexRefreshService> logger,
        TimeSpan? refreshInterval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _refreshInterval = refreshInterval ?? TimeSpan.FromHours(24);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Schema index refresh service starting. Refresh interval: {Interval}",
            _refreshInterval);

        // Initial refresh on startup (non-blocking per provider)
        await RefreshAllProvidersAsync(stoppingToken);

        // Periodic refresh
        using var timer = new PeriodicTimer(_refreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            _logger.LogInformation("Starting periodic schema index refresh");
            await RefreshAllProvidersAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Refreshes all providers. Each runs in its own Task for non-blocking operation.
    /// </summary>
    private async Task RefreshAllProvidersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var providers = scope.ServiceProvider.GetServices<IExternalSchemaProvider>().ToList();
        var indexService = scope.ServiceProvider.GetRequiredService<ISchemaIndexService>();

        // Get the concrete type to access provider management methods
        var concreteService = indexService as SchemaIndexService;

        _logger.LogInformation("Refreshing schema index from {Count} providers", providers.Count);

        var tasks = providers.Select(provider =>
            RefreshProviderAsync(provider, concreteService, cancellationToken));

        await Task.WhenAll(tasks);

        _logger.LogInformation("Schema index refresh complete for all providers");
    }

    private async Task RefreshProviderAsync(
        IExternalSchemaProvider provider,
        SchemaIndexService? indexService,
        CancellationToken cancellationToken)
    {
        var providerName = provider.ProviderName;

        // Check backoff
        if (indexService?.IsProviderInBackoff(providerName) == true)
        {
            _logger.LogDebug("Skipping {Provider} — in backoff period", providerName);
            return;
        }

        indexService?.SetProviderLoading(providerName, true);

        try
        {
            _logger.LogInformation("Refreshing schema index from {Provider}", providerName);

            // Check availability first
            if (!await provider.IsAvailableAsync(cancellationToken))
            {
                _logger.LogWarning("Provider {Provider} is unavailable, skipping refresh", providerName);
                indexService?.RecordProviderFailure(providerName, "Provider unavailable");
                return;
            }

            // Fetch catalog
            var catalog = await provider.GetCatalogAsync(cancellationToken);
            var entries = catalog.Select(entry => new ProviderSchemaEntry(
                SourceUri: entry.Url,
                Title: entry.Name,
                Description: entry.Description,
                SectorTags: entry.SectorTags ?? provider.DefaultSectorTags,
                SchemaVersion: "1.0.0",
                Content: entry.Content ?? CreateMinimalSchema(entry.Name, entry.Description)
            )).ToList();

            if (entries.Count > 0 && indexService is not null)
            {
                var upserted = await indexService.UpsertFromProviderAsync(
                    providerName, entries, cancellationToken);

                _logger.LogInformation("Refreshed {Provider}: {Upserted} upserted of {Total} entries",
                    providerName, upserted, entries.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to refresh schema index from {Provider}", providerName);
            indexService?.RecordProviderFailure(providerName, ex.Message);
        }
        finally
        {
            indexService?.SetProviderLoading(providerName, false);
        }
    }

    /// <summary>
    /// Triggers a manual refresh for a specific provider.
    /// </summary>
    public async Task RefreshProviderManuallyAsync(string providerName, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var providers = scope.ServiceProvider.GetServices<IExternalSchemaProvider>();
        var provider = providers.FirstOrDefault(p =>
            string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            throw new KeyNotFoundException($"Provider '{providerName}' not found");
        }

        var indexService = scope.ServiceProvider.GetRequiredService<ISchemaIndexService>() as SchemaIndexService;
        await RefreshProviderAsync(provider, indexService, cancellationToken);
    }

    private static JsonDocument CreateMinimalSchema(string name, string description)
    {
        var obj = new Dictionary<string, object?>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = name,
            ["description"] = description ?? "",
            ["type"] = "object"
        };
        var json = JsonSerializer.Serialize(obj);
        return JsonDocument.Parse(json);
    }
}
