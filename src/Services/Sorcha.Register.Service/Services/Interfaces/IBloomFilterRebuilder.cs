// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Service.Services.Interfaces;

/// <summary>
/// Per-register bloom filter rebuild operation. Rebuilds the local-address index for
/// a single register by streaming addresses from the Wallet Service and atomically
/// swapping the rebuilt filter into Redis.
/// </summary>
/// <remarks>
/// <para>
/// Centralises the rebuild flow so it can be triggered from multiple call sites:
/// startup reconciliation (<see cref="Implementation.BloomFilterStartupRebuildService"/>),
/// admin endpoint (<c>POST /admin/rebuild-index</c>), and register-creation fan-in
/// (<see cref="RegisterCreationOrchestrator.FinalizeAsync"/>).
/// </para>
/// </remarks>
public interface IBloomFilterRebuilder
{
    /// <summary>
    /// Rebuild the bloom filter for a single register. Streams addresses from the
    /// Wallet Service and atomically swaps the rebuilt filter into Redis.
    /// </summary>
    /// <param name="registerId">The register whose bloom filter should be rebuilt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Statistics about the rebuilt index.</returns>
    Task<BloomFilterStats> RebuildAsync(string registerId, CancellationToken cancellationToken = default);
}
