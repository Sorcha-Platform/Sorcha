// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// In-memory <see cref="IStatusListStore"/> for tests and Development-only runs.
/// </summary>
/// <remarks>
/// This is the behaviour #1482 was filed against, kept deliberately as the no-database fallback and
/// audited by <c>IStorageRegistrationLog</c> so Production/Staging refuse to start on it. It stores
/// a CLONE rather than the caller's instance: an in-memory store that hands back the same object
/// round-trips every field for free and can never exhibit a dropped-field mapping bug, which would
/// make tests against it prove nothing about the EF Core path.
/// </remarks>
public class InMemoryStatusListStore : IStatusListStore
{
    private readonly ConcurrentDictionary<string, BitstringStatusList> _lists = new();
    private readonly ConcurrentDictionary<string, long> _watermarks = new();

    /// <inheritdoc />
    public Task<BitstringStatusList?> GetAsync(string listId, CancellationToken ct = default) =>
        Task.FromResult(_lists.TryGetValue(listId, out var l) ? Clone(l) : null);

    /// <inheritdoc />
    public Task SaveAsync(BitstringStatusList list, CancellationToken ct = default)
    {
        _lists[list.Id] = Clone(list);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<long?> GetReconciledDocketAsync(string listId, CancellationToken ct = default) =>
        Task.FromResult(_watermarks.TryGetValue(listId, out var d) ? d : (long?)null);

    /// <inheritdoc />
    public Task SetReconciledDocketAsync(string listId, long docketNumber, CancellationToken ct = default)
    {
        _watermarks[listId] = docketNumber;
        return Task.CompletedTask;
    }

    private static BitstringStatusList Clone(BitstringStatusList m) => new()
    {
        Id = m.Id,
        IssuerWallet = m.IssuerWallet,
        RegisterId = m.RegisterId,
        Purpose = m.Purpose,
        EncodedList = m.EncodedList,
        Size = m.Size,
        NextAvailableIndex = m.NextAvailableIndex,
        Version = m.Version,
        LastUpdated = m.LastUpdated
    };
}
