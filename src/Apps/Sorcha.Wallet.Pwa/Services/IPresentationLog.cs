// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Per-device log of presentations the citizen has made — credentials they
/// showed to a verifier (Feature 114 US5). The inverse of
/// <see cref="IVerificationHistoryStore"/> (which records verifications the
/// citizen <i>performed</i> as a verifier). Surfaced chronologically on the
/// Activity page.
/// </summary>
/// <remarks>
/// Client-side only in this PR — a private notebook on the device. Entries
/// carry disclosed claim <i>names</i> only (never values) plus the verifier
/// label and timestamp. Server-side forwarding (so the platform writes the
/// F111 lifecycle record) lands in a follow-up PR; <see cref="PresentationLogEntry.SyncedToServer"/>
/// is the seam for it. Clearing site data wipes the log — matching the
/// verification-history precedent.
/// </remarks>
public interface IPresentationLog
{
    /// <summary>Append a presentation entry.</summary>
    Task AppendAsync(PresentationLogEntry entry, CancellationToken ct = default);

    /// <summary>List entries, newest first.</summary>
    Task<IReadOnlyList<PresentationLogEntry>> ListAsync(CancellationToken ct = default);

    /// <summary>Delete a single entry by id. Idempotent.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Wipe the log (e.g. on sign-out).</summary>
    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>
/// One presentation the citizen made, persisted in IndexedDB store
/// <c>presentationLog</c> (keyPath <c>id</c>).
/// </summary>
/// <param name="Id">Client-generated GUID — primary key.</param>
/// <param name="PresentedAt">UTC time the presentation was sent.</param>
/// <param name="CredentialType">Credential VCT presented (e.g. <c>AssuredIdentityCredential/v1</c>).</param>
/// <param name="CredentialLabel">Issuer-supplied display label, if any.</param>
/// <param name="VerifierLabel">Human-readable verifier identity — the request purpose, falling back to the OID4VP client_id.</param>
/// <param name="DisclosedClaims">Names of the claims disclosed (never values).</param>
/// <param name="Outcome">Wallet-observed outcome of the presentation.</param>
/// <param name="CredentialId">Local cache id of the credential presented. Carried so the
/// server-forwarding drain (US5 PR2) can populate the wire contract's correlation field.
/// Defaults to <see cref="Guid.Empty"/> for entries written before PR2 — those are skipped
/// by the drain since they cannot form a valid report.</param>
/// <param name="SyncedToServer">True once forwarded to the platform lifecycle record. Set by
/// the sync drain on a 202 from <c>POST /api/v1/wallet/presentations/log</c>.</param>
public sealed record PresentationLogEntry(
    Guid Id,
    DateTimeOffset PresentedAt,
    string CredentialType,
    string? CredentialLabel,
    string VerifierLabel,
    IReadOnlyList<string> DisclosedClaims,
    PresentationLogOutcome Outcome,
    Guid CredentialId = default,
    bool SyncedToServer = false);

/// <summary>Wallet-observed outcome of a presentation.</summary>
public enum PresentationLogOutcome
{
    /// <summary>Verifier accepted the posted presentation (2xx from response_uri).</summary>
    Sent = 0,

    /// <summary>Verifier rejected or returned a non-success status.</summary>
    Rejected = 1,
}

/// <summary>In-memory <see cref="IPresentationLog"/> for tests.</summary>
public sealed class InMemoryPresentationLog : IPresentationLog
{
    private readonly List<PresentationLogEntry> _entries = new();

    /// <inheritdoc />
    public Task AppendAsync(PresentationLogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // Mirror IndexedDB put semantics: upsert by id so re-appending an entry
        // (e.g. to flip SyncedToServer) replaces it rather than duplicating it.
        _entries.RemoveAll(e => e.Id == entry.Id);
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PresentationLogEntry>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PresentationLogEntry>>(
            _entries.OrderByDescending(e => e.PresentedAt).ToList());

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _entries.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken ct = default)
    {
        _entries.Clear();
        return Task.CompletedTask;
    }
}

/// <summary>IndexedDB-backed <see cref="IPresentationLog"/>.</summary>
public sealed class IndexedDbPresentationLog : IPresentationLog
{
    private const string StoreName = "presentationLog";

    private readonly IJSRuntime _js;

    /// <summary>Initialise a new instance.</summary>
    public IndexedDbPresentationLog(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    /// <inheritdoc />
    public async Task AppendAsync(PresentationLogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // Store uses keyPath "id"; pass the record alone — IndexedDB extracts the key.
        await _js.InvokeVoidAsync("SorchaIndexedDb.put", ct, StoreName, entry);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PresentationLogEntry>> ListAsync(CancellationToken ct = default)
    {
        var all = await _js.InvokeAsync<PresentationLogEntry[]?>("SorchaIndexedDb.getAll", ct, StoreName);
        if (all is null || all.Length == 0) return Array.Empty<PresentationLogEntry>();
        return all.OrderByDescending(e => e.PresentedAt).ToList();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        => await _js.InvokeVoidAsync("SorchaIndexedDb.del", ct, StoreName, id.ToString());

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken ct = default)
        => await _js.InvokeVoidAsync("SorchaIndexedDb.clear", ct, StoreName);
}
