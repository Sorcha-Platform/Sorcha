// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.CitizenWallet;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Per-row actions for the Activity presentation feed (Feature 114, US5 PR3).
/// UI-agnostic so the server-authoritative delete and its reframed FR-009 copy can
/// be unit-tested without rendering a component.
/// </summary>
public static class PresentationActivityActions
{
    /// <summary>Confirmation dialog title for removing a presentation from history.</summary>
    public const string RemoveConfirmTitle = "Remove from your history?";

    /// <summary>
    /// Reframed FR-009 confirmation copy: server-authoritative delete affects every
    /// device but not the verifier's own records (there is no register/ledger record).
    /// </summary>
    public const string RemoveConfirmBody =
        "This removes the presentation from your history on all your devices. "
        + "It does not affect the verifier's own records, or the credential itself.";

    /// <summary>
    /// Delete a presentation everywhere: server-authoritative row removal first (so
    /// the entry stays gone across all the citizen's devices), then the device-local
    /// copy. A local-only (unsynced) entry has no server row — the server delete is a
    /// harmless idempotent no-op. A transient server failure is swallowed; the caller
    /// reloads, so the displayed state reflects the truth rather than a false success.
    /// </summary>
    public static async Task DeleteEverywhereAsync(
        ICitizenWalletClient client,
        IPresentationLog log,
        Guid id,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(log);

        try
        {
            await client.DeletePresentationAsync(id, ct);
        }
        catch
        {
            // Transient server error — leave the server row as-is; the local delete
            // below + caller reload keep the UI consistent with server truth.
        }

        await log.DeleteAsync(id, ct);
    }
}
