// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.ObjectModel;

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Response body for <c>GET /api/v1/wallet/credentials</c>. Used by a freshly-enrolled
/// wallet to seed its cache; subsequent updates flow through <c>GET /api/v1/wallet/sync</c>.
/// </summary>
public sealed record CredentialListResponse
{
    /// <summary>Full snapshot of credentials currently issued to the authenticated citizen.</summary>
    public IReadOnlyList<CachedCredentialPayload> Credentials { get; init; } =
        new ReadOnlyCollection<CachedCredentialPayload>([]);
}
