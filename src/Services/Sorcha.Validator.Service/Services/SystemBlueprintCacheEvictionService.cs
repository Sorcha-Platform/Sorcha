// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Feature 197. Evicts the cached system blueprints whenever a docket seals on the system register,
/// so a validator never validates governance or system workflows against a definition that a later
/// publication has superseded. Every node (including replicas, whose peer-sync writes go through
/// the Register Service WriteDocket handler) publishes <c>docket:confirmed</c>, so this holds
/// on every node.
/// </summary>
/// <remarks>
/// Eviction is by blueprint id only; content-keyed (pinned) cache entries are untouched, which is
/// correct — a pinned publication id never changes meaning. Failures are logged and never thrown
/// out of the handler. With no <see cref="IEventSubscriber"/> registered this service is a no-op.
/// </remarks>
public sealed class SystemBlueprintCacheEvictionService : BackgroundService
{
    private readonly IBlueprintCache _blueprintCache;
    private readonly IEventSubscriber? _eventSubscriber;
    private readonly ILogger<SystemBlueprintCacheEvictionService> _logger;

    public SystemBlueprintCacheEvictionService(
        IBlueprintCache blueprintCache,
        ILogger<SystemBlueprintCacheEvictionService> logger,
        IEventSubscriber? eventSubscriber = null)
    {
        _blueprintCache = blueprintCache ?? throw new ArgumentNullException(nameof(blueprintCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventSubscriber = eventSubscriber;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_eventSubscriber is null)
        {
            _logger.LogDebug("No event subscriber available — system blueprint cache eviction is disabled");
            return;
        }

        try
        {
            await _eventSubscriber.SubscribeAsync<DocketConfirmedEvent>(
                RegisterEventChannels.DocketConfirmed,
                evt => HandleDocketConfirmedAsync(evt, stoppingToken),
                stoppingToken);

            _logger.LogInformation(
                "Subscribed to {Channel} — system blueprint cache evicts when the system register seals",
                RegisterEventChannels.DocketConfirmed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to subscribe to {Channel} — system blueprint cache will only refresh on expiry",
                RegisterEventChannels.DocketConfirmed);
        }
    }

    private async Task HandleDocketConfirmedAsync(DocketConfirmedEvent evt, CancellationToken ct)
    {
        if (!string.Equals(evt.RegisterId, SystemRegisterConstants.SystemRegisterId, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var blueprintId in SystemBlueprintCatalog.Ids)
        {
            if (ct.IsCancellationRequested) return;

            try
            {
                await _blueprintCache.RemoveAsync(blueprintId, ct);
                FederationValidatorMetrics.SystemBlueprintCacheEviction("evicted");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                FederationValidatorMetrics.SystemBlueprintCacheEviction("failed");
                _logger.LogWarning(ex,
                    "Failed to evict system blueprint {BlueprintId} from the cache after docket {DocketId} sealed on the system register",
                    blueprintId, evt.DocketId);
            }
        }

        _logger.LogDebug(
            "System blueprint cache evicted after docket {DocketId} sealed on the system register",
            evt.DocketId);
    }
}
