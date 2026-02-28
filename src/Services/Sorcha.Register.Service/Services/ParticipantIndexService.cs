// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text.Json;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Register.Models;
using Sorcha.Storage.Abstractions;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// In-memory index for published participant records on registers.
/// Maps wallet addresses to participant IDs and maintains the latest version of each participant.
/// Optionally writes through to Redis (ICacheStore) for multi-instance deployments and faster restart recovery.
/// </summary>
public class ParticipantIndexService
{
    private readonly ILogger<ParticipantIndexService> _logger;
    private readonly ICacheStore? _cacheStore;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    private const string AddrCachePrefix = "participant:addr:";
    private const string IdCachePrefix = "participant:id:";

    // registerId -> participantId -> latest record
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PublishedParticipantRecord>> _participantIndex = new();

    // registerId -> walletAddress -> participantId
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _addressIndex = new();

    public ParticipantIndexService(ILogger<ParticipantIndexService> logger, ICacheStore? cacheStore = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheStore = cacheStore;
    }

    /// <summary>
    /// Indexes a participant transaction payload, updating both address and participant indexes.
    /// </summary>
    public void IndexParticipant(string registerId, string txId, JsonElement payload, DateTimeOffset timestamp)
    {
        try
        {
            var record = JsonSerializer.Deserialize<ParticipantRecord>(payload.GetRawText());
            if (record == null) return;

            var participants = _participantIndex.GetOrAdd(registerId, _ => new());
            var addresses = _addressIndex.GetOrAdd(registerId, _ => new());

            // Check if we should update (only if version is higher)
            if (participants.TryGetValue(record.ParticipantId, out var existing) &&
                existing.Version >= record.Version)
            {
                _logger.LogDebug(
                    "Skipping older version {Version} for participant {ParticipantId} (current: {CurrentVersion})",
                    record.Version, record.ParticipantId, existing.Version);
                return;
            }

            // Remove old address mappings if updating
            if (existing != null)
            {
                foreach (var addr in existing.Addresses)
                {
                    addresses.TryRemove(addr.WalletAddress, out _);
                }
            }

            // Build the published record
            var published = new PublishedParticipantRecord
            {
                ParticipantId = record.ParticipantId,
                OrganizationName = record.OrganizationName,
                ParticipantName = record.ParticipantName,
                Status = record.Status.ToString(),
                Version = record.Version,
                LatestTxId = txId,
                Addresses = record.Addresses.Select(a => new ParticipantAddressInfo
                {
                    WalletAddress = a.WalletAddress,
                    PublicKey = a.PublicKey,
                    Algorithm = a.Algorithm,
                    Primary = a.Primary
                }).ToList(),
                Metadata = record.Metadata,
                PublishedAt = timestamp
            };

            // Update participant index
            participants[record.ParticipantId] = published;

            // Update address index
            foreach (var addr in record.Addresses)
            {
                addresses[addr.WalletAddress] = record.ParticipantId;
            }

            _logger.LogInformation(
                "Indexed participant {ParticipantId} v{Version} on register {RegisterId} ({AddressCount} addresses)",
                record.ParticipantId, record.Version, registerId, record.Addresses.Count);

            // Write-through to Redis for multi-instance consistency
            _ = WriteToCacheAsync(registerId, published);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index participant TX {TxId} on register {RegisterId}", txId, registerId);
        }
    }

    /// <summary>
    /// Checks whether a wallet address is already claimed by an active participant on the given register.
    /// </summary>
    public bool IsAddressClaimed(string registerId, string walletAddress, string? excludeParticipantId = null)
    {
        if (!_addressIndex.TryGetValue(registerId, out var addresses))
            return false;

        if (!addresses.TryGetValue(walletAddress, out var existingParticipantId))
            return false;

        // Exclude the participant's own addresses (for updates)
        if (excludeParticipantId != null &&
            string.Equals(existingParticipantId, excludeParticipantId, StringComparison.Ordinal))
            return false;

        // Check the participant is still active
        if (_participantIndex.TryGetValue(registerId, out var participants) &&
            participants.TryGetValue(existingParticipantId, out var record) &&
            string.Equals(record.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>
    /// Gets a participant by wallet address.
    /// </summary>
    public PublishedParticipantRecord? GetByAddress(string registerId, string walletAddress)
    {
        if (!_addressIndex.TryGetValue(registerId, out var addresses))
            return null;

        if (!addresses.TryGetValue(walletAddress, out var participantId))
            return null;

        if (!_participantIndex.TryGetValue(registerId, out var participants))
            return null;

        participants.TryGetValue(participantId, out var record);
        return record;
    }

    /// <summary>
    /// Gets a participant by ID.
    /// </summary>
    public PublishedParticipantRecord? GetById(string registerId, string participantId)
    {
        if (!_participantIndex.TryGetValue(registerId, out var participants))
            return null;

        participants.TryGetValue(participantId, out var record);
        return record;
    }

    /// <summary>
    /// Lists participants on a register with pagination and status filtering.
    /// </summary>
    public ParticipantPage List(string registerId, int skip = 0, int top = 20, string? statusFilter = "active")
    {
        if (!_participantIndex.TryGetValue(registerId, out var participants))
        {
            return new ParticipantPage { Page = skip / top + 1, PageSize = top, Total = 0 };
        }

        var query = participants.Values.AsEnumerable();

        // Apply status filter
        if (!string.IsNullOrEmpty(statusFilter) &&
            !string.Equals(statusFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p =>
                string.Equals(p.Status, statusFilter, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = query.ToList();
        var total = filtered.Count;
        var paged = filtered.Skip(skip).Take(top).ToList();

        return new ParticipantPage
        {
            Page = skip / top + 1,
            PageSize = top,
            Total = total,
            Participants = paged
        };
    }

    /// <summary>
    /// Writes a participant record to Redis cache for multi-instance consistency.
    /// Fire-and-forget — cache failures do not affect in-memory index correctness.
    /// </summary>
    private async Task WriteToCacheAsync(string registerId, PublishedParticipantRecord record)
    {
        if (_cacheStore == null) return;

        try
        {
            // Cache by participant ID
            var idKey = $"{IdCachePrefix}{registerId}:{record.ParticipantId}";
            await _cacheStore.SetAsync(idKey, record, CacheTtl);

            // Cache each address mapping
            foreach (var addr in record.Addresses)
            {
                var addrKey = $"{AddrCachePrefix}{registerId}:{addr.WalletAddress}";
                await _cacheStore.SetAsync(addrKey, record, CacheTtl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write participant {ParticipantId} to Redis cache", record.ParticipantId);
        }
    }
}
