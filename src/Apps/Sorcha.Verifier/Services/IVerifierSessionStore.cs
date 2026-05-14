// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Verifier.Services.Models;

namespace Sorcha.Verifier.Services;

/// <summary>
/// In-memory session store for the reference verifier. Keys are session ids; values are
/// pending or completed <see cref="VerifierSession"/>s. The reference impl is a singleton —
/// production verifiers would back this with Redis. v1 scope is a demo-grade verifier.
/// </summary>
public interface IVerifierSessionStore
{
    /// <summary>Persist a newly-created session.</summary>
    void Add(VerifierSession session);

    /// <summary>Fetch by id. Returns null if missing or expired.</summary>
    VerifierSession? Get(string sessionId);

    /// <summary>Replace the session record (typically to attach an outcome).</summary>
    void Update(VerifierSession session);

    /// <summary>Drop sessions whose <see cref="VerifierSession.ExpiresAt"/> has passed.</summary>
    void PruneExpired(DateTimeOffset now);
}
