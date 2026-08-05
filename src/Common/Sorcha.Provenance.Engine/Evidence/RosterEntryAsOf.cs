// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Provenance.Engine.Evidence;

/// <summary>
/// One validator's membership as it stood at the subject docket.
/// </summary>
public sealed record RosterEntryAsOf
{
    /// <summary>The validator's identifier (wallet address or DID).</summary>
    public required string ValidatorId { get; init; }

    /// <summary>The docket-signing public key authorised for this validator then (base64).</summary>
    public required string PublicKey { get; init; }

    /// <summary>
    /// Whether this entry held signing authority at the subject docket.
    /// </summary>
    /// <remarks>
    /// Set by the resolver, which maps the register's <c>ValidatorKeyStatus</c> onto a single
    /// question: could this key legitimately have signed <i>this</i> docket? <c>Active</c> and
    /// <c>Rotated</c> both can — a rotated key is exactly what a historical docket was signed with,
    /// and excluding it is one of the ways the roster-as-of trap is sprung. <c>Revoked</c> and
    /// <c>Ejected</c> cannot.
    /// </remarks>
    public required bool HeldAuthority { get; init; }
}
