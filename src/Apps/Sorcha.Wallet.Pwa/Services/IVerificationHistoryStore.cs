// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Per-device store for verifications the user has performed — the
/// citizen-as-verifier history surfaced on Activity (Feature 125, T019 / R-005).
/// Client-side only in v1; no server persistence. Verifications are the
/// user's private notebook (<i>"I verified Liam Buchanan on this date"</i>),
/// not transactions on a Sorcha register.
/// </summary>
/// <remarks>
/// Clearing site data wipes the history — acceptable v1 behaviour matching
/// the F124 welcome-takeover flag's per-device scope. Server-side persistence
/// is deferred to a future spec.
/// </remarks>
public interface IVerificationHistoryStore
{
    /// <summary>Append a new verification record.</summary>
    Task AddAsync(VerificationRecord record, CancellationToken ct = default);

    /// <summary>List all records, newest first.</summary>
    Task<IReadOnlyList<VerificationRecord>> ListAsync(CancellationToken ct = default);

    /// <summary>List records for a single context (Personal when <paramref name="contextOrgId"/> is null), newest first.</summary>
    Task<IReadOnlyList<VerificationRecord>> ListByContextAsync(Guid? contextOrgId, CancellationToken ct = default);

    /// <summary>Fetch a single record by id, or null if not found.</summary>
    Task<VerificationRecord?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Wipe the history (e.g. on sign-out).</summary>
    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>Outcome of a verification flow, persisted alongside the record.</summary>
/// <remarks>
/// Mirrors the trust panel's verdict at the time of the verification. Re-running
/// verification later may produce a different verdict (e.g. a now-revoked
/// credential); the historical record preserves the <i>original</i> verdict.
/// </remarks>
public enum VerifyOutcome
{
    /// <summary>All checks passed — credential valid, holder→device delegation intact, not revoked.</summary>
    Pass,
    /// <summary>At least one check returned a warning (e.g. status list unreachable, signature kid mismatch).</summary>
    Warn,
    /// <summary>At least one check failed (e.g. revoked, signature invalid, expired).</summary>
    Fail
}

/// <summary>
/// One past verification, persisted in IndexedDB.
/// </summary>
/// <param name="Id">Client-generated GUID — primary key in the <c>verifications</c> store.</param>
/// <param name="VerifiedAt">UTC time the verification ran.</param>
/// <param name="ContextOrgId">Context the user was acting under; null = Personal.</param>
/// <param name="HolderDisplayName">Display name of the credential's holder.</param>
/// <param name="IssuerOrgName">Display name of the issuing organisation.</param>
/// <param name="CredentialType">Credential VCT (e.g. <c>WaterEngineerCredential/v1</c>).</param>
/// <param name="Outcome">Trust panel's final verdict at the time of the verification.</param>
/// <param name="TrustPanelJson">Serialised state used to re-render the trust panel on tap.</param>
public sealed record VerificationRecord(
    Guid Id,
    DateTimeOffset VerifiedAt,
    Guid? ContextOrgId,
    string HolderDisplayName,
    string IssuerOrgName,
    string CredentialType,
    VerifyOutcome Outcome,
    string TrustPanelJson);

/// <summary>In-memory <see cref="IVerificationHistoryStore"/> for tests.</summary>
public sealed class InMemoryVerificationHistoryStore : IVerificationHistoryStore
{
    private readonly List<VerificationRecord> _records = new();

    /// <inheritdoc />
    public Task AddAsync(VerificationRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records.Add(record);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VerificationRecord>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<VerificationRecord>>(_records
            .OrderByDescending(r => r.VerifiedAt)
            .ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<VerificationRecord>> ListByContextAsync(Guid? contextOrgId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<VerificationRecord>>(_records
            .Where(r => r.ContextOrgId == contextOrgId)
            .OrderByDescending(r => r.VerifiedAt)
            .ToList());

    /// <inheritdoc />
    public Task<VerificationRecord?> GetAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_records.FirstOrDefault(r => r.Id == id));

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken ct = default)
    {
        _records.Clear();
        return Task.CompletedTask;
    }
}

/// <summary>IndexedDB-backed <see cref="IVerificationHistoryStore"/>.</summary>
public sealed class IndexedDbVerificationHistoryStore : IVerificationHistoryStore
{
    private const string StoreName = "verifications";

    private readonly IJSRuntime _js;
    /// <summary>Initialise a new instance.</summary>
    public IndexedDbVerificationHistoryStore(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    /// <inheritdoc />
    public async Task AddAsync(VerificationRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        // Store uses keyPath: "id", so we pass the record alone — IndexedDB extracts the key.
        await _js.InvokeVoidAsync("SorchaIndexedDb.put", ct, StoreName, record);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VerificationRecord>> ListAsync(CancellationToken ct = default)
    {
        var all = await _js.InvokeAsync<VerificationRecord[]?>("SorchaIndexedDb.getAll", ct, StoreName);
        if (all is null || all.Length == 0) return Array.Empty<VerificationRecord>();
        return all.OrderByDescending(r => r.VerifiedAt).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VerificationRecord>> ListByContextAsync(Guid? contextOrgId, CancellationToken ct = default)
    {
        var all = await ListAsync(ct).ConfigureAwait(false);
        return all.Where(r => r.ContextOrgId == contextOrgId).ToList();
    }

    /// <inheritdoc />
    public async Task<VerificationRecord?> GetAsync(Guid id, CancellationToken ct = default)
        => await _js.InvokeAsync<VerificationRecord?>("SorchaIndexedDb.get", ct, StoreName, id.ToString());

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken ct = default)
        => await _js.InvokeVoidAsync("SorchaIndexedDb.clear", ct, StoreName);
}
