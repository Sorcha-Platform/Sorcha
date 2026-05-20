// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Service.Services.Interfaces;
using StackExchange.Redis;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Default <see cref="ICitizenPresentationLogReporter"/>. Dedupes each reported
/// entry with a Redis SET-NX claim, then forwards the newly-claimed entries via
/// <see cref="IPresentationLogForwarder"/> (Feature 114 US5).
/// </summary>
public sealed class CitizenPresentationLogReporter : ICitizenPresentationLogReporter
{
    private static readonly TimeSpan DedupeTtl = TimeSpan.FromHours(24);

    private readonly IConnectionMultiplexer _redis;
    private readonly IPresentationLogForwarder _forwarder;
    private readonly ILogger<CitizenPresentationLogReporter> _logger;

    /// <summary>Initialise a new instance.</summary>
    public CitizenPresentationLogReporter(
        IConnectionMultiplexer redis,
        IPresentationLogForwarder forwarder,
        ILogger<CitizenPresentationLogReporter> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _forwarder = forwarder ?? throw new ArgumentNullException(nameof(forwarder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<int> ReportAsync(
        Guid platformUserId,
        IReadOnlyList<PresentationLogEntry> entries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var accepted = 0;
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            if (!await TryClaimAsync(entry.Id))
            {
                _logger.LogDebug(
                    "Presentation-log entry {EntryId} already reported (platformUser={PlatformUserId}); skipping.",
                    entry.Id, platformUserId);
                continue;
            }

            await _forwarder.ForwardAsync(platformUserId, entry, ct);
            accepted++;
        }

        return accepted;
    }

    /// <summary>
    /// Atomic first-writer-wins claim on the entry id. Returns <c>true</c> when this
    /// caller claimed the id (so it should forward), <c>false</c> when it was already
    /// claimed. Degrades open — a Redis failure forwards the entry rather than
    /// silently dropping it; the downstream consumer carries its own dedupe.
    /// </summary>
    private async Task<bool> TryClaimAsync(Guid entryId)
    {
        var key = $"sorcha:wallet:presentation-log-dedupe:{entryId}";
        try
        {
            return await _redis.GetDatabase()
                .StringSetAsync(key, "1", DedupeTtl, When.NotExists);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Redis dedupe claim failed for presentation-log entry {EntryId}; forwarding without dedupe.",
                entryId);
            return true;
        }
    }
}
