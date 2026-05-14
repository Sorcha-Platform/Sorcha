// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Verifier.Engine.Models;

/// <summary>
/// In-memory verifier session — the link between a generated presentation request
/// (QR shown to the citizen) and the eventual outcome reported back to the
/// browser. One per QR scan attempt; pruned on completion or expiry.
/// </summary>
public sealed record VerifierSession
{
    /// <summary>Opaque session identifier embedded in the request URL.</summary>
    public required string SessionId { get; init; }

    /// <summary>Verifier organisation DID — the audience the wallet binds the KB-JWT to.</summary>
    public required string ClientId { get; init; }

    /// <summary>Random nonce — wallet must echo this in the KB-JWT.</summary>
    public required string Nonce { get; init; }

    /// <summary>The intended credential type URI (vct) the verifier requires.</summary>
    public required string RequiredVct { get; init; }

    /// <summary>Required claim names the citizen must disclose. Empty = none mandatory.</summary>
    public required IReadOnlyList<string> RequiredClaims { get; init; }

    /// <summary>Optional claim names the citizen may disclose. Used for consent UI.</summary>
    public IReadOnlyList<string> OptionalClaims { get; init; } = [];

    /// <summary>Human-readable purpose shown in the wallet's consent sheet.</summary>
    public required string Purpose { get; init; }

    /// <summary>UTC time the session was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC time the session expires (typically CreatedAt + 5 min).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Outcome — null while pending.</summary>
    public VerificationOutcome? Outcome { get; init; }
}

/// <summary>Result of verifying a wallet's vp_token submission.</summary>
public sealed record VerificationOutcome
{
    /// <summary>True if every check passed.</summary>
    public required bool Accepted { get; init; }

    /// <summary>Disclosed claim values, keyed by claim name.</summary>
    public required IReadOnlyDictionary<string, object?> DisclosedClaims { get; init; }

    /// <summary>Human-readable rejection reasons. Empty when Accepted is true.</summary>
    public required IReadOnlyList<string> Errors { get; init; }

    /// <summary>UTC time the verifier completed validation.</summary>
    public required DateTimeOffset CompletedAt { get; init; }
}
