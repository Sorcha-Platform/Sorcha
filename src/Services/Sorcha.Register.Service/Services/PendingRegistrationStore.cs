// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Register.Models;
using StackExchange.Redis;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Redis-backed implementation of pending registration storage
/// </summary>
/// <remarks>
/// Uses Redis Hash operations for thread-safe, multi-instance access.
/// Each pending registration is stored as a JSON value in a Redis hash
/// with key "register:pending" and field = registerId.
/// TTL-based expiry is managed via per-key expiration on individual string keys
/// as a fallback; CleanupExpired scans and removes stale entries.
/// </remarks>
public class PendingRegistrationStore : IPendingRegistrationStore
{
    private const string RedisKeyPrefix = "register:pending:";
    private readonly IDatabase _database;
    private readonly ILogger<PendingRegistrationStore> _logger;

    public PendingRegistrationStore(
        IConnectionMultiplexer redis,
        ILogger<PendingRegistrationStore> logger)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _database = redis.GetDatabase();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void Add(string registerId, PendingRegistration registration)
    {
        if (string.IsNullOrEmpty(registerId))
            throw new ArgumentException("Register ID cannot be null or empty", nameof(registerId));

        ArgumentNullException.ThrowIfNull(registration);

        var key = RedisKeyPrefix + registerId;
        var json = JsonSerializer.Serialize(registration);

        // Use NX (only set if not exists) to match ConcurrentDictionary.TryAdd behavior
        var added = _database.StringSet(key, json, when: When.NotExists);

        if (!added)
        {
            _logger.LogWarning("Failed to add pending registration for ID {RegisterId} - already exists", registerId);
        }
        else
        {
            // Set TTL based on ExpiresAt so Redis auto-evicts expired entries
            var ttl = registration.ExpiresAt - DateTimeOffset.UtcNow;
            if (ttl > TimeSpan.Zero)
            {
                _database.KeyExpire(key, ttl);
            }

            _logger.LogDebug("Added pending registration for ID {RegisterId}", registerId);
        }
    }

    /// <inheritdoc />
    public bool TryRemove(string registerId, out PendingRegistration? registration)
    {
        if (string.IsNullOrEmpty(registerId))
        {
            registration = null;
            return false;
        }

        var key = RedisKeyPrefix + registerId;

        // Get and delete atomically in a single GETDEL round trip
        var value = _database.StringGetDelete(key);

        if (value.HasValue)
        {
            registration = JsonSerializer.Deserialize<PendingRegistration>((string)value!);
            _logger.LogDebug("Removed pending registration for ID {RegisterId}", registerId);
            return true;
        }

        registration = null;
        return false;
    }

    /// <inheritdoc />
    public bool Exists(string registerId)
    {
        if (string.IsNullOrEmpty(registerId))
            return false;

        return _database.KeyExists(RedisKeyPrefix + registerId);
    }

    /// <inheritdoc />
    public void CleanupExpired()
    {
        // Redis TTL handles most expiration automatically.
        // This method scans for any entries that may have missed TTL setting
        // (e.g., entries created before TTL was added).
        var server = _database.Multiplexer.GetServers().FirstOrDefault();
        if (server == null) return;

        var expiredCount = 0;
        foreach (var key in server.Keys(pattern: RedisKeyPrefix + "*"))
        {
            var json = _database.StringGet(key);
            if (!json.HasValue) continue;

            var reg = JsonSerializer.Deserialize<PendingRegistration>((string)json!);
            if (reg != null && reg.IsExpired())
            {
                if (_database.KeyDelete(key))
                {
                    var registerId = ((string)key!).Replace(RedisKeyPrefix, "");
                    _logger.LogInformation("Removed expired pending registration {RegisterId}", registerId);
                    expiredCount++;
                }
            }
        }

        if (expiredCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired pending registrations", expiredCount);
        }
    }
}
