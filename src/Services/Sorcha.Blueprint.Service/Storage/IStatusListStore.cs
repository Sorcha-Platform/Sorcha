// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// Durable home for Bitstring Status Lists.
/// </summary>
/// <remarks>
/// Revocation is the one operation whose entire purpose is to be permanent and publicly checkable,
/// so it must not live only in process memory. This interface is audited by
/// <c>IStorageRegistrationLog</c> for exactly that reason: an in-memory backing is fine for a test
/// run and unacceptable in Production.
/// </remarks>
public interface IStatusListStore
{
    /// <summary>Loads a list by id, or null when this node has never seen it.</summary>
    Task<BitstringStatusList?> GetAsync(string listId, CancellationToken ct = default);

    /// <summary>Inserts or updates a list.</summary>
    Task SaveAsync(BitstringStatusList list, CancellationToken ct = default);

    /// <summary>
    /// The docket this list has been reconciled against the ledger up to, or null if never.
    /// </summary>
    Task<long?> GetReconciledDocketAsync(string listId, CancellationToken ct = default);

    /// <summary>Records how far the ledger replay has folded.</summary>
    Task SetReconciledDocketAsync(string listId, long docketNumber, CancellationToken ct = default);
}
