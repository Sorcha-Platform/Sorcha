// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Allocates indexes, flips bits, and publishes signed Token Status List 2024 JWTs
/// for citizen wallet device delegation revocation status (Feature 114).
/// </summary>
/// <remarks>
/// One or more lists per tenant org. Indexes are NEVER reused — revocation only
/// flips the bit; allocation only moves the watermark. The signed JWT served to
/// verifiers is regenerated on every flip and on a 1h cadence by the
/// CitizenStatusListPublisherService background worker.
/// </remarks>
public interface ICitizenStatusListPublisher
{
    /// <summary>
    /// Allocates the next free bit index for the given org. Creates a new list
    /// (with <c>ListId = previous + 1</c>) when the current list is full.
    /// </summary>
    /// <param name="organizationId">Owning tenant org.</param>
    /// <param name="signingWalletAddress">Wallet whose <c>sorcha:citizen-status-signing</c> derivation signs the list JWT.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>(ListId, IndexInList)</c> — both written into the device's delegation credential <c>status.status_list</c> claim.</returns>
    Task<(int ListId, int IndexInList)> AllocateIndexAsync(
        Guid organizationId,
        string signingWalletAddress,
        CancellationToken ct = default);

    /// <summary>
    /// Sets the revoked bit at the given index and regenerates the signed list JWT.
    /// Idempotent — already-set bits remain set.
    /// </summary>
    Task FlipAsync(
        Guid organizationId,
        int listId,
        int indexInList,
        string signingWalletAddress,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the current signed Token Status List 2024 JWT for the given list.
    /// Public — no auth — verifiers fetch this offline-cacheably.
    /// </summary>
    /// <returns>Compact JWS, served as <c>application/statuslist+jwt</c>.</returns>
    Task<string?> GetSignedListAsync(
        Guid organizationId,
        int listId,
        CancellationToken ct = default);

    /// <summary>
    /// Regenerates and re-signs the list JWT, refreshing <c>iat</c> + <c>exp</c>.
    /// Called by background worker every 1h and inline on every flip.
    /// </summary>
    Task RegenerateAsync(
        Guid organizationId,
        int listId,
        string signingWalletAddress,
        CancellationToken ct = default);

    /// <summary>
    /// Builds the public URL a verifier fetches the given list at, suitable for
    /// embedding in delegation credentials' <c>status.status_list.uri</c>.
    /// </summary>
    string BuildStatusListUri(Guid organizationId, int listId);
}
