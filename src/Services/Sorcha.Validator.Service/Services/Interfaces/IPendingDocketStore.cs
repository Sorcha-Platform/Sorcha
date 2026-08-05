// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Validator.Service.Models;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Services.Interfaces;

/// <summary>
/// In-memory store for dockets awaiting consensus.
/// Tracks dockets from proposal through signature collection to commitment.
/// </summary>
public interface IPendingDocketStore
{
    /// <summary>
    /// Add a proposed docket to the store
    /// </summary>
    /// <param name="docket">Docket to store</param>
    /// <param name="ct">Cancellation token</param>
    Task AddAsync(Docket docket, CancellationToken ct = default);

    /// <summary>
    /// Get a pending docket by ID
    /// </summary>
    /// <param name="docketId">Docket ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The docket or null if not found</returns>
    Task<Docket?> GetAsync(string docketId, CancellationToken ct = default);

    /// <summary>
    /// Get all pending dockets for a register
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of pending dockets</returns>
    Task<IReadOnlyList<Docket>> GetByRegisterAsync(string registerId, CancellationToken ct = default);

    /// <summary>
    /// Get all pending dockets with a specific status
    /// </summary>
    /// <param name="status">Docket status to filter by</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of dockets with that status</returns>
    Task<IReadOnlyList<Docket>> GetByStatusAsync(DocketStatus status, CancellationToken ct = default);

    /// <summary>
    /// Update a docket's status
    /// </summary>
    /// <param name="docketId">Docket ID</param>
    /// <param name="status">New status</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if updated, false if docket not found</returns>
    Task<bool> UpdateStatusAsync(string docketId, DocketStatus status, CancellationToken ct = default);

    /// <summary>
    /// Add a signature to a pending docket
    /// </summary>
    /// <param name="docketId">Docket ID</param>
    /// <param name="signature">Signature to add</param>
    /// <param name="validatorId">Validator who provided the signature</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Updated docket or null if not found</returns>
    Task<Docket?> AddSignatureAsync(
        string docketId,
        RegisterSignature signature,
        string validatorId,
        CancellationToken ct = default);

    /// <summary>
    /// Remove a docket from the store (after commitment or abandonment)
    /// </summary>
    /// <param name="docketId">Docket ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if removed, false if not found</returns>
    Task<bool> RemoveAsync(string docketId, CancellationToken ct = default);

    /// <summary>
    /// Get the count of pending dockets
    /// </summary>
    Task<int> GetCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Get dockets that have been pending longer than the specified timeout
    /// </summary>
    /// <param name="timeout">Timeout duration</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of stale dockets</returns>
    Task<IReadOnlyList<Docket>> GetStaleDocketsAsync(TimeSpan timeout, CancellationToken ct = default);

    /// <summary>
    /// Clear all pending dockets for a register
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of dockets cleared</returns>
    Task<int> ClearRegisterAsync(string registerId, CancellationToken ct = default);

    /// <summary>
    /// Get store statistics
    /// </summary>
    PendingDocketStoreStats GetStats();
}

/// <summary>
/// Statistics for the pending docket store
/// </summary>
public record PendingDocketStoreStats
{
    /// <summary>Total dockets currently in store</summary>
    public int TotalPending { get; init; }

    /// <summary>Dockets by status</summary>
    public IReadOnlyDictionary<DocketStatus, int> ByStatus { get; init; }
        = new Dictionary<DocketStatus, int>();

    /// <summary>Dockets by register</summary>
    public IReadOnlyDictionary<string, int> ByRegister { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Total dockets added since startup</summary>
    public long TotalAdded { get; init; }

    /// <summary>Total dockets removed since startup</summary>
    public long TotalRemoved { get; init; }

    /// <summary>Average time in store (ms)</summary>
    public double AverageTimeInStoreMs { get; init; }
}
