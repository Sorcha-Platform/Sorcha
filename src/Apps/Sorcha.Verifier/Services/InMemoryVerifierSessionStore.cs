// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Sorcha.Verifier.Engine.Models;

namespace Sorcha.Verifier.Services;

/// <inheritdoc />
public sealed class InMemoryVerifierSessionStore : IVerifierSessionStore
{
    private readonly ConcurrentDictionary<string, VerifierSession> _sessions = new();

    /// <inheritdoc />
    public void Add(VerifierSession session) => _sessions[session.SessionId] = session;

    /// <inheritdoc />
    public VerifierSession? Get(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return null;
        if (session.ExpiresAt <= DateTimeOffset.UtcNow && session.Outcome is null)
        {
            _sessions.TryRemove(sessionId, out _);
            return null;
        }
        return session;
    }

    /// <inheritdoc />
    public void Update(VerifierSession session) => _sessions[session.SessionId] = session;

    /// <inheritdoc />
    public void PruneExpired(DateTimeOffset now)
    {
        foreach (var (id, session) in _sessions)
        {
            if (session.ExpiresAt <= now && session.Outcome is null)
            {
                _sessions.TryRemove(id, out _);
            }
        }
    }
}
