// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Service.Models;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Server-side store for the citizen's pending-application notice (Feature 124).
/// Set by the walkthrough script (or a future application-submission flow) and
/// read by the wallet PWA every time its Home renders. TTL-based — eventually
/// self-clearing if no credential ever arrives.
/// </summary>
public interface IPendingApplicationStore
{
    /// <summary>Reads the citizen's active notice, or null if absent.</summary>
    Task<PendingApplicationNotice?> GetAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>
    /// Sets or replaces the citizen's notice. Idempotent — calling with a new
    /// label replaces the prior label and resets the TTL.
    /// </summary>
    Task<PendingApplicationNotice> SetAsync(Guid platformUserId, string label, CancellationToken ct = default);

    /// <summary>Clears the citizen's notice. Idempotent — no-op if already absent.</summary>
    Task ClearAsync(Guid platformUserId, CancellationToken ct = default);
}
