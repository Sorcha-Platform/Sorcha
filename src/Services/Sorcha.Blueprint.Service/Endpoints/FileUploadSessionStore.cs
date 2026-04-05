// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// Server-side store for in-progress multi-chunk file upload sessions.
/// Holds master encryption keys that never leave the server boundary.
/// Sessions expire after 30 minutes of inactivity.
/// </summary>
/// <remarks>
/// Uses a <see cref="ConcurrentDictionary{TKey,TValue}"/> instead of
/// <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> to avoid
/// DI ambiguity with <c>JsonSchemaCache</c> which has both an IMemoryCache
/// constructor and a parameterless/int constructor.
/// </remarks>
public sealed class FileUploadSessionStore : IDisposable
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _sessionTtl = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Creates a new session store with automatic cleanup of expired sessions.
    /// </summary>
    public FileUploadSessionStore()
    {
        _cleanupTimer = new Timer(CleanupExpired, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Creates a new upload session with the given master key and salt.
    /// </summary>
    /// <returns>Opaque session ID (32-char hex GUID).</returns>
    public string CreateSession(byte[] masterFileKey, byte[] salt)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        _sessions[sessionId] = new SessionEntry(masterFileKey, salt, DateTimeOffset.UtcNow);
        return sessionId;
    }

    /// <summary>
    /// Tries to retrieve an active session by ID.
    /// </summary>
    /// <returns>True if the session exists and has not expired.</returns>
    public bool TryGetSession(string sessionId, out byte[] masterFileKey, out byte[] salt)
    {
        if (_sessions.TryGetValue(sessionId, out var entry) &&
            DateTimeOffset.UtcNow - entry.CreatedAt < _sessionTtl)
        {
            masterFileKey = entry.MasterFileKey;
            salt = entry.Salt;
            return true;
        }

        masterFileKey = [];
        salt = [];
        return false;
    }

    private void CleanupExpired(object? state)
    {
        var cutoff = DateTimeOffset.UtcNow - _sessionTtl;
        foreach (var kvp in _sessions)
        {
            if (kvp.Value.CreatedAt < cutoff)
                _sessions.TryRemove(kvp.Key, out _);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }

    private sealed record SessionEntry(byte[] MasterFileKey, byte[] Salt, DateTimeOffset CreatedAt);
}
