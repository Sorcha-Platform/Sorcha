// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Contracts.Models;

/// <summary>Immutable wire contract for a signed IETF Token Status List.</summary>
public sealed record SignStatusListTokenResponse
{
    /// <summary>Compact-serialised <c>statuslist+jwt</c>.</summary>
    public required string Jwt { get; init; }

    /// <summary>The token's <c>iss</c> — the organisation's canonical issuer DID.</summary>
    public required string IssuerDid { get; init; }

    /// <summary>The token's <c>kid</c> — the organisation's current VC-issuance key.</summary>
    public required string Kid { get; init; }
}
