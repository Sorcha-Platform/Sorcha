// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;
using Sorcha.Register.Models;

namespace Sorcha.Register.Service.Services;

/// <summary>Writes <see cref="SystemBlueprintDriftState"/> as its camelCase name.</summary>
public sealed class SystemBlueprintDriftStateConverter()
    : JsonStringEnumConverter<SystemBlueprintDriftState>(JsonNamingPolicy.CamelCase);

/// <summary>
/// How the system blueprint definitions shipped in this node's image compare with what the
/// system register currently holds (Feature 197, #1466).
/// </summary>
[JsonConverter(typeof(SystemBlueprintDriftStateConverter))]
public enum SystemBlueprintDriftState
{
    /// <summary>The image's definition is the register's current publication.</summary>
    InSync,

    /// <summary>The image's definition matches an older publication; the register holds a newer one.</summary>
    ImageBehind,

    /// <summary>The register holds publications, none of which match the image's definition.</summary>
    ImageAhead,

    /// <summary>Neither the register nor the image has the blueprint.</summary>
    Missing,

    /// <summary>The state could not be determined this cycle. Never reported as <see cref="InSync"/>.</summary>
    Unknown
}

/// <summary>Drift of one system blueprint.</summary>
/// <param name="BlueprintId">Blueprint identifier.</param>
/// <param name="State">Classification.</param>
/// <param name="CurrentPublicationTxId">Current publication on the register; null when absent or unreadable.</param>
/// <param name="CurrentVersion">1-based ordinal of the current publication.</param>
/// <param name="ImagePublicationTxId">Id a publish of the image's definition would produce; null when the image lacks the file.</param>
/// <param name="ImageMatchesVersion">Set only for <see cref="SystemBlueprintDriftState.ImageBehind"/>: version of the matching publication.</param>
/// <param name="CheckedAt">When the comparison was made.</param>
public sealed record SystemBlueprintDriftEntry(
    string BlueprintId,
    SystemBlueprintDriftState State,
    string? CurrentPublicationTxId,
    int? CurrentVersion,
    string? ImagePublicationTxId,
    int? ImageMatchesVersion,
    DateTimeOffset CheckedAt);

/// <summary>Reports drift between the image's system blueprints and the system register.</summary>
public interface ISystemBlueprintDriftReporter
{
    /// <summary>Computes one entry per system blueprint, in <see cref="SystemBlueprintCatalog.Ids"/> order.</summary>
    Task<IReadOnlyList<SystemBlueprintDriftEntry>> ComputeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ISystemBlueprintDriftReporter"/>. A singleton: it resolves the scoped
/// <see cref="SystemRegisterService"/> per call.
/// </summary>
public sealed class SystemBlueprintDriftReporter : ISystemBlueprintDriftReporter
{
    private readonly ISystemBlueprintCatalogSource _catalog;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _time;
    private readonly ILogger<SystemBlueprintDriftReporter> _logger;

    /// <summary>Creates the reporter.</summary>
    public SystemBlueprintDriftReporter(
        ISystemBlueprintCatalogSource catalog,
        IServiceScopeFactory scopeFactory,
        TimeProvider time,
        ILogger<SystemBlueprintDriftReporter> logger)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SystemBlueprintDriftEntry>> ComputeAsync(
        CancellationToken cancellationToken = default)
    {
        var entries = new List<SystemBlueprintDriftEntry>(SystemBlueprintCatalog.Ids.Count);

        foreach (var id in SystemBlueprintCatalog.Ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var checkedAt = _time.GetUtcNow();

            try
            {
                var imageId = _catalog.TryComputePublicationId(id);

                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<SystemRegisterService>();
                var publications = await service.GetPublicationsAsync(id, cancellationToken);

                entries.Add(Classify(id, imageId, publications, checkedAt));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "System blueprint {BlueprintId} drift could not be determined", id);
                entries.Add(new SystemBlueprintDriftEntry(
                    id, SystemBlueprintDriftState.Unknown, null, null, null, null, checkedAt));
            }
        }

        return entries;
    }

    /// <summary>
    /// Classifies one blueprint. Pure. <paramref name="orderedPublications"/> is oldest to newest in
    /// ledger order; the last is current and versions are 1-based indexes into it.
    /// </summary>
    public static SystemBlueprintDriftEntry Classify(
        string blueprintId,
        string? imagePublicationId,
        IReadOnlyList<TransactionModel> orderedPublications,
        DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(orderedPublications);

        if (orderedPublications.Count == 0)
        {
            // Image has the file but the register does not: nothing published yet. With neither,
            // there is nothing anywhere.
            return new SystemBlueprintDriftEntry(
                blueprintId, SystemBlueprintDriftState.Missing, null, null, imagePublicationId, null, checkedAt);
        }

        var current = orderedPublications[^1];
        var currentVersion = orderedPublications.Count;

        if (imagePublicationId is null)
        {
            // The node cannot say which side is right.
            return new SystemBlueprintDriftEntry(
                blueprintId, SystemBlueprintDriftState.Unknown, current.TxId, currentVersion, null, null, checkedAt);
        }

        if (string.Equals(current.TxId, imagePublicationId, StringComparison.Ordinal))
        {
            return new SystemBlueprintDriftEntry(
                blueprintId, SystemBlueprintDriftState.InSync, current.TxId, currentVersion, imagePublicationId, null, checkedAt);
        }

        for (var i = 0; i < orderedPublications.Count - 1; i++)
        {
            if (string.Equals(orderedPublications[i].TxId, imagePublicationId, StringComparison.Ordinal))
            {
                return new SystemBlueprintDriftEntry(
                    blueprintId, SystemBlueprintDriftState.ImageBehind, current.TxId, currentVersion,
                    imagePublicationId, i + 1, checkedAt);
            }
        }

        return new SystemBlueprintDriftEntry(
            blueprintId, SystemBlueprintDriftState.ImageAhead, current.TxId, currentVersion, imagePublicationId, null, checkedAt);
    }
}
